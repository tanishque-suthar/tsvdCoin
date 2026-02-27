using TsvdChain.Core.Hashing;

namespace TsvdChain.Core.Blockchain;

/// <summary>
/// Simple transaction model.  Id is SHA256 of the transaction content (excluding signature).
/// Signature verification is a placeholder — will implement ECDSA in a later phase.
/// </summary>
public sealed record Transaction
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required long Amount { get; init; }
    public required long Timestamp { get; init; }
    public string? Signature { get; init; }
    public required string Id { get; init; }

    public static Transaction Create(string from, string to, long amount, string? signature = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var content = $"{from}{to}{amount}{timestamp}";
        var id = Sha256Hasher.ComputeHashString(content);
        return new Transaction
        {
            From = from,
            To = to,
            Amount = amount,
            Timestamp = timestamp,
            Signature = signature,
            Id = id
        };
    }

    /// <summary>
    /// Placeholder for signature validation; will be expanded to use ECDSA in Phase 8.
    /// </summary>
    public bool ValidateSignature() => true;
}