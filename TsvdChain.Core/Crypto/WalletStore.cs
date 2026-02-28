using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TsvdChain.Core.Crypto;

/// <summary>
/// Handles encrypted storage of a wallet's private key using AES-256-GCM
/// with a password-derived key (PBKDF2 + SHA-256, 100 000 iterations).
/// </summary>
public sealed class WalletStore
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;   // AES-GCM standard
    private const int TagSize = 16;     // 128-bit authentication tag
    private const int KeySize = 32;     // AES-256
    private const int Iterations = 100_000;

    private readonly string _walletPath;

    public WalletStore(string walletDirectory)
    {
        Directory.CreateDirectory(walletDirectory);
        _walletPath = Path.Combine(walletDirectory, "wallet.json");
    }

    /// <summary>
    /// Whether a wallet file already exists on disk.
    /// </summary>
    public bool WalletExists() => File.Exists(_walletPath);

    /// <summary>
    /// Generate a new key pair, encrypt the private key with <paramref name="password"/>,
    /// and persist to disk. Returns the unlocked <see cref="KeyPair"/>.
    /// </summary>
    public KeyPair CreateWallet(string password)
    {
        var keyPair = KeyPair.Generate();
        var privateKeyBytes = keyPair.ExportPrivateKey();

        try
        {
            var (ciphertext, salt, nonce, tag) = Encrypt(privateKeyBytes, password);

            var walletFile = new WalletFile
            {
                PublicKey = keyPair.PublicKeyHex,
                Salt = Convert.ToBase64String(salt),
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(tag)
            };

            var json = JsonSerializer.Serialize(walletFile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_walletPath, json);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyBytes);
        }

        return keyPair;
    }

    /// <summary>
    /// Decrypt and return the key pair stored on disk using the given <paramref name="password"/>.
    /// Throws <see cref="CryptographicException"/> on wrong password or corrupt file.
    /// </summary>
    public KeyPair UnlockWallet(string password)
    {
        var json = File.ReadAllText(_walletPath);
        var walletFile = JsonSerializer.Deserialize<WalletFile>(json)
            ?? throw new InvalidOperationException("Invalid wallet file.");

        var salt = Convert.FromBase64String(walletFile.Salt);
        var nonce = Convert.FromBase64String(walletFile.Nonce);
        var ciphertext = Convert.FromBase64String(walletFile.Ciphertext);
        var tag = Convert.FromBase64String(walletFile.Tag);

        var privateKeyBytes = Decrypt(ciphertext, password, salt, nonce, tag);

        try
        {
            var keyPair = KeyPair.FromPrivateKey(privateKeyBytes);

            if (keyPair.PublicKeyHex != walletFile.PublicKey)
            {
                keyPair.Dispose();
                throw new CryptographicException("Public key mismatch — wallet file may be corrupt.");
            }

            return keyPair;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyBytes);
        }
    }

    private static (byte[] Ciphertext, byte[] Salt, byte[] Nonce, byte[] Tag) Encrypt(byte[] plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(password, salt);

        try
        {
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];

            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            return (ciphertext, salt, nonce, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] Decrypt(byte[] ciphertext, string password, byte[] salt, byte[] nonce, byte[] tag)
    {
        var key = DeriveKey(password, salt);

        try
        {
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeySize);
    }

    private sealed record WalletFile
    {
        public required string PublicKey { get; init; }
        public required string Salt { get; init; }
        public required string Nonce { get; init; }
        public required string Ciphertext { get; init; }
        public required string Tag { get; init; }
    }
}
