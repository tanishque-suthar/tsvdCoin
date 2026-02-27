using System.Collections.Concurrent;
using TsvdChain.Core.Blockchain;

namespace TsvdChain.Core.Mempool;

/// <summary>
/// Concurrent mempool using ConcurrentDictionary with TryUpdate semantics.
/// Stores transactions by Id. Lightweight validation (signature placeholder) is performed by caller.
/// </summary>
public sealed class MempoolService
{
    private readonly ConcurrentDictionary<string, Transaction> _txs = new();

    public int Count => _txs.Count;

    public bool AddTransaction(Transaction tx)
    {
        return _txs.TryAdd(tx.Id, tx);
    }

    public bool TryUpdateTransaction(string id, Transaction newTx)
    {
        if (_txs.TryGetValue(id, out var existing))
        {
            return _txs.TryUpdate(id, newTx, existing);
        }
        return false;
    }

    public bool RemoveTransaction(string id)
    {
        return _txs.TryRemove(id, out _);
    }

    public IReadOnlyList<Transaction> GetTransactions(int max = 100)
    {
        return _txs.Values.Take(max).ToList().AsReadOnly();
    }

    public void Clear() => _txs.Clear();
}