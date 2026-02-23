using System.Security.Cryptography;

namespace TsvdChain.Core.Hashing;

/// <summary>
/// Provides zero-allocation SHA256 hashing using Span<byte>.
/// </summary>
public static class Sha256Hasher
{
    /// <summary>
    /// Computes SHA256 hash of the given data using Span<byte> for zero-allocation.
    /// </summary>
    public static void ComputeHash(ReadOnlySpan<byte> data, Span<byte> output)
    {
        using var sha256 = SHA256.Create();
        sha256.TryComputeHash(data, output, out _);
    }

    /// <summary>
    /// Computes SHA256 hash and returns as a hex string.
    /// </summary>
    public static string ComputeHashString(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[32];
        ComputeHash(data, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Computes SHA256 hash of a string (UTF8 encoded).
    /// </summary>
    public static string ComputeHashString(string data)
    {
        return ComputeHashString(System.Text.Encoding.UTF8.GetBytes(data));
    }
}
