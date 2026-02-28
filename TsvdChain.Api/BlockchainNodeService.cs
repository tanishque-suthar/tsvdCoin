using System.Collections.Concurrent;
using System.Text.Json;
using TsvdChain.Core.Blockchain;
using TsvdChain.Core.Crypto;
using TsvdChain.Core.Hashing;
using TsvdChain.Core.Mempool;
using TsvdChain.Core.Mining;

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
    private readonly object _lock = new();

    // Simple PoW difficulty: number of leading '0' characters required in hex hash.
    private readonly int _difficulty;
    private readonly long _blockReward;

    public BlockchainNodeService(
        Blockchain blockchain,
        IBlockchainStore store,
        ILogger<BlockchainNodeService> logger,
        IConfiguration configuration)
    {
        _blockchain = blockchain;
        _store = store;
        _logger = logger;
        _difficulty = configuration.GetValue("Blockchain:Difficulty", 3);
        _blockReward = configuration.GetValue<long>("Blockchain:BlockReward", 50);
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

    public async Task<Block> MineBlockAsync(CancellationToken cancellationToken = default)
    {
        Block newBlock;

        lock (_lock)
        {
            var latest = _blockchain.GetLatestBlock()!;
            var index = latest.Index + 1;
            var previousHash = latest.Hash;

            var txs = (Mempool?.GetTransactions(100) ?? []).ToList();

            // Prepend coinbase reward.
            var rewardAddress = Wallet?.PublicKeyHex ?? "system";
            var coinbase = Transaction.CreateSystemTransaction(rewardAddress, _blockReward);
            txs.Insert(0, coinbase);

            var txList = txs.AsReadOnly();
            var merkleRoot = MerkleTree.ComputeMerkleRoot(txList.Select(t => t.Id));
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var nonce = 0;
            var prefix = new string('0', _difficulty);

            // PoW loop: hash raw header values, zero allocations per iteration.
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var hash = Sha256Hasher.ComputeHashString($"{index}{timestamp}{previousHash}{merkleRoot}{nonce}");
                if (hash.StartsWith(prefix, StringComparison.Ordinal))
                {
                    newBlock = new Block
                    {
                        Index = index,
                        Timestamp = timestamp,
                        PreviousHash = previousHash,
                        Transactions = txList,
                        MerkleRoot = merkleRoot,
                        Nonce = nonce
                    };
                    break;
                }

                nonce++;
            }

            if (!_blockchain.AddBlock(newBlock))
            {
                throw new InvalidOperationException("Failed to add mined block to local blockchain.");
            }

            // Remove mined transactions from mempool.
            foreach (var tx in txList)
            {
                Mempool?.RemoveTransaction(tx.Id);
            }

            _logger.LogInformation("Mined new block {Index} with hash {Hash}", newBlock.Index, newBlock.Hash);
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);

        return newBlock;
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

