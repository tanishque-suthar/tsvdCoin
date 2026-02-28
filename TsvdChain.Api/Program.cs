using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using TsvdChain.Api;
using TsvdChain.Core.Blockchain;
using TsvdChain.Core.Crypto;
using TsvdChain.Core.Mempool;
using TsvdChain.Core.Mining;
using TsvdChain.P2P;
using TsvdChain.P2P.Configuration;
using TsvdChain.Api.Dto;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddOpenApi();
builder.Services.AddSignalR(options =>
{
    options.MaximumParallelInvocationsPerClient = 1;
});

builder.Services.Configure<SeedNodeOptions>(
    builder.Configuration.GetSection(SeedNodeOptions.SectionName));

// Wallet: unlock or create on startup.
var walletPassword = builder.Configuration["Wallet:Password"]
    ?? throw new InvalidOperationException(
        "Wallet password is required. Set 'Wallet:Password' in config or WALLET__PASSWORD env var.");

var walletDir = Path.Combine(builder.Environment.ContentRootPath, "Data");
var walletStore = new WalletStore(walletDir);
KeyPair wallet;

if (walletStore.WalletExists())
{
    wallet = walletStore.UnlockWallet(walletPassword);
    Console.WriteLine($"Wallet unlocked. Address: {wallet.PublicKeyHex}");
}
else
{
    wallet = walletStore.CreateWallet(walletPassword);
    Console.WriteLine($"New wallet created. Address: {wallet.PublicKeyHex}");
}

builder.Services.AddSingleton(wallet);
builder.Services.AddSingleton(walletStore);

builder.Services.AddSingleton<Blockchain>();
builder.Services.AddSingleton<MempoolService>();
builder.Services.AddSingleton<MinerService>(sp => new MinerService(
    sp.GetRequiredService<Blockchain>(),
    sp.GetRequiredService<MempoolService>(),
    sp.GetRequiredService<KeyPair>().PublicKeyHex));
builder.Services.AddSingleton<IBlockchainStore, JsonBlockchainStore>();
builder.Services.AddSingleton<BlockchainNodeService>();
builder.Services.AddSingleton<TsvdChain.P2P.IBlockchainNodeService>(sp => sp.GetRequiredService<BlockchainNodeService>());
builder.Services.AddSingleton<PeerConnectionService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// REST API endpoints
app.MapGet("/chain", (BlockchainNodeService node) =>
{
    return Results.Ok(node.GetChain());
});

app.MapGet("/latest-block", (BlockchainNodeService node) =>
{
    var latest = node.GetLatestBlock();
    return latest is null ? Results.NotFound() : Results.Ok(latest);
});

app.MapPost("/mine", async (BlockchainNodeService node, CancellationToken ct) =>
{
    var block = await node.MineBlockAsync(ct);
    return Results.Ok(block);
});

app.MapGet("/peers", (PeerConnectionService peers) =>
{
    return Results.Ok(new
    {
        Count = peers.PeerCount,
        Peers = peers.GetAllPeers()
    });
});

// SignalR hub
app.MapHub<BlockchainHub>("/blockchainHub");

// Load persisted blockchain from store before accepting traffic.
await app.Services.GetRequiredService<BlockchainNodeService>().InitializeFromStoreAsync();

// Optional seed-node bootstrap for initial sync.
var seedOptions = app.Services.GetRequiredService<IOptions<SeedNodeOptions>>().Value;
if (seedOptions.EnableSeedNodes && seedOptions.Nodes.Count > 0)
{
    _ = Task.Run(async () =>
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        var nodeService = app.Services.GetRequiredService<BlockchainNodeService>();

        foreach (var nodeUrl in seedOptions.Nodes)
        {
            try
            {
                var connection = new HubConnectionBuilder()
                    .WithUrl($"{nodeUrl}/blockchainHub")
                    .WithAutomaticReconnect()
                    .Build();

                // Handle full chain sync responses.
                connection.On<IEnumerable<Block>>("ReceiveChain", async chain =>
                {
                    await nodeService.TryReplaceChainAsync(chain);
                });

                // Handle individual block broadcasts from peers.
                connection.On<Block>("ReceiveBlock", async block =>
                {
                    if (await nodeService.TryAcceptBlockAsync(block))
                    {
                        logger.LogInformation("Accepted block {Index} from seed peer {Url}", block.Index, nodeUrl);
                    }
                });

                // Handle chain requests from the remote hub.
                connection.On("RequestChain", async () =>
                {
                    var chain = nodeService.GetChain();
                    await connection.InvokeAsync("SubmitChain", chain);
                });

                await connection.StartAsync();

                // Store connection so we can broadcast to it later.
                nodeService.AddOutboundConnection(connection);

                logger.LogInformation("Connected to seed node {NodeUrl}", nodeUrl);

                // Request initial chain sync — SendChain sends the
                // remote node's chain back to us via ReceiveChain.
                await connection.InvokeAsync("SendChain");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to connect to seed node {NodeUrl}", nodeUrl);
            }
        }
    });
}

// Wire mempool, miner, and wallet into node service
var nodeService = app.Services.GetRequiredService<BlockchainNodeService>();
nodeService.Mempool = app.Services.GetRequiredService<MempoolService>();
nodeService.Miner = app.Services.GetRequiredService<MinerService>();
nodeService.Wallet = app.Services.GetRequiredService<KeyPair>();


// API endpoints for mempool and miner
app.MapGet("/address", (KeyPair kp) => Results.Ok(new { Address = kp.PublicKeyHex }));

app.MapPost("/tx", (BlockchainNodeService node, TxDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.From) || string.IsNullOrWhiteSpace(dto.To))
        return Results.BadRequest("From and To addresses must not be empty.");
    if (dto.Amount <= 0)
        return Results.BadRequest("Amount must be greater than zero.");
    if (string.IsNullOrWhiteSpace(dto.Signature))
        return Results.BadRequest("Signature is required.");

    // Reconstruct transaction from pre-signed DTO fields.
    var tx = new Transaction
    {
        From = dto.From,
        To = dto.To,
        Amount = dto.Amount,
        Timestamp = dto.Timestamp,
        Signature = dto.Signature,
        Id = dto.Id
    };

    if (!tx.ValidateSignature())
        return Results.BadRequest("Invalid signature.");

    if (node.Mempool?.AddTransaction(tx) == true)
        return Results.Accepted(null, tx);
    return Results.Conflict("Transaction already in mempool.");
});

// Convenience: sign a transaction with this node's wallet and add to mempool.
app.MapPost("/tx/send", (BlockchainNodeService node, KeyPair kp, SendTxDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.To))
        return Results.BadRequest("To address must not be empty.");
    if (dto.Amount <= 0)
        return Results.BadRequest("Amount must be greater than zero.");

    var tx = Transaction.CreateSigned(kp, dto.To, dto.Amount);

    if (node.Mempool?.AddTransaction(tx) == true)
        return Results.Accepted(null, tx);
    return Results.Conflict("Transaction already in mempool.");
});

app.MapGet("/mempool", (BlockchainNodeService node) =>
{
    return Results.Ok(node.Mempool?.GetTransactions() ?? Array.Empty<Transaction>());
});

app.MapPost("/miner/start", (BlockchainNodeService node) =>
{
    node.Miner?.Start();
    return Results.Accepted();
});

app.MapPost("/miner/stop", (BlockchainNodeService node) =>
{
    node.Miner?.Stop();
    return Results.Accepted();
});

app.Run();
