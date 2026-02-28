namespace TsvdChain.Core.Blockchain;

/// <summary>
/// Represents a blockchain - a growing list of immutable blocks.
/// </summary>
public sealed class Blockchain
{
    private readonly List<Block> _chain = new();

    public IReadOnlyList<Block> Chain => _chain.AsReadOnly();

    /// <summary>
    /// Creates a new blockchain with the genesis block.
    /// </summary>
    public static Blockchain CreateWithGenesis(string genesisData = "Genesis Block - tsvdChain")
    {
        var blockchain = new Blockchain();
        var genesisBlock = Block.CreateGenesis(genesisData);
        blockchain.AddBlock(genesisBlock);
        return blockchain;
    }

    /// <summary>
    /// Adds a block to the chain. Rejects the block if previous hash or self-hash is invalid.
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

        if (!block.ValidateHash())
        {
            return false; // Tampered or corrupt block
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

            if (!currentBlock.ValidateHash())
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
            if (!chain[i].ValidateHash()) return false;
            if (chain[i].PreviousHash != chain[i - 1].Hash) return false;
        }
        return true;
    }
}
