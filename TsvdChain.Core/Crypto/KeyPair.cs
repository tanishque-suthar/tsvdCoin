using System.Security.Cryptography;
using System.Text;

namespace TsvdChain.Core.Crypto;

/// <summary>
/// ECDSA P-256 key pair for signing and verifying transactions.
/// Wraps <see cref="ECDsa"/> with hex-based convenience methods.
/// </summary>
public sealed class KeyPair : IDisposable
{
    private readonly ECDsa _ecdsa;

    /// <summary>
    /// Hex-encoded SPKI public key — used as the node's address / "From" field.
    /// </summary>
    public string PublicKeyHex { get; }

    private KeyPair(ECDsa ecdsa)
    {
        _ecdsa = ecdsa;
        PublicKeyHex = Convert.ToHexString(ecdsa.ExportSubjectPublicKeyInfo());
    }

    /// <summary>
    /// Generate a new random ECDSA P-256 key pair.
    /// </summary>
    public static KeyPair Generate()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new KeyPair(ecdsa);
    }

    /// <summary>
    /// Restore a key pair from an exported private key (ECPrivateKey DER format).
    /// </summary>
    public static KeyPair FromPrivateKey(byte[] ecPrivateKey)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportECPrivateKey(ecPrivateKey, out _);
        return new KeyPair(ecdsa);
    }

    /// <summary>
    /// Export the private key in ECPrivateKey DER format (for encrypted storage).
    /// </summary>
    public byte[] ExportPrivateKey() => _ecdsa.ExportECPrivateKey();

    /// <summary>
    /// Sign arbitrary bytes, returning the raw signature.
    /// </summary>
    public byte[] Sign(byte[] data) => _ecdsa.SignData(data, HashAlgorithmName.SHA256);

    /// <summary>
    /// Sign a UTF-8 string, returning the hex-encoded signature.
    /// </summary>
    public string SignHex(string content)
    {
        var data = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(Sign(data));
    }

    /// <summary>
    /// Verify a signature against a hex-encoded public key (SPKI format).
    /// </summary>
    public static bool Verify(string publicKeyHex, string content, string signatureHex)
    {
        var data = Encoding.UTF8.GetBytes(content);
        var signature = Convert.FromHexString(signatureHex);
        var publicKeyBytes = Convert.FromHexString(publicKeyHex);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);
        return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
    }

    public void Dispose() => _ecdsa.Dispose();
}
