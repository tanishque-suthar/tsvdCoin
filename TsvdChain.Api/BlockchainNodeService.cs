using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using TsvdChain.Core.Blockchain;
using TsvdChain.Core.Crypto;
using TsvdChain.Core.Mempool;
using TsvdChain.Core.Mining;
using TsvdChain.P2P;

namespace TsvdChain.Api;

public interface IBlockchainStore
{
    Task<IReadOnlyList<Block>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyList<Block> chain, CancellationToken cancellationToken = default);
}

public sealed class JsonBlockchainStore : IBlockchainStore
{
    private readonly string _filePath;

    public JsonBlockchainStore(IWebHostEnvironment env)
    {
        var dataDirectory = Path.Combine(env.ContentRootPath, "Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "chain.json");
    }

    public async Task<IReadOnlyList<Block>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<Block>();
        }

        await using var stream = File.OpenRead(_filePath);
        var chain = await JsonSerializer.DeserializeAsync<List<Block>>(stream, cancellationToken: cancellationToken);
        return chain?.AsReadOnly() ?? new List<Block>().AsReadOnly();
    }

    public async Task SaveAsync(IReadOnlyList<Block> chain, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, chain, cancellationToken: cancellationToken, options: new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}

public sealed class BlockchainNodeService : TsvdChain.P2P.IBlockchainNodeService
{
    private readonly Blockchain _blockchain;
    private readonly IBlockchainStore _store;
    private readonly ILogger<BlockchainNodeService> _logger;
    private readonly IHubContext<BlockchainHub> _hubContext;
    private readonly object _lock = new();
    private readonly ConcurrentBag<HubConnection> _outboundConnections = new();

    public BlockchainNodeService(
        Blockchain blockchain,
        IBlockchainStore store,
        ILogger<BlockchainNodeService> logger,
        IHubContext<BlockchainHub> hubContext)
    {
        _blockchain = blockchain;
        _store = store;
        _logger = logger;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Register an outbound SignalR connection (e.g. to a seed node)
    /// so mined blocks can be broadcast to it.
    /// </summary>
    public void AddOutboundConnection(HubConnection connection)
    {
        _outboundConnections.Add(connection);
    }

    // Expose mempool and miner for integration.
    public MempoolService? Mempool { get; set; }
    public MinerService? Miner { get; set; }

    /// <summary>
    /// The wallet key pair, set at startup after unlocking/creating the wallet.
    /// </summary>
    public KeyPair? Wallet { get; set; }

    public IReadOnlyList<Block> GetChain()
    {
        lock (_lock)
        {
            return _blockchain.Chain;
        }
    }

    public Block? GetLatestBlock()
    {
        lock (_lock)
        {
            return _blockchain.GetLatestBlock();
        }
    }

    /// <summary>
    /// Try to add a transaction to the mempool. Used by the hub for gossip and by the API.
    /// </summary>
    public bool TryAddToMempool(Transaction tx)
    {
        return Mempool?.AddTransaction(tx) == true;
    }

    /// <summary>
    /// Broadcasts a transaction to all connected peers (inbound hub clients + outbound seed connections).
    /// </summary>
    public async Task BroadcastTransactionAsync(Transaction tx)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveTransaction", tx);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast transaction {Id} to hub clients", tx.Id);
        }

        foreach (var conn in _outboundConnections)
        {
            try
            {
                if (conn.State == HubConnectionState.Connected)
                {
                    await conn.InvokeAsync("SubmitTransaction", tx);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast transaction {Id} to outbound peer", tx.Id);
            }
        }
    }

    /// <summary>
    /// Compute the balance for an address by scanning all transactions on the chain.
    /// </summary>
    public long GetBalance(string address)
    {
        lock (_lock)
        {
            long balance = 0;
            foreach (var block in _blockchain.Chain)
            {
                foreach (var tx in block.Transactions)
                {
                    if (tx.To == address)
                        balance += tx.Amount;
                    if (tx.From == address)
                        balance -= tx.Amount;
                }
            }
            return balance;
        }
    }

    public async Task<Block> MineBlockAsync(CancellationToken cancellationToken = default)
    {
        if (Miner is null)
            throw new InvalidOperationException("Miner is not configured.");

        var newBlock = await Miner.MineOneBlockAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Mined new block {Index} with hash {Hash}", newBlock.Index, newBlock.Hash);

        await PersistAsync(cancellationToken).ConfigureAwait(false);

        // Broadcast the new block to all peers.
        await BroadcastBlockAsync(newBlock).ConfigureAwait(false);

        return newBlock;
    }

    /// <summary>
    /// Broadcasts a block to all connected peers (inbound hub clients + outbound seed connections).
    /// </summary>
    private async Task BroadcastBlockAsync(Block block)
    {
        // Inbound peers connected to our SignalR hub.
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveBlock", block);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast block {Index} to hub clients", block.Index);
        }

        // Outbound connections (seed nodes we connected to as a client).
        foreach (var conn in _outboundConnections)
        {
            try
            {
                if (conn.State == HubConnectionState.Connected)
                {
                    await conn.InvokeAsync("SubmitBlock", block);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast block {Index} to outbound peer", block.Index);
            }
        }
    }

    public async Task<bool> TryAcceptBlockAsync(Block block, CancellationToken cancellationToken = default)
    {
        var added = false;

        lock (_lock)
        {
            added = _blockchain.AddBlock(block);
            if (added)
            {
                _logger.LogInformation("Accepted block {Index} from peer", block.Index);
            }
            else
            {
                _logger.LogWarning("Rejected block {Index} from peer", block.Index);
            }
        }

        if (added)
        {
            // Remove confirmed transactions from the mempool.
            Mempool?.RemoveConfirmed(block.Transactions);

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }

        return added;
    }

    public async Task<bool> TryReplaceChainAsync(IEnumerable<Block> remoteChain, CancellationToken cancellationToken = default)
    {
        var remoteList = remoteChain.OrderBy(b => b.Index).ToList();
        if (remoteList.Count == 0)
        {
            return false;
        }

        // Validate the remote chain without touching the live chain.
        if (!Blockchain.IsValidChain(remoteList.AsReadOnly()))
        {
            _logger.LogWarning("Remote chain failed structural validation; rejecting.");
            return false;
        }

        lock (_lock)
        {
            if (remoteList.Count <= _blockchain.Chain.Count)
            {
                return false; // Not longer than current chain.
            }

            _blockchain.ReplaceChain(remoteList);
        }

        // Persist the new canonical chain.
        await _store.SaveAsync(remoteList.AsReadOnly(), cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Replaced local chain with remote chain of length {Length}", remoteList.Count);
        return true;
    }

    public async Task InitializeFromStoreAsync(CancellationToken cancellationToken = default)
    {
        var storedChain = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (storedChain.Count == 0)
        {
            return;
        }

        if (!Blockchain.IsValidChain(storedChain))
        {
            _logger.LogWarning("Stored chain failed validation; ignoring persisted chain.");
            return;
        }

        lock (_lock)
        {
            _blockchain.ReplaceChain(storedChain);
        }

        _logger.LogInformation("Loaded blockchain from store with {Length} blocks", storedChain.Count);
    }

    private Task PersistAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Block> snapshot;
        lock (_lock)
        {
            snapshot = _blockchain.Chain.ToList().AsReadOnly();
        }

        return _store.SaveAsync(snapshot, cancellationToken);
    }
}

