using TsvdChain.Core.Blockchain;
using TsvdChain.Core.Crypto;

namespace TsvdChain.Tests;

public class BlockchainTests
{
    [Fact]
    public void Block_Hash_Should_Be_Deterministic()
    {
        var block = Block.Create(1, "prevhash", new[] { Transaction.CreateSystemTransaction("alice", 10) }, 0);
        var hash1 = block.Hash;
        var hash2 = block.Hash;
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Blockchain_AddBlock_Should_Reject_Invalid_PreviousHash()
    {
        var blockchain = new Blockchain();
        var invalidBlock = Block.Create(1, "wronghash", new[] { Transaction.CreateSystemTransaction("alice", 10) });

        var added = blockchain.AddBlock(invalidBlock);

        Assert.False(added);
    }

    [Fact]
    public void Blockchain_IsValid_Should_Detect_Tampering()
    {
        var blockchain = new Blockchain();
        var genesis = blockchain.GetLatestBlock()!;
        var block1 = Block.Create(genesis.Index + 1, genesis.Hash, new[] { Transaction.CreateSystemTransaction("alice", 10) });
        var block2 = Block.Create(block1.Index + 1, block1.Hash, new[] { Transaction.CreateSystemTransaction("bob", 5) });

        Assert.True(blockchain.AddBlock(block1));
        Assert.True(blockchain.AddBlock(block2));
        Assert.True(blockchain.IsValid());

        var tamperedBlock1 = Block.Create(block1.Index, genesis.Hash,
            new[] { Transaction.CreateSystemTransaction("eve", 999) });

        var tamperedChain = blockchain.Chain.ToList();
        tamperedChain[1] = tamperedBlock1;

        Assert.False(Blockchain.IsValidChain(tamperedChain.AsReadOnly()));
    }

    [Fact]
    public void Transaction_CreateSigned_Should_Pass_Validation()
    {
        using var kp = KeyPair.Generate();
        var tx = Transaction.CreateSigned(kp, "recipient", 42);

        Assert.True(tx.ValidateSignature());
        Assert.Equal(kp.PublicKeyHex, tx.From);
    }

    [Fact]
    public void Transaction_Tampered_Should_Fail_Validation()
    {
        using var kp = KeyPair.Generate();
        var tx = Transaction.CreateSigned(kp, "recipient", 42);

        // Tamper with the amount
        var tampered = tx with { Amount = 999 };

        Assert.False(tampered.ValidateSignature());
    }

    [Fact]
    public void Transaction_Coinbase_Should_Pass_Validation()
    {
        var coinbase = Transaction.CreateSystemTransaction("miner-address", 50);

        Assert.True(coinbase.ValidateSignature());
        Assert.Equal("system", coinbase.From);
        Assert.Null(coinbase.Signature);
    }

    [Fact]
    public void Transaction_Without_Signature_Should_Fail_Validation()
    {
        using var kp = KeyPair.Generate();
        var tx = Transaction.CreateSigned(kp, "recipient", 10);

        // Strip the signature
        var unsigned = tx with { Signature = null };

        Assert.False(unsigned.ValidateSignature());
    }
}
