using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class EncryptedExportFileTests
{
    [Fact]
    public void EnsureFileWithinImportLimit_RejectsOversizedFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-export-tests", Guid.NewGuid().ToString("N"), "large.clipton");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        {
            stream.SetLength(EncryptedExportFile.MaxImportFileBytes + 1);
        }

        Assert.Throws<InvalidOperationException>(() => EncryptedExportFile.EnsureFileWithinImportLimit(path));
    }

    [Fact]
    public void EnsureFileWithinImportLimit_AllowsFilesAtLimit()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-export-tests", Guid.NewGuid().ToString("N"), "limit.clipton");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        {
            stream.SetLength(EncryptedExportFile.MaxImportFileBytes);
        }

        EncryptedExportFile.EnsureFileWithinImportLimit(path);
    }

    [Fact]
    public void Read_UsesIterationCountStoredInEnvelope()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-export-tests", Guid.NewGuid().ToString("N"), "custom-iterations.clipton");
        var options = new JsonSerializerOptions();
        const string kind = "test";
        const string passphrase = "correct horse";
        WriteEncryptedEnvelope(path, kind, new[] { "alpha" }, passphrase, options, iterations: 25_001);

        var loaded = EncryptedExportFile.Read<string[]>(path, kind, passphrase, options);

        Assert.Equal(["alpha"], loaded);
    }

    [Theory]
    [InlineData(EncryptedExportFile.MinSupportedIterations - 1)]
    [InlineData(EncryptedExportFile.MaxSupportedIterations + 1)]
    public void Read_RejectsIterationCountsOutsideSupportedRangeBeforeKeyDerivation(int iterations)
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-export-tests", Guid.NewGuid().ToString("N"), "invalid-iterations.clipton");
        var options = new JsonSerializerOptions();
        const string kind = "test";
        const string passphrase = "correct horse";
        WriteUnencryptedEnvelope(path, kind, options, iterations);

        Assert.Throws<InvalidOperationException>(() =>
            EncryptedExportFile.Read<string[]>(path, kind, passphrase, options));
    }

    private static void WriteEncryptedEnvelope<T>(
        string path,
        string kind,
        T value,
        string passphrase,
        JsonSerializerOptions options,
        int iterations)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, options);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(kind));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var envelope = new
        {
            Version = 1,
            Kind = kind,
            Kdf = "PBKDF2-SHA256",
            Iterations = iterations,
            Cipher = "AES-256-GCM",
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext)
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(envelope, options));
    }

    private static void WriteUnencryptedEnvelope(
        string path,
        string kind,
        JsonSerializerOptions options,
        int iterations)
    {
        var envelope = new
        {
            Version = 1,
            Kind = kind,
            Kdf = "PBKDF2-SHA256",
            Iterations = iterations,
            Cipher = "AES-256-GCM",
            Salt = Convert.ToBase64String(new byte[16]),
            Nonce = Convert.ToBase64String(new byte[12]),
            Tag = Convert.ToBase64String(new byte[16]),
            Ciphertext = string.Empty
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(envelope, options));
    }
}
