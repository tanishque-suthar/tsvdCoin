using TsvdChain.Core.Hashing;

namespace TsvdChain.Core.Blockchain;

/// <summary>
/// Immutable block in the blockchain using C# 14 record with init accessors.
/// </summary>
public sealed record class Block
{
    public required int Index { get; init; }
    public required long Timestamp { get; init; }
    public required string PreviousHash { get; init; }
    public required string Data { get; init; }
    public required int Nonce { get; init; }
    public required string Hash { get; init; }

    /// <summary>
    /// Creates a new block and computes its hash.
    /// </summary>
    public static Block Create(int index, string previousHash, string data, int nonce = 0)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var content = $"{index}{timestamp}{previousHash}{data}{nonce}";
        var hash = Sha256Hasher.ComputeHashString(content);
        
        return new Block
        {
            Index = index,
            Timestamp = timestamp,
            PreviousHash = previousHash,
            Data = data,
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
        return Create(0, genesisPreviousHash, data, 0);
    }

    /// <summary>
    /// Validates the block's hash.
    /// </summary>
    public bool ValidateHash()
    {
        var content = $"{Index}{Timestamp}{PreviousHash}{Data}{Nonce}";
        var computedHash = Sha256Hasher.ComputeHashString(content);
        return Hash == computedHash;
    }

    /// <summary>
    /// Creates a new block with updated nonce (for mining).
    /// </summary>
    public Block WithNonce(int newNonce)
    {
        var content = $"{Index}{Timestamp}{PreviousHash}{Data}{newNonce}";
        var hash = Sha256Hasher.ComputeHashString(content);
        
        return this with
        {
            Nonce = newNonce,
            Hash = hash
        };
    }
}
