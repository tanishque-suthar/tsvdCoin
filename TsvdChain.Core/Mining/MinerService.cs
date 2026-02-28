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

    private readonly int _difficulty;
    private readonly string _rewardAddress;
    private readonly long _blockReward;
    private Task? _miningTask;

    public MinerService(
        TsvdChain.Core.Blockchain.Blockchain blockchain,
        MempoolService mempool,
        int difficulty = 3,
        string rewardAddress = "system",
        long blockReward = 50)
    {
        _blockchain = blockchain;
        _mempool = mempool;
        _difficulty = difficulty;
        _rewardAddress = rewardAddress;
        _blockReward = blockReward;
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

    private async Task MiningLoop()
    {
        var prefix = new string('0', _difficulty);

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var latest = _blockchain.GetLatestBlock()!;
                var index = latest.Index + 1;
                var previousHash = latest.Hash;

                var txs = _mempool.GetTransactions(100).ToList();

                // Prepend coinbase reward transaction.
                var coinbase = Transaction.CreateSystemTransaction(_rewardAddress, _blockReward);
                txs.Insert(0, coinbase);

                var txList = txs.AsReadOnly();
                var merkleRoot = MerkleTree.ComputeMerkleRoot(txList.Select(t => t.Id));
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var nonce = 0;

                while (!_cts.IsCancellationRequested)
                {
                    var hash = Sha256Hasher.ComputeHashString($"{index}{timestamp}{previousHash}{merkleRoot}{nonce}");
                    if (hash.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        // Only create the block once a valid nonce is found.
                        var block = new Block
                        {
                            Index = index,
                            Timestamp = timestamp,
                            PreviousHash = previousHash,
                            Transactions = txList,
                            MerkleRoot = merkleRoot,
                            Nonce = nonce
                        };

                        if (_blockchain.AddBlock(block))
                        {
                            foreach (var tx in txs)
                            {
                                _mempool.RemoveTransaction(tx.Id);
                            }
                        }
                        break;
                    }

                    nonce++;
                    if (nonce % 10000 == 0)
                    {
                        await Task.Delay(1, _cts.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(100, _cts.Token).ConfigureAwait(false); }
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