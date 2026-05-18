using System.Security.Cryptography;
using System.Text.Json;
using Clipton.Core;

namespace Clipton.WinUI;

public sealed class EncryptedHistoryStore
{
    private readonly string _path;

    public EncryptedHistoryStore(string path)
    {
        _path = path;
    }

    public IReadOnlyList<ClipboardSnapshot> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            var encrypted = File.ReadAllBytes(_path);
            var json = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var dto = JsonSerializer.Deserialize<ClipboardSnapshotDto[]>(json) ?? [];
            return dto.Select(item => item.ToSnapshot()).ToArray();
        }
        catch (CryptographicException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<ClipboardSnapshot> snapshots)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var dto = snapshots.Select(ClipboardSnapshotDto.FromSnapshot).ToArray();
        var json = JsonSerializer.SerializeToUtf8Bytes(dto, new JsonSerializerOptions { WriteIndented = true });
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, encrypted);
    }

    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private sealed class ClipboardSnapshotDto
    {
        public string Id { get; set; } = string.Empty;
        public DateTimeOffset CapturedAt { get; set; }
        public ClipboardFormatKind[] Formats { get; set; } = [];
        public string? Text { get; set; }
        public string? Rtf { get; set; }
        public string? Html { get; set; }
        public byte[]? ImagePng { get; set; }
        public string[] FilePaths { get; set; } = [];

        public static ClipboardSnapshotDto FromSnapshot(ClipboardSnapshot snapshot)
        {
            return new ClipboardSnapshotDto
            {
                Id = snapshot.Id,
                CapturedAt = snapshot.CapturedAt,
                Formats = snapshot.Formats.ToArray(),
                Text = snapshot.Text,
                Rtf = snapshot.Rtf,
                Html = snapshot.Html,
                ImagePng = snapshot.ImagePng,
                FilePaths = snapshot.FilePaths.ToArray()
            };
        }

        public ClipboardSnapshot ToSnapshot()
        {
            return new ClipboardSnapshot(Id, CapturedAt, Formats, Text, Rtf, Html, ImagePng, FilePaths);
        }
    }
}
