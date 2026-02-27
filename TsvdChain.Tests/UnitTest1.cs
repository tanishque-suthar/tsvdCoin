using TsvdChain.Core.Blockchain;
using TsvdChain.Core.Hashing;

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
        var next = Block.Create(genesis.Index + 1, genesis.Hash, new[] { Transaction.Create("alice", "bob", 10) });

        Assert.True(blockchain.AddBlock(next));
        Assert.True(blockchain.IsValid());

        // Tamper with block by changing hash field via new instance.
        var tampered = next with { Hash = Sha256Hasher.ComputeHashString("different") };

        var chainList = blockchain.Chain.ToList();
        chainList[1] = tampered;

        // Rebuild blockchain from tampered list to test validation.
        var rebuilt = new Blockchain();
        foreach (var block in chainList)
        {
            rebuilt.AddBlock(block);
        }

        Assert.False(rebuilt.IsValid());
    }
}
