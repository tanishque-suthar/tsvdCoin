using Microsoft.AspNetCore.SignalR.Client;
using TsvdChain.Api;
using TsvdChain.Core.Blockchain;
using TsvdChain.P2P;
using TsvdChain.P2P.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

builder.Services.Configure<SeedNodeOptions>(
    builder.Configuration.GetSection(SeedNodeOptions.SectionName));

builder.Services.AddSingleton<Blockchain>(_ => Blockchain.CreateWithGenesis());
builder.Services.AddSingleton<IBlockchainStore, JsonBlockchainStore>();
builder.Services.AddSingleton<BlockchainNodeService>();
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

app.MapPost("/mine", async (BlockchainNodeService node, string data, CancellationToken ct) =>
{
    var block = await node.MineBlockAsync(data, ct);
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

// Optional seed-node bootstrap for initial sync.
var seedOptions = app.Services.GetService<IConfiguration>()?.GetSection("SeedNodes");
if (seedOptions is not null)
{
    var nodes = seedOptions.Get<string[]>() ?? Array.Empty<string>();
    if (nodes.Length > 0)
    {
        _ = Task.Run(async () =>
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            var nodeService = app.Services.GetRequiredService<BlockchainNodeService>();

            foreach (var nodeUrl in nodes)
            {
                try
                {
                    var connection = new HubConnectionBuilder()
                        .WithUrl($"{nodeUrl}/blockchainHub")
                        .WithAutomaticReconnect()
                        .Build();

                    await connection.StartAsync();

                    logger.LogInformation("Connected to seed node {NodeUrl}", nodeUrl);

                    // Request chain sync from seed node.
                    await connection.InvokeAsync("RequestChainSync");

                    connection.On<IEnumerable<Block>>("ReceiveChain", async chain =>
                    {
                        await nodeService.TryReplaceChainAsync(chain);
                    });
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to connect to seed node {NodeUrl}", nodeUrl);
                }
            }
        });
    }
}

app.Run();
