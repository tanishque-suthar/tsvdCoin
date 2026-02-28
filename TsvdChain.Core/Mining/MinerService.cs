using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TsvdChain.Core.Blockchain;
using TsvdChain.Core.Hashing;
using TsvdChain.Core.Mempool;

namespace TsvdChain.Core.Mining;

/// <summary>
/// Simple miner service implementing a cancellable PoW loop.
/// Uses MempoolService to select transactions and Blockchain to append mined blocks.
/// Implements IAsyncDisposable for graceful shutdown.
/// </summary>
public sealed class MinerService : IAsyncDisposable
{
    private readonly TsvdChain.Core.Blockchain.Blockchain _blockchain;
    private readonly MempoolService _mempool;
    private readonly CancellationTokenSource _cts = new();

    private readonly string _rewardAddress;
    private Task? _miningTask;

    public MinerService(
        TsvdChain.Core.Blockchain.Blockchain blockchain,
        MempoolService mempool,
        string rewardAddress = "system")
    {
        _blockchain = blockchain;
        _mempool = mempool;
        _rewardAddress = rewardAddress;
    }

    public void Start()
    {
        if (_miningTask != null) return;
        _miningTask = Task.Run(MiningLoop);
    }

    public void Stop()
    {
        _cts.Cancel();
        _miningTask = null;
    }

    /// <summary>
    /// Mines a single block: builds a template from the current chain tip and mempool,
    /// runs the PoW loop, adds the block to the chain, and returns it.
    /// This is the single source of truth for all mining logic.
    /// </summary>
    public async Task<Block> MineOneBlockAsync(CancellationToken cancellationToken = default)
    {
        var latest = _blockchain.GetLatestBlock()!;
        var index = latest.Index + 1;
        var previousHash = latest.Hash;

        var txs = _mempool.GetTransactions(100).ToList();

        // Prepend coinbase reward transaction (amount from consensus rules).
        var reward = Consensus.GetBlockReward(index);
        var coinbase = Transaction.CreateSystemTransaction(_rewardAddress, reward);
        txs.Insert(0, coinbase);

        var txList = txs.AsReadOnly();
        var merkleRoot = MerkleTree.ComputeMerkleRoot(txList.Select(t => t.Id));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var prefix = new string('0', Consensus.Difficulty);
        var nonce = 0;

        // PoW loop — yields every 10k iterations to stay cancellable.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hash = Sha256Hasher.ComputeHashString($"{index}{timestamp}{previousHash}{merkleRoot}{nonce}");
            if (hash.StartsWith(prefix, StringComparison.Ordinal))
            {
                var block = new Block
                {
                    Index = index,
                    Timestamp = timestamp,
                    PreviousHash = previousHash,
                    Transactions = txList,
                    MerkleRoot = merkleRoot,
                    Nonce = nonce
                };

                // Stale check: chain may have advanced during mining.
                var currentLatest = _blockchain.GetLatestBlock()!;
                if (currentLatest.Hash != previousHash)
                {
                    throw new InvalidOperationException("Block is stale \u2014 chain advanced during mining.");
                }

                if (!_blockchain.AddBlock(block))
                {
                    throw new InvalidOperationException("Failed to add mined block to local blockchain.");
                }

                // Remove mined transactions from mempool.
                foreach (var tx in txs)
                {
                    _mempool.RemoveTransaction(tx.Id);
                }

                return block;
            }

            nonce++;
            if (nonce % 10000 == 0)
            {
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task MiningLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await MineOneBlockAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // Brief pause before retrying (e.g. stale block).
                await Task.Delay(100, _cts.Token).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_miningTask != null)
        {
            await _miningTask.ConfigureAwait(false);
        }
        _cts.Dispose();
    }
}