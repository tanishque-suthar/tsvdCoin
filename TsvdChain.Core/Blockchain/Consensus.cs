namespace TsvdChain.Core.Blockchain;

/// <summary>
/// Hardcoded consensus rules enforced by every node in the network.
/// These values cannot be configured — changing them makes your blocks
/// invalid to every other honest node.
/// </summary>
public static class Consensus
{
    /// <summary>
    /// Block reward starts at 50 coins.
    /// </summary>
    public const long InitialBlockReward = 50;

    /// <summary>
    /// Reward halves every 210,000 blocks (~4 years at 10 min/block in Bitcoin).
    /// </summary>
    public const int HalvingInterval = 210_000;

    /// <summary>
    /// PoW difficulty: number of leading '0' hex characters required in block hash.
    /// </summary>
    public const int Difficulty = 3;

    /// <summary>
    /// The "From" address used for coinbase (mining reward) transactions.
    /// </summary>
    public const string CoinbaseFrom = "system";

    /// <summary>
    /// Returns the allowed block reward for a given block height.
    /// Halves every <see cref="HalvingInterval"/> blocks, reaching 0 after 64 halvings.
    /// </summary>
    public static long GetBlockReward(int blockHeight)
    {
        var halvings = blockHeight / HalvingInterval;
        if (halvings >= 64) return 0;
        return InitialBlockReward >> halvings;
    }

    /// <summary>
    /// Validates that a block's coinbase transaction follows consensus rules:
    /// - First transaction must be from "system"
    /// - Amount must not exceed the allowed reward for this block height
    /// </summary>
    public static bool ValidateCoinbase(Block block)
    {
        if (block.Transactions.Count == 0) return false;

        var coinbase = block.Transactions[0];
        if (coinbase.From != CoinbaseFrom) return false;
        if (coinbase.Amount > GetBlockReward(block.Index)) return false;

        return true;
    }

    /// <summary>
    /// Validates that a block's hash meets the required PoW difficulty.
    /// </summary>
    public static bool ValidateDifficulty(Block block)
    {
        var prefix = new string('0', Difficulty);
        return block.Hash.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates that every non-coinbase transaction in the block has sufficient balance,
    /// given the balances accumulated from all preceding blocks in the chain.
    /// </summary>
    /// <param name="chain">All blocks up to (but not including) the block being validated.</param>
    /// <param name="block">The block to validate.</param>
    public static bool ValidateBalances(IReadOnlyList<Block> chain, Block block)
    {
        // Build running balance from all prior blocks.
        var balances = new Dictionary<string, long>();
        foreach (var b in chain)
        {
            foreach (var tx in b.Transactions)
            {
                if (tx.From != CoinbaseFrom)
                {
                    balances.TryGetValue(tx.From, out var fromBal);
                    balances[tx.From] = fromBal - tx.Amount;
                }
                balances.TryGetValue(tx.To, out var toBal);
                balances[tx.To] = toBal + tx.Amount;
            }
        }

        // Validate each non-coinbase transaction in the new block.
        foreach (var tx in block.Transactions)
        {
            if (tx.From == CoinbaseFrom) continue;

            balances.TryGetValue(tx.From, out var senderBalance);
            if (tx.Amount > senderBalance)
                return false;

            // Update running balance so subsequent txs in the same block are checked correctly.
            balances[tx.From] = senderBalance - tx.Amount;
            balances.TryGetValue(tx.To, out var receiverBalance);
            balances[tx.To] = receiverBalance + tx.Amount;
        }

        return true;
    }
}
