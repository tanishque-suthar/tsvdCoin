namespace TsvdChain.Core.Blockchain;

/// <summary>
/// Represents a blockchain - a growing list of immutable blocks.
/// </summary>
public sealed class Blockchain
{
    private readonly List<Block> _chain = new();

    public IReadOnlyList<Block> Chain => _chain.AsReadOnly();

    /// <summary>
    /// Initializes the blockchain with the hardcoded genesis block.
    /// </summary>
    public Blockchain()
    {
        _chain.Add(Block.Genesis);
    }

    /// <summary>
    /// Adds a block to the chain. Rejects the block if previous hash linkage is invalid.
    /// Hash is always computed from block contents (never stored), so no separate hash validation needed.
    /// </summary>
    public bool AddBlock(Block block)
    {
        if (_chain.Count > 0)
        {
            var lastBlock = _chain[^1];
            if (block.PreviousHash != lastBlock.Hash)
            {
                return false; // Invalid previous hash
            }
        }

        // Validate coinbase reward against consensus rules.
        if (block.Index > 0 && !Consensus.ValidateCoinbase(block))
        {
            return false;
        }

        // Validate PoW difficulty (genesis exempt — it has a zero nonce).
        if (block.Index > 0 && !Consensus.ValidateDifficulty(block))
        {
            return false;
        }

        // Validate that all non-coinbase transactions have sufficient balance.
        if (block.Index > 0 && !Consensus.ValidateBalances(_chain, block))
        {
            return false;
        }

        _chain.Add(block);
        return true;
    }

    /// <summary>
    /// Replaces the entire chain with <paramref name="newChain"/>. No validation is performed —
    /// the caller must validate before calling this method.
    /// </summary>
    public void ReplaceChain(IEnumerable<Block> newChain)
    {
        _chain.Clear();
        _chain.AddRange(newChain);
    }

    /// <summary>
    /// Gets the latest block in the chain.
    /// </summary>
    public Block? GetLatestBlock() => _chain.Count > 0 ? _chain[^1] : null;

    /// <summary>
    /// Validates the entire blockchain integrity.
    /// </summary>
    public bool IsValid()
    {
        for (int i = 1; i < _chain.Count; i++)
        {
            var currentBlock = _chain[i];
            var previousBlock = _chain[i - 1];

            if (currentBlock.PreviousHash != previousBlock.Hash)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Validates an arbitrary list of blocks without modifying any chain instance.
    /// Useful for pre-validating a candidate chain before replacement.
    /// </summary>
    public static bool IsValidChain(IReadOnlyList<Block> chain)
    {
        for (int i = 1; i < chain.Count; i++)
        {
            if (chain[i].PreviousHash != chain[i - 1].Hash) return false;
            if (!Consensus.ValidateCoinbase(chain[i])) return false;
            if (!Consensus.ValidateDifficulty(chain[i])) return false;

            // Validate balances using all preceding blocks as context.
            var preceding = chain.Take(i).ToList().AsReadOnly();
            if (!Consensus.ValidateBalances(preceding, chain[i])) return false;
        }
        return true;
    }
}
