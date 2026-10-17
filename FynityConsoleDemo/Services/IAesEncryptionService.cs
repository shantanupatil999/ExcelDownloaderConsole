namespace FynityConsoleDemo.Services;

/// <summary>
/// Abstraction over the encrypt/decrypt scheme used by the Fynity APIs.
/// If it turns out Fynity uses something other than AES-CBC (different mode,
/// key derivation, or encoding), only AesEncryptionService needs to change —
/// everything else in the project talks to this interface.
/// </summary>
public interface IAesEncryptionService
{
    /// <summary>Encrypts plain text (e.g. a JSON payload) and returns Base64 ciphertext.</summary>
    string Encrypt(string plainText);

    /// <summary>Decrypts Base64 ciphertext back to plain text.</summary>
    string Decrypt(string cipherTextBase64);

    /// <summary>Decrypts Base64 ciphertext back to raw bytes (use when the plaintext is binary, e.g. an xlsx file).</summary>
    byte[] DecryptToBytes(string cipherTextBase64);
}
