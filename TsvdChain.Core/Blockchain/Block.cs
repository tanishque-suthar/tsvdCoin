using System.Collections.Generic;
using System.Linq;
using TsvdChain.Core.Hashing;

namespace TsvdChain.Core.Blockchain;

/// <summary>
/// Immutable block in the blockchain using C# 14 record with init accessors.
/// Now includes Transactions and MerkleRoot.
/// </summary>
public sealed record class Block
{
    public required int Index { get; init; }
    public required long Timestamp { get; init; }
    public required string PreviousHash { get; init; }
    public required IReadOnlyList<Transaction> Transactions { get; init; }
    public required string MerkleRoot { get; init; }
    public required int Nonce { get; init; }
    public required string Hash { get; init; }

    /// <summary>
    /// Creates a new block and computes its MerkleRoot and hash.
    /// </summary>
    public static Block Create(int index, string previousHash, string data, int nonce = 0)
    {
        // Backwards-compatible overload: wrap string data into a single-system transaction.
        var tx = Transaction.Create("system", data, 0);
        return Create(index, previousHash, new[] { tx }, nonce);
    }

    /// <summary>
    /// Creates a new block and computes its MerkleRoot and hash.
    /// </summary>
    public static Block Create(int index, string previousHash, IEnumerable<Transaction>? transactions, int nonce = 0)
    {
        var txList = (transactions ?? Enumerable.Empty<Transaction>()).ToList().AsReadOnly();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var merkleRoot = MerkleTree.ComputeMerkleRoot(txList.Select(t => t.Id));
        var content = $"{index}{timestamp}{previousHash}{merkleRoot}{nonce}";
        var hash = Sha256Hasher.ComputeHashString(content);

        return new Block
        {
            Index = index,
            Timestamp = timestamp,
            PreviousHash = previousHash,
            Transactions = txList,
            MerkleRoot = merkleRoot,
            Nonce = nonce,
            Hash = hash
        };
    }

    /// <summary>
    /// Creates the genesis block (block index 0).
    /// </summary>
    public static Block CreateGenesis(string data = "Genesis Block - tsvdChain")
    {
        const string genesisPreviousHash = "0000000000000000000000000000000000000000000000000000000000000000";
        // Represent genesis as a single transaction carrying the genesis data in To field.
        var genesisTx = Transaction.Create("system", "genesis", 0, signature: null) with { };
        return Create(0, genesisPreviousHash, new[] { genesisTx }, 0);
    }

    /// <summary>
    /// Validates the block's hash.
    /// </summary>
    public bool ValidateHash()
    {
        var content = $"{Index}{Timestamp}{PreviousHash}{MerkleRoot}{Nonce}";
        var computedHash = Sha256Hasher.ComputeHashString(content);
        return Hash == computedHash;
    }

    /// <summary>
    /// Creates a new block with updated nonce (for mining).
    /// </summary>
    public Block WithNonce(int newNonce)
    {
        var content = $"{Index}{Timestamp}{PreviousHash}{MerkleRoot}{newNonce}";
        var hash = Sha256Hasher.ComputeHashString(content);

        return this with
        {
            Nonce = newNonce,
            Hash = hash
        };
    }
}