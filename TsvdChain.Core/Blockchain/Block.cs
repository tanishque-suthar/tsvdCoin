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

    /// <summary>
    /// Block hash, derived from header fields. Never stored — always computed, just like Bitcoin.
    /// </summary>
    public string Hash => Sha256Hasher.ComputeHashString($"{Index}{Timestamp}{PreviousHash}{MerkleRoot}{Nonce}");

    /// <summary>
    /// Creates a new block and computes its MerkleRoot.
    /// </summary>
    public static Block Create(int index, string previousHash, IEnumerable<Transaction>? transactions, int nonce = 0)
    {
        var txList = (transactions ?? Enumerable.Empty<Transaction>()).ToList().AsReadOnly();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var merkleRoot = MerkleTree.ComputeMerkleRoot(txList.Select(t => t.Id));

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

    /// <summary>
    /// The one and only genesis block. Deterministic and identical across all nodes.
    /// </summary>
    public static readonly Block Genesis = CreateGenesisInternal();

    private static Block CreateGenesisInternal()
    {
        const string previousHash = "0000000000000000000000000000000000000000000000000000000000000000";
        const long timestamp = 0L;
        const int nonce = 0;

        var genesisTx = new Transaction
        {
            From = "system",
            To = "genesis",
            Amount = 0,
            Timestamp = 0,
            Signature = null,
            Id = Sha256Hasher.ComputeHashString("systemgenesis00")
        };

        var txList = new List<Transaction> { genesisTx }.AsReadOnly();
        var merkleRoot = MerkleTree.ComputeMerkleRoot(txList.Select(t => t.Id));

        return new Block
        {
            Index = 0,
            Timestamp = timestamp,
            PreviousHash = previousHash,
            Transactions = txList,
            MerkleRoot = merkleRoot,
            Nonce = nonce
        };
    }

}