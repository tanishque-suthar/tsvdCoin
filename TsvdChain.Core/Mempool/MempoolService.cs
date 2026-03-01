using System.Collections.Concurrent;
using TsvdChain.Core.Blockchain;

namespace TsvdChain.Core.Mempool;

/// <summary>
/// Concurrent mempool using ConcurrentDictionary with TryUpdate semantics.
/// Validates signature and balance before accepting transactions.
/// </summary>
public sealed class MempoolService
{
    private readonly ConcurrentDictionary<string, Transaction> _txs = new();
    private readonly Func<string, long> _getConfirmedBalance;

    public int Count => _txs.Count;

    /// <param name="getConfirmedBalance">
    /// Returns the confirmed on-chain balance for a given address.
    /// Used to reject transactions that would overdraw an account.
    /// </param>
    public MempoolService(Func<string, long> getConfirmedBalance)
    {
        _getConfirmedBalance = getConfirmedBalance;
    }

    public bool AddTransaction(Transaction tx)
    {
        if (!tx.ValidateSignature())
        {
            return false;
        }

        // System (coinbase) transactions skip balance check.
        if (tx.From != Consensus.CoinbaseFrom)
        {
            var confirmedBalance = _getConfirmedBalance(tx.From);

            // Also account for pending mempool transactions from the same sender.
            var pendingSpend = _txs.Values
                .Where(t => t.From == tx.From)
                .Sum(t => t.Amount);

            if (tx.Amount > confirmedBalance - pendingSpend)
            {
                return false; // Insufficient balance
            }
        }

        return _txs.TryAdd(tx.Id, tx);
    }

    public bool RemoveTransaction(string id)
    {
        return _txs.TryRemove(id, out _);
    }

    /// <summary>
    /// Remove all transactions present in the given block (they are now confirmed).
    /// </summary>
    public void RemoveConfirmed(IEnumerable<Transaction> confirmed)
    {
        foreach (var tx in confirmed)
        {
            _txs.TryRemove(tx.Id, out _);
        }
    }

    public IReadOnlyList<Transaction> GetTransactions(int max = 100)
    {
        return _txs.Values.Take(max).ToList().AsReadOnly();
    }

    public void Clear() => _txs.Clear();
}