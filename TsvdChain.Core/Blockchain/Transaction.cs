using TsvdChain.Core.Crypto;
using TsvdChain.Core.Hashing;

namespace TsvdChain.Core.Blockchain;

/// <summary>
/// Immutable transaction model. Id is SHA256 of the unsigned content.
/// Signature is ECDSA P-256 over the same content, verified against the From public key.
/// </summary>
public sealed record Transaction
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required long Amount { get; init; }
    public required long Timestamp { get; init; }
    public string? Signature { get; init; }
    public required string Id { get; init; }

    /// <summary>
    /// The content string that is hashed to produce the Id and signed to produce the Signature.
    /// </summary>
    private string UnsignedContent => $"{From}{To}{Amount}{Timestamp}";

    /// <summary>
    /// Create a signed transaction using the sender's key pair.
    /// </summary>
    public static Transaction CreateSigned(KeyPair keyPair, string to, long amount)
    {
        var from = keyPair.PublicKeyHex;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var content = $"{from}{to}{amount}{timestamp}";
        var signature = keyPair.SignHex(content);
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
    /// Create an unsigned system/coinbase transaction (e.g. mining reward).
    /// </summary>
    public static Transaction CreateSystemTransaction(string toAddress, long reward)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var content = $"system{toAddress}{reward}{timestamp}";
        var id = Sha256Hasher.ComputeHashString(content);

        return new Transaction
        {
            From = "system",
            To = toAddress,
            Amount = reward,
            Timestamp = timestamp,
            Signature = null,
            Id = id
        };
    }

    /// <summary>
    /// Validate the ECDSA signature against the From public key.
    /// System/coinbase transactions are always valid.
    /// </summary>
    public bool ValidateSignature()
    {
        if (From == "system") return true;
        if (string.IsNullOrEmpty(Signature)) return false;

        try
        {
            return KeyPair.Verify(From, UnsignedContent, Signature);
        }
        catch (Exception)
        {
            return false; // Malformed key or signature
        }
    }
}