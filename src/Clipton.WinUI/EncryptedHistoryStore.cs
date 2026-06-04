using System.Security.Cryptography;
using System.Text.Json;
using Clipton.Core;

namespace Clipton.WinUI;

public sealed class EncryptedHistoryStore
{
    private const int FormatVersion = 4;
    private const int LegacySegmentedFormatVersion = 3;
    private const int ChunkSize = 50;
    private const int CompactDeltaThreshold = 50;
    private readonly string _legacyPath;
    private readonly string _directory;
    private readonly string _manifestPath;
    private readonly string _basePath;
    private readonly string _deltaPath;
    private readonly string _headPath;
    private readonly string _chunksDirectory;
    private readonly object _syncRoot = new();

    public EncryptedHistoryStore(string path)
    {
        _legacyPath = path;
        var root = Path.GetDirectoryName(path) ?? string.Empty;
        _directory = Path.Combine(root, "history");
        _manifestPath = Path.Combine(_directory, "manifest.dat");
        _basePath = Path.Combine(_directory, "base.dat");
        _deltaPath = Path.Combine(_directory, "delta.dat");
        _headPath = Path.Combine(_directory, "head.dat");
        _chunksDirectory = Path.Combine(_directory, "chunks");
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

    public int Count()
    {
        lock (_syncRoot)
        {
            if (File.Exists(_manifestPath) && TryReadManifest() is { } manifest && IsSupportedManifestVersion(manifest.Version))
            {
                return manifest.OrderedIds.Length;
            }

            return LoadLegacy().Count;
        }
    }

    public IReadOnlyList<ClipboardSnapshot> LoadRange(int offset, int count)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must not be negative.");
        }

        if (count <= 0)
        {
            return [];
        }

        lock (_syncRoot)
        {
            try
            {
                if (TryReadManifest() is { Version: FormatVersion } manifest)
                {
                    return LoadRangeFromChunks(manifest, offset, count);
                }

                return LoadOrderedDtos()
                    .Skip(offset)
                    .Take(count)
                    .Select(item => item.ToSnapshot())
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
    }

    public IReadOnlyList<ClipboardSnapshot> LoadRecent(int count)
    {
        return LoadRange(0, count);
    }

    public void Save(IEnumerable<ClipboardSnapshot> snapshots)
    {
        lock (_syncRoot)
        {
            var items = snapshots.Select(ClipboardSnapshotDto.FromSnapshot).ToArray();
            var ids = items.Select(item => item.Id).ToArray();
            var manifest = TryReadManifest();
            if (manifest is null || manifest.Version != FormatVersion || !Directory.Exists(_chunksDirectory))
            {
                SaveCompacted(items);
                return;
            }

            if (TrySaveHeadDelta(items, ids, manifest))
            {
                return;
            }

            SaveCompacted(items);
        }
    }

    public void SavePreservingOlder(IEnumerable<ClipboardSnapshot> snapshots, int loadedPersistedCount, int capacity)
    {
        lock (_syncRoot)
        {
            var currentItems = snapshots.Select(ClipboardSnapshotDto.FromSnapshot).ToArray();
            var currentIds = currentItems.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var manifest = TryReadManifest();
            if (manifest is { Version: FormatVersion } && Directory.Exists(_chunksDirectory))
            {
                if (TrySavePreservingOlderHeadDelta(currentItems, currentIds, loadedPersistedCount, capacity, manifest))
                {
                    return;
                }
            }

            var remainingCapacity = Math.Max(0, capacity - currentItems.Length);
            var olderItems = remainingCapacity == 0
                ? []
                : LoadOrderedDtos()
                    .Skip(Math.Max(0, loadedPersistedCount))
                    .Where(item => !currentIds.Contains(item.Id))
                    .Take(remainingCapacity)
                    .ToArray();
            Save(currentItems.Concat(olderItems).Take(capacity).Select(item => item.ToSnapshot()));
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
            return LoadOrderedDtos()
                .Select(item => item.ToSnapshot())
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

    private IReadOnlyList<ClipboardSnapshotDto> LoadOrderedDtos()
    {
        if (File.Exists(_manifestPath))
        {
            var manifest = ReadManifest();
            if (!IsSupportedManifestVersion(manifest.Version))
            {
                return [];
            }

            if (manifest.Version == FormatVersion)
            {
                return LoadDtosFromChunks(manifest, 0, manifest.OrderedIds.Length);
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
                .Select(item => item!)
                .ToArray();
        }

        if (!File.Exists(_legacyPath))
        {
            return [];
        }

        var encrypted = File.ReadAllBytes(_legacyPath);
        var json = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<ClipboardSnapshotDto[]>(json) ?? [];
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
        SaveChunks(items);
        if (File.Exists(_deltaPath))
        {
            File.Delete(_deltaPath);
        }

        if (File.Exists(_headPath))
        {
            File.Delete(_headPath);
        }

        if (File.Exists(_basePath))
        {
            File.Delete(_basePath);
        }

        var ids = items.Select(item => item.Id).ToArray();
        var chunkIds = Enumerable.Range(0, (items.Length + ChunkSize - 1) / ChunkSize)
            .Select(index => ChunkFileName(index))
            .ToArray();
        WriteManifest(new HistoryManifestDto(FormatVersion, ids, ids, [], chunkIds));
    }

    private bool TrySaveHeadDelta(ClipboardSnapshotDto[] items, string[] ids, HistoryManifestDto manifest)
    {
        if (ids.SequenceEqual(manifest.OrderedIds))
        {
            return true;
        }

        var knownIds = manifest.OrderedIds.ToHashSet(StringComparer.Ordinal);
        var newItems = items.TakeWhile(item => !knownIds.Contains(item.Id)).ToArray();
        if (newItems.Length == 0)
        {
            return false;
        }

        if (!ids.Skip(newItems.Length).SequenceEqual(manifest.OrderedIds.Take(ids.Length - newItems.Length)))
        {
            return false;
        }

        return TryWriteHeadDelta(newItems, ids, manifest);
    }

    private bool TrySavePreservingOlderHeadDelta(
        ClipboardSnapshotDto[] currentItems,
        HashSet<string> currentIds,
        int loadedPersistedCount,
        int capacity,
        HistoryManifestDto manifest)
    {
        var currentItemIds = currentItems.Select(item => item.Id).ToArray();
        var knownIds = manifest.OrderedIds.ToHashSet(StringComparer.Ordinal);
        var newItems = currentItems.TakeWhile(item => !knownIds.Contains(item.Id)).ToArray();
        var expectedLoadedCount = Math.Max(0, Math.Min(loadedPersistedCount, currentItems.Length) - newItems.Length);
        if (!currentItemIds.Skip(newItems.Length).Take(expectedLoadedCount).SequenceEqual(manifest.OrderedIds.Take(expectedLoadedCount)))
        {
            return false;
        }

        var ids = currentItemIds
            .Concat(manifest.OrderedIds.Skip(expectedLoadedCount).Where(id => !currentIds.Contains(id)))
            .Take(capacity)
            .ToArray();
        if (ids.SequenceEqual(manifest.OrderedIds))
        {
            return true;
        }

        return TryWriteHeadDelta(newItems, ids, manifest);
    }

    private bool TryWriteHeadDelta(ClipboardSnapshotDto[] newItems, string[] ids, HistoryManifestDto manifest)
    {
        var existingHead = ReadHeadDtos(manifest);
        var head = newItems
            .Concat(existingHead)
            .Where(item => ids.Contains(item.Id, StringComparer.Ordinal))
            .DistinctBy(item => item.Id)
            .ToArray();
        if (head.Length >= CompactDeltaThreshold)
        {
            return false;
        }

        WriteHead(head);
        WriteManifest(manifest with
        {
            OrderedIds = ids,
            BaseIds = ids,
            DeltaIds = head.Select(item => item.Id).ToArray()
        });
        return true;
    }

    private IReadOnlyList<ClipboardSnapshot> LoadRangeFromChunks(HistoryManifestDto manifest, int offset, int count)
    {
        return LoadDtosFromChunks(manifest, offset, count)
            .Select(item => item.ToSnapshot())
            .ToArray();
    }

    private IReadOnlyList<ClipboardSnapshotDto> LoadDtosFromChunks(HistoryManifestDto manifest, int offset, int count)
    {
        if (manifest.OrderedIds.Length == 0 || offset >= manifest.OrderedIds.Length)
        {
            return [];
        }

        var end = Math.Min(manifest.OrderedIds.Length, offset + count);
        var result = new List<ClipboardSnapshotDto>(end - offset);
        var headIds = manifest.DeltaIds;
        if (offset < headIds.Length)
        {
            var head = ReadHeadDtos(manifest);
            var headEnd = Math.Min(end, head.Count);
            for (var index = offset; index < headEnd; index++)
            {
                result.Add(head[index]);
            }

            if (end <= headIds.Length)
            {
                return result;
            }
        }

        var chunkOffset = Math.Max(0, offset - headIds.Length);
        var chunkEnd = end - headIds.Length;
        var firstChunk = chunkOffset / ChunkSize;
        var lastChunk = (chunkEnd - 1) / ChunkSize;

        for (var chunkIndex = firstChunk; chunkIndex <= lastChunk; chunkIndex++)
        {
            var path = ChunkPath(chunkIndex);
            if (!File.Exists(path))
            {
                continue;
            }

            var chunk = ReadProtected<ClipboardSnapshotDto[]>(path);
            var chunkStart = chunkIndex * ChunkSize;
            var localStart = Math.Max(0, chunkOffset - chunkStart);
            var localEnd = Math.Min(chunk.Length, chunkEnd - chunkStart);
            for (var index = localStart; index < localEnd; index++)
            {
                result.Add(chunk[index]);
            }
        }

        return result;
    }

    private IReadOnlyList<ClipboardSnapshotDto> ReadHeadDtos(HistoryManifestDto manifest)
    {
        if (manifest.DeltaIds.Length == 0)
        {
            return [];
        }

        if (!File.Exists(_headPath))
        {
            return [];
        }

        var byId = ReadProtected<ClipboardSnapshotDto[]>(_headPath)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        return manifest.DeltaIds
            .Select(id => byId.GetValueOrDefault(id))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
    }

    private void WriteHead(ClipboardSnapshotDto[] head)
    {
        if (head.Length == 0)
        {
            if (File.Exists(_headPath))
            {
                File.Delete(_headPath);
            }

            return;
        }

        WriteProtected(_headPath, head);
    }

    private void SaveChunks(ClipboardSnapshotDto[] items)
    {
        CleanupStaleChunkDirectories();
        var tempChunksDirectory = Path.Combine(_directory, $"chunks.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(tempChunksDirectory);
        try
        {
            for (var index = 0; index * ChunkSize < items.Length; index++)
            {
                var chunk = items
                    .Skip(index * ChunkSize)
                    .Take(ChunkSize)
                    .ToArray();
                WriteProtected(Path.Combine(tempChunksDirectory, ChunkFileName(index)), chunk);
            }

            var oldChunksDirectory = Path.Combine(_directory, $"chunks.{Guid.NewGuid():N}.old");
            if (Directory.Exists(_chunksDirectory))
            {
                Directory.Move(_chunksDirectory, oldChunksDirectory);
            }

            Directory.Move(tempChunksDirectory, _chunksDirectory);
            if (Directory.Exists(oldChunksDirectory))
            {
                Directory.Delete(oldChunksDirectory, recursive: true);
            }
        }
        catch
        {
            if (Directory.Exists(tempChunksDirectory))
            {
                Directory.Delete(tempChunksDirectory, recursive: true);
            }

            throw;
        }
    }

    private void CleanupStaleChunkDirectories()
    {
        if (!Directory.Exists(_directory))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(_directory)
            .Where(path => Path.GetFileName(path).StartsWith("chunks.", StringComparison.Ordinal)))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private string ChunkPath(int index)
    {
        return Path.Combine(_chunksDirectory, ChunkFileName(index));
    }

    private static string ChunkFileName(int index)
    {
        return $"chunk-{index:0000}.dat";
    }

    private static bool IsSupportedManifestVersion(int version)
    {
        return version is FormatVersion or LegacySegmentedFormatVersion;
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

    private sealed record HistoryManifestDto(
        int Version,
        string[] OrderedIds,
        string[] BaseIds,
        string[] DeltaIds,
        string[]? ChunkIds = null);

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
