using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace FynityConsoleDemo.Services;

public class CryptoOptions
{
    public string Algorithm { get; set; } = "AES-256-GCM";

    /// <summary>
    /// Used as a raw UTF-8 byte string, matching the PHP partner's
    /// mb_convert_encoding($key, 'UTF-8') — this is NOT Base64-decoded.
    /// Must be exactly 32 bytes once UTF-8 encoded (AES-256).
    /// </summary>
    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// AES-256-GCM, matching the partner's PHP implementation exactly:
///
///   encrypt: nonce = random_bytes(12)
///            [ciphertext, tag] = openssl_encrypt(plainText, 'aes-256-gcm', key, OPENSSL_RAW_DATA, nonce)
///            wire = base64(nonce . tag . ciphertext)
///
///   decrypt: data = base64_decode(wire)
///            nonce = data[0:12], tag = data[12:28], ciphertext = data[28:]
///            plainText = openssl_decrypt(ciphertext, 'aes-256-gcm', key, OPENSSL_RAW_DATA, nonce, tag)
///
/// .NET's AesGcm class maps onto this directly: NonceByteSizes = 12,
/// TagByteSizes = 16 (both fixed sizes PHP/OpenSSL use by default for GCM).
/// </summary>
public class AesEncryptionService : IAesEncryptionService
{
    private const int NonceSize = 12; // bytes
    private const int TagSize = 16;   // bytes

    private readonly byte[] _key;

    public AesEncryptionService(IOptions<CryptoOptions> options)
    {
        var opts = options.Value;

        if (string.IsNullOrEmpty(opts.Key))
            throw new InvalidOperationException(
                "Crypto:Key is not configured. Set it in appsettings.json or user-secrets before calling encrypt/decrypt.");

        _key = Encoding.UTF8.GetBytes(opts.Key);

        if (_key.Length != 32)
            throw new InvalidOperationException(
                $"Crypto:Key must be exactly 32 bytes once UTF-8 encoded for AES-256-GCM (got {_key.Length}). " +
                "This mirrors the PHP code's own check.");
    }

    public string Encrypt(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var gcm = new AesGcm(_key, TagSize))
        {
            gcm.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        // Same layout as the PHP side: nonce . tag . ciphertext, then Base64.
        var wire = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, wire, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, wire, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, wire, NonceSize + TagSize, cipherBytes.Length);

        return Convert.ToBase64String(wire);
    }

    public string Decrypt(string cipherTextBase64)
    {
        var bytes = DecryptToBytes(cipherTextBase64);
        return Encoding.UTF8.GetString(bytes);
    }

    public byte[] DecryptToBytes(string cipherTextBase64)
    {
        var wire = Convert.FromBase64String(cipherTextBase64);

        if (wire.Length < NonceSize + TagSize)
            throw new CryptographicException(
                $"Payload too short to contain a {NonceSize}-byte nonce + {TagSize}-byte tag ({wire.Length} bytes total).");

        var nonce = wire.AsSpan(0, NonceSize);
        var tag = wire.AsSpan(NonceSize, TagSize);
        var cipherBytes = wire.AsSpan(NonceSize + TagSize);

        var plainBytes = new byte[cipherBytes.Length];

        using (var gcm = new AesGcm(_key, TagSize))
        {
            gcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }

        return plainBytes;
    }
}
