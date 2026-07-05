using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clipton.WinUI;

internal static class EncryptedExportFile
{
    public const string Extension = ".clipton";
    public const int MinPassphraseLength = 8;
    private const int Version = 1;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Iterations = 210_000;
    private const string Kdf = "PBKDF2-SHA256";
    private const string Cipher = "AES-256-GCM";
    internal const long MaxImportFileBytes = 128L * 1024 * 1024;

    public static bool HasEncryptedExtension(string path)
    {
        return string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidPassphrase(string? passphrase)
    {
        return passphrase is { Length: >= MinPassphraseLength };
    }

    public static void Write<T>(string path, string kind, T value, string passphrase, JsonSerializerOptions options)
    {
        if (!IsValidPassphrase(passphrase))
        {
            throw new ArgumentException("Export password must be at least 8 characters.", nameof(passphrase));
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, options);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        var key = DeriveKey(passphrase, salt);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(kind));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var envelope = new Envelope(
            Version,
            kind,
            Kdf,
            Iterations,
            Cipher,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(ciphertext));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(envelope, options));
    }

    public static T Read<T>(string path, string expectedKind, string passphrase, JsonSerializerOptions options)
    {
        if (!IsValidPassphrase(passphrase))
        {
            throw new ArgumentException("Export password must be at least 8 characters.", nameof(passphrase));
        }

        EnsureFileWithinImportLimit(path);
        var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path), options)
            ?? throw new InvalidOperationException("The selected file is not a supported encrypted Clipton export.");
        if (envelope.Version != Version
            || !string.Equals(envelope.Kind, expectedKind, StringComparison.Ordinal)
            || !string.Equals(envelope.Kdf, Kdf, StringComparison.Ordinal)
            || envelope.Iterations != Iterations
            || !string.Equals(envelope.Cipher, Cipher, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The selected file is not a supported encrypted Clipton export.");
        }

        var salt = Convert.FromBase64String(envelope.Salt);
        var nonce = Convert.FromBase64String(envelope.Nonce);
        var tag = Convert.FromBase64String(envelope.Tag);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        if (salt.Length != SaltSize || nonce.Length != NonceSize || tag.Length != TagSize)
        {
            throw new InvalidOperationException("The selected file is not a supported encrypted Clipton export.");
        }

        var plaintext = new byte[ciphertext.Length];
        var key = DeriveKey(passphrase, salt);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(expectedKind));
            return JsonSerializer.Deserialize<T>(plaintext, options)
                ?? throw new InvalidOperationException("The selected file does not contain supported Clipton data.");
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("The export password is incorrect or the file was modified.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal static void EnsureFileWithinImportLimit(string path)
    {
        var length = new FileInfo(path).Length;
        if (length > MaxImportFileBytes)
        {
            throw new InvalidOperationException($"The selected file is too large to import. The maximum supported import size is {MaxImportFileBytes / 1024 / 1024} MB.");
        }
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);
    }

    private sealed record Envelope(
        int Version,
        string Kind,
        string Kdf,
        int Iterations,
        string Cipher,
        string Salt,
        string Nonce,
        string Tag,
        string Ciphertext);
}
