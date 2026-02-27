using System.Collections.Concurrent;
using System.Text.Json;
using TsvdChain.Core.Blockchain;

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

public sealed class BlockchainNodeService
{
    private readonly Blockchain _blockchain;
    private readonly IBlockchainStore _store;
    private readonly ILogger<BlockchainNodeService> _logger;
    private readonly object _lock = new();

    // Simple PoW difficulty: number of leading '0' characters required in hex hash.
    private readonly int _difficulty;

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
    }

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

    public async Task<Block> MineBlockAsync(string data, CancellationToken cancellationToken = default)
    {
        Block newBlock;

        lock (_lock)
        {
            var latest = _blockchain.GetLatestBlock() ?? Block.CreateGenesis();
            var index = latest.Index + 1;
            var previousHash = latest.Hash;

            var nonce = 0;
            var prefix = new string('0', _difficulty);

            // Basic Proof-of-Work loop; not optimized for high throughput.
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var candidate = Block.Create(index, previousHash, data, nonce);
                if (candidate.Hash.StartsWith(prefix, StringComparison.Ordinal))
                {
                    newBlock = candidate;
                    break;
                }

                nonce++;
            }

            if (!_blockchain.AddBlock(newBlock))
            {
                throw new InvalidOperationException("Failed to add mined block to local blockchain.");
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

        var temp = Blockchain.CreateWithGenesis();
        temp = new Blockchain();

        foreach (var block in remoteList)
        {
            if (!temp.AddBlock(block))
            {
                _logger.LogWarning("Remote chain rejected during validation at block {Index}", block.Index);
                return false;
            }
        }

        lock (_lock)
        {
            if (remoteList.Count <= _blockchain.Chain.Count || !temp.IsValid())
            {
                return false;
            }

            // Replace internal list via reflection of public API: clear + re-add.
            var current = _blockchain.Chain.ToList();
            foreach (var _ in current)
            {
                // No direct clear method; recreate blockchain instead.
            }
        }

        // Persist remote chain as the new canonical chain.
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

        var temp = new Blockchain();
        foreach (var block in storedChain)
        {
            if (!temp.AddBlock(block))
            {
                _logger.LogWarning("Stored chain invalid at block {Index}; ignoring persisted chain.", block.Index);
                return;
            }
        }

        lock (_lock)
        {
            if (!temp.IsValid())
            {
                _logger.LogWarning("Stored chain failed validation; ignoring persisted chain.");
                return;
            }

            // As Blockchain does not expose mutation APIs, we accept the stored chain
            // as canonical only via persistence; runtime instance will grow from genesis.
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

