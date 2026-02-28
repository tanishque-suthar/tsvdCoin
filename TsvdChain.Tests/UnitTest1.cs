using TsvdChain.Core.Blockchain;

namespace TsvdChain.Tests;

public class BlockchainTests
{
    [Fact]
    public void Block_Create_Should_Produce_Valid_Hash()
    {
        var block = Block.Create(1, "prevhash", new[] { Transaction.Create("alice", "bob", 10) }, 0);
        Assert.True(block.ValidateHash());
    }

    [Fact]
    public void Blockchain_AddBlock_Should_Reject_Invalid_PreviousHash()
    {
        var blockchain = Blockchain.CreateWithGenesis();
        var invalidBlock = Block.Create(1, "wronghash", new[] { Transaction.Create("alice", "bob", 10) });

        var added = blockchain.AddBlock(invalidBlock);

        Assert.False(added);
    }

    [Fact]
    public void Blockchain_IsValid_Should_Detect_Tampering()
    {
        var blockchain = Blockchain.CreateWithGenesis();
        var genesis = blockchain.GetLatestBlock()!;
        var block1 = Block.Create(genesis.Index + 1, genesis.Hash, new[] { Transaction.Create("alice", "bob", 10) });
        var block2 = Block.Create(block1.Index + 1, block1.Hash, new[] { Transaction.Create("bob", "carol", 5) });

        Assert.True(blockchain.AddBlock(block1));
        Assert.True(blockchain.AddBlock(block2));
        Assert.True(blockchain.IsValid());

        // Tamper: replace block1 with a different block (Block.Create gives it a valid hash,
        // but its hash differs from the original block1.Hash, breaking block2's PreviousHash link).
        var tamperedBlock1 = Block.Create(block1.Index, genesis.Hash,
            new[] { Transaction.Create("alice", "eve", 999) });

        var tamperedChain = blockchain.Chain.ToList();
        tamperedChain[1] = tamperedBlock1; // tamperedBlock1.Hash != block1.Hash → block2 link broken

        Assert.False(Blockchain.IsValidChain(tamperedChain.AsReadOnly()));
    }
}
