using TsvdChain.Core.Blockchain;
using TsvdChain.Core.Crypto;
using TsvdChain.Core.Hashing;

namespace TsvdChain.Tests;

public class BlockchainTests
{
    /// <summary>
    /// Mines a block (brute-force nonce) that satisfies consensus difficulty.
    /// </summary>
    private static Block MineTestBlock(int index, string previousHash, IEnumerable<Transaction> transactions)
    {
        var prefix = new string('0', Consensus.Difficulty);
        var txList = transactions.ToList().AsReadOnly();
        var merkleRoot = MerkleTree.ComputeMerkleRoot(txList.Select(t => t.Id));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        for (int nonce = 0; ; nonce++)
        {
            var hash = Sha256Hasher.ComputeHashString($"{index}{timestamp}{previousHash}{merkleRoot}{nonce}");
            if (hash.StartsWith(prefix, StringComparison.Ordinal))
                return new Block
                {
                    Index = index,
                    Timestamp = timestamp,
                    PreviousHash = previousHash,
                    Transactions = txList,
                    MerkleRoot = merkleRoot,
                    Nonce = nonce
                };
        }
    }
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
        var block1 = MineTestBlock(genesis.Index + 1, genesis.Hash, new[] { Transaction.CreateSystemTransaction("alice", 10) });
        var block2 = MineTestBlock(block1.Index + 1, block1.Hash, new[] { Transaction.CreateSystemTransaction("bob", 5) });

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

    [Fact]
    public void Blockchain_AddBlock_Should_Reject_Excessive_Reward()
    {
        var blockchain = new Blockchain();
        var genesis = blockchain.GetLatestBlock()!;

        // Block with coinbase reward exceeding consensus (50 is valid, 100 is not)
        var block = Block.Create(1, genesis.Hash, new[] { Transaction.CreateSystemTransaction("miner", 100) });

        Assert.False(blockchain.AddBlock(block));
    }

    [Fact]
    public void Blockchain_AddBlock_Should_Accept_Valid_Reward()
    {
        var blockchain = new Blockchain();
        var genesis = blockchain.GetLatestBlock()!;

        var block = MineTestBlock(1, genesis.Hash, new[] { Transaction.CreateSystemTransaction("miner", 50) });

        Assert.True(blockchain.AddBlock(block));
    }

    [Fact]
    public void Consensus_GetBlockReward_Should_Halve()
    {
        Assert.Equal(50, Consensus.GetBlockReward(0));
        Assert.Equal(50, Consensus.GetBlockReward(209_999));
        Assert.Equal(25, Consensus.GetBlockReward(210_000));
        Assert.Equal(12, Consensus.GetBlockReward(420_000));
        Assert.Equal(0, Consensus.GetBlockReward(210_000 * 64));
    }

    [Fact]
    public void Blockchain_AddBlock_Should_Reject_Insufficient_Difficulty()
    {
        var blockchain = new Blockchain();
        var genesis = blockchain.GetLatestBlock()!;

        // Block with valid coinbase but no PoW (nonce 0, unlikely to satisfy difficulty)
        var block = Block.Create(1, genesis.Hash, new[] { Transaction.CreateSystemTransaction("miner", 50) }, 0);

        // Almost certainly won't start with "000"
        if (!block.Hash.StartsWith(new string('0', Consensus.Difficulty), StringComparison.Ordinal))
        {
            Assert.False(blockchain.AddBlock(block));
        }
    }

    [Fact]
    public void Consensus_ValidateDifficulty_Should_Check_Leading_Zeros()
    {
        var blockchain = new Blockchain();
        var genesis = blockchain.GetLatestBlock()!;

        var mined = MineTestBlock(1, genesis.Hash, new[] { Transaction.CreateSystemTransaction("miner", 50) });
        Assert.True(Consensus.ValidateDifficulty(mined));
    }
}
