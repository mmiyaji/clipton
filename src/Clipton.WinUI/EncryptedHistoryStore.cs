using System.Security.Cryptography;
using System.Text.Json;
using Clipton.Core;

namespace Clipton.WinUI;

public sealed class EncryptedHistoryStore
{
    private const int FormatVersion = 3;
    private const int CompactDeltaThreshold = 50;
    private readonly string _legacyPath;
    private readonly string _directory;
    private readonly string _manifestPath;
    private readonly string _basePath;
    private readonly string _deltaPath;
    private readonly object _syncRoot = new();

    public EncryptedHistoryStore(string path)
    {
        _legacyPath = path;
        var root = Path.GetDirectoryName(path) ?? string.Empty;
        _directory = Path.Combine(root, "history");
        _manifestPath = Path.Combine(_directory, "manifest.dat");
        _basePath = Path.Combine(_directory, "base.dat");
        _deltaPath = Path.Combine(_directory, "delta.dat");
    }

    public IReadOnlyList<ClipboardSnapshot> Load()
    {
        lock (_syncRoot)
        {
            if (File.Exists(_manifestPath))
            {
                return LoadSegmented();
            }

            var legacy = LoadLegacy();
            if (legacy.Count > 0)
            {
                SaveCompacted(legacy.Select(ClipboardSnapshotDto.FromSnapshot).ToArray());
                TryMoveLegacyAside();
            }

            return legacy;
        }
    }

    public void Save(IEnumerable<ClipboardSnapshot> snapshots)
    {
        lock (_syncRoot)
        {
            var items = snapshots.Select(ClipboardSnapshotDto.FromSnapshot).ToArray();
            var ids = items.Select(item => item.Id).ToArray();
            var currentIds = ids.ToHashSet(StringComparer.Ordinal);
            var manifest = TryReadManifest();
            if (manifest is null || manifest.Version != FormatVersion || !File.Exists(_basePath))
            {
                SaveCompacted(items);
                return;
            }

            var knownIds = manifest.BaseIds.Concat(manifest.DeltaIds).ToHashSet(StringComparer.Ordinal);
            var newItems = items.TakeWhile(item => !knownIds.Contains(item.Id)).ToArray();
            var existingDelta = File.Exists(_deltaPath)
                ? TryReadProtected<ClipboardSnapshotDto[]>(_deltaPath) ?? []
                : [];
            var delta = newItems
                .Concat(existingDelta)
                .Where(item => currentIds.Contains(item.Id))
                .DistinctBy(item => item.Id)
                .ToArray();

            var baseIds = manifest.BaseIds.Where(currentIds.Contains).ToArray();
            if (delta.Length >= CompactDeltaThreshold || baseIds.Length == 0)
            {
                SaveCompacted(items);
                return;
            }

            if (newItems.Length > 0 || !delta.Select(item => item.Id).SequenceEqual(manifest.DeltaIds.Where(currentIds.Contains)))
            {
                WriteProtected(_deltaPath, delta);
            }

            WriteManifest(new HistoryManifestDto(
                FormatVersion,
                ids,
                baseIds,
                delta.Select(item => item.Id).ToArray()));
        }
    }

    public void Delete()
    {
        lock (_syncRoot)
        {
            if (File.Exists(_legacyPath))
            {
                File.Delete(_legacyPath);
            }

            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    private IReadOnlyList<ClipboardSnapshot> LoadSegmented()
    {
        try
        {
            var manifest = ReadManifest();
            if (manifest.Version != FormatVersion)
            {
                return [];
            }

            var byId = new Dictionary<string, ClipboardSnapshotDto>(StringComparer.Ordinal);
            foreach (var item in File.Exists(_basePath) ? ReadProtected<ClipboardSnapshotDto[]>(_basePath) : [])
            {
                byId[item.Id] = item;
            }

            foreach (var item in File.Exists(_deltaPath) ? ReadProtected<ClipboardSnapshotDto[]>(_deltaPath) : [])
            {
                byId[item.Id] = item;
            }

            return manifest.OrderedIds
                .Select(id => byId.GetValueOrDefault(id))
                .Where(item => item is not null)
                .Select(item => item!.ToSnapshot())
                .ToArray();
        }
        catch (CryptographicException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private IReadOnlyList<ClipboardSnapshot> LoadLegacy()
    {
        if (!File.Exists(_legacyPath))
        {
            return [];
        }

        try
        {
            var encrypted = File.ReadAllBytes(_legacyPath);
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

    private void SaveCompacted(ClipboardSnapshotDto[] items)
    {
        Directory.CreateDirectory(_directory);
        WriteProtected(_basePath, items);
        if (File.Exists(_deltaPath))
        {
            File.Delete(_deltaPath);
        }

        var ids = items.Select(item => item.Id).ToArray();
        WriteManifest(new HistoryManifestDto(FormatVersion, ids, ids, []));
    }

    private void TryMoveLegacyAside()
    {
        try
        {
            if (File.Exists(_legacyPath))
            {
                File.Move(_legacyPath, $"{_legacyPath}.legacy.bak", overwrite: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static T? TryReadProtected<T>(string path)
    {
        try
        {
            return File.Exists(path) ? ReadProtected<T>(path) : default;
        }
        catch (CryptographicException)
        {
            return default;
        }
        catch (JsonException)
        {
            return default;
        }
        catch (IOException)
        {
            return default;
        }
    }

    private HistoryManifestDto? TryReadManifest()
    {
        try
        {
            return File.Exists(_manifestPath) ? ReadManifest() : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private HistoryManifestDto ReadManifest()
    {
        try
        {
            return JsonSerializer.Deserialize<HistoryManifestDto>(File.ReadAllBytes(_manifestPath))!;
        }
        catch (JsonException) when (File.Exists(_manifestPath))
        {
            // Compatibility with pre-release builds that protected the manifest.
            return ReadProtected<HistoryManifestDto>(_manifestPath);
        }
    }

    private void WriteManifest(HistoryManifestDto manifest)
    {
        Directory.CreateDirectory(_directory);
        var json = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = Path.Combine(_directory, $"manifest.dat.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, json);
        File.Move(tempPath, _manifestPath, overwrite: true);
    }

    private static T ReadProtected<T>(string path)
    {
        var encrypted = File.ReadAllBytes(path);
        var json = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<T>(json)!;
    }

    private static void WriteProtected<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true });
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var tempPath = Path.Combine(directory ?? string.Empty, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, encrypted);
        File.Move(tempPath, path, overwrite: true);
    }

    private sealed record HistoryManifestDto(int Version, string[] OrderedIds, string[] BaseIds, string[] DeltaIds);

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
