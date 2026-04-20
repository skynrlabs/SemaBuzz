using System.Net;
using System.Net.Sockets;

namespace SemaBuzz.Protocol;

/// <summary>
/// Encrypts and decrypts SemaBuzzPacket payloads using AES-256-GCM.
/// The shield is always on — no plaintext crosses the wire.
/// </summary>
public sealed class SemaBuzzShield : IDisposable
{
    // AES-256: 32-byte key, 12-byte nonce (GCM standard)
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;
    private bool _disposed;

    public SemaBuzzShield(byte[] key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"SemaBuzz Shield requires a {KeySize}-byte key.", nameof(key));
        _key = (byte[])key.Clone();
    }

    /// <summary>
    /// Derive a key from a shared passphrase using PBKDF2-SHA256 (100 000 iterations).
    /// A fixed protocol salt is used because both peers must derive the same key
    /// independently. This makes dictionary/brute-force attacks ~100 000× harder
    /// than raw SHA-256 and prevents precomputed rainbow tables for other salts.
    /// </summary>
    public static SemaBuzzShield FromPassphrase(string passphrase)
    {
        var rawKey = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            password:        System.Text.Encoding.UTF8.GetBytes(passphrase),
            salt:            System.Text.Encoding.UTF8.GetBytes("SemaBuzz-wire-v1"),
            iterations:      100_000,
            hashAlgorithm:   System.Security.Cryptography.HashAlgorithmName.SHA256,
            outputLength:    KeySize);
        return new SemaBuzzShield(rawKey);
    }

    /// <summary>
    /// Derive an AES-256 key from an ECDH shared secret using HKDF-SHA256.
    /// The raw shared secret bytes are zeroed after extraction.
    /// </summary>
    public static SemaBuzzShield FromEcdhSecret(byte[] rawSharedSecret)
    {
        var key = System.Security.Cryptography.HKDF.DeriveKey(
            hashAlgorithmName: System.Security.Cryptography.HashAlgorithmName.SHA256,
            ikm:               rawSharedSecret,
            outputLength:      KeySize,
            salt:              "SemaBuzz-ecdh-v2"u8.ToArray(),
            info:              Array.Empty<byte>());
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(rawSharedSecret);
        return new SemaBuzzShield(key);
    }

    /// <summary>
    /// Encrypt raw packet bytes. Output: [12-byte nonce][16-byte tag][ciphertext]
    /// </summary>
    public byte[] Encrypt(byte[] plaintext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var nonce = new byte[NonceSize];
        System.Security.Cryptography.RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new System.Security.Cryptography.AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var output = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, output, NonceSize + TagSize, ciphertext.Length);
        return output;
    }

    /// <summary>
    /// Decrypt encrypted bytes. Input must be [nonce][tag][ciphertext].
    /// Returns null if authentication fails.
    /// </summary>
    public byte[]? Decrypt(byte[] encrypted)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (encrypted.Length < NonceSize + TagSize) return null;

        var nonce = encrypted[..NonceSize];
        var tag = encrypted[NonceSize..(NonceSize + TagSize)];
        var ciphertext = encrypted[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new System.Security.Cryptography.AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (System.Security.Cryptography.AuthenticationTagMismatchException)
        {
            return null; // Tampered or wrong key — drop it.
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(_key);
            _disposed = true;
        }
    }
}
