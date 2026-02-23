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
    /// Adds a block to the chain. The block is validated before adding.
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
            return false; // Invalid block hash
        }

        _chain.Add(block);
        return true;
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
}
