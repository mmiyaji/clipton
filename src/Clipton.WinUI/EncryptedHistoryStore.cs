using System.Security.Cryptography;
using System.Text.Json;
using Clipton.Core;

namespace Clipton.WinUI;

/// <summary>
/// Persists clipboard history using Windows user-scoped DPAPI.
/// </summary>
/// <remarks>
/// Current history uses an itemized format: a protected manifest stores ordering and each
/// payload is encrypted in its own file. Older single-file, segmented and chunked formats
/// remain readable so users can upgrade without losing local history.
/// </remarks>
public sealed class EncryptedHistoryStore
{
    // Format 5 separates ordering from item payloads. Both the manifest and item files are
    // DPAPI-protected; keeping items individual makes small saves cheaper and lets the runtime
    // page older history without decrypting everything at startup.
    private const int FormatVersion = 5;
    private const int ChunkedFormatVersion = 4;
    private const int LegacySegmentedFormatVersion = 3;
    private const int ChunkSize = 50;
    private readonly string _legacyPath;
    private readonly string _directory;
    private readonly string _manifestPath;
    private readonly string _basePath;
    private readonly string _deltaPath;
    private readonly string _headPath;
    private readonly string _chunksDirectory;
    private readonly string _itemsDirectory;
    private readonly object _syncRoot = new();
    private bool _suppressWritesAfterTransientReadFailure;

    /// <summary>
    /// Creates a store rooted next to the legacy encrypted history file path.
    /// </summary>
    /// <param name="path">Path used by legacy single-file history.</param>
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
        _itemsDirectory = Path.Combine(_directory, "items");
    }

    /// <summary>
    /// Loads all supported history items, upgrading legacy single-file history when found.
    /// </summary>
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

    /// <summary>
    /// Returns the persisted item count without decrypting item payloads when possible.
    /// </summary>
    public int Count()
    {
        lock (_syncRoot)
        {
            if (File.Exists(_manifestPath) && TryReadManifest() is { } manifest && IsSupportedManifestVersion(manifest.Version))
            {
                if (manifest.Version == FormatVersion)
                {
                    return GetExistingItemIds(manifest, repairManifest: true).Length;
                }

                return manifest.OrderedIds.Length;
            }

            return LoadLegacy().Count;
        }
    }

    /// <summary>
    /// Loads a newest-first range from the persisted history.
    /// </summary>
    /// <param name="offset">Zero-based item offset.</param>
    /// <param name="count">Maximum number of items to load.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative.</exception>
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
                    return LoadRangeFromItems(manifest, offset, count);
                }

                if (TryReadManifest() is { Version: ChunkedFormatVersion } chunkedManifest)
                {
                    return LoadRangeFromChunks(chunkedManifest, offset, count);
                }

                return LoadOrderedDtos()
                    .Skip(offset)
                    .Take(count)
                    .Select(item => item.ToSnapshot())
                    .ToArray();
            }
            catch (Exception exception) when (IsTransientReadException(exception))
            {
                MarkTransientReadFailure(exception, "Load history range");
                return [];
            }
            catch (Exception exception) when (IsPermanentReadException(exception))
            {
                AppDiagnostics.Log(exception, "Load history range");
                return [];
            }
        }
    }

    /// <summary>
    /// Loads the newest persisted items.
    /// </summary>
    public IReadOnlyList<ClipboardSnapshot> LoadRecent(int count)
    {
        return LoadRange(0, count);
    }

    /// <summary>
    /// Loads every persisted history item for a complete export, or throws when the
    /// committed ordering and readable payloads do not match exactly.
    /// </summary>
    /// <remarks>
    /// Unlike normal paging reads, this method never repairs a manifest or suppresses a
    /// payload failure. The manifest and all referenced payloads are read under one lock
    /// so callers cannot mistake a partial backup for a complete export.
    /// </remarks>
    public IReadOnlyList<ClipboardSnapshot> LoadAllStrict()
    {
        lock (_syncRoot)
        {
            try
            {
                if (!File.Exists(_manifestPath))
                {
                    if (Directory.Exists(_directory))
                    {
                        throw new InvalidDataException(
                            "The persisted history directory exists without its required manifest.");
                    }

                    return LoadLegacyStrict();
                }

                HistoryManifestDto? manifest = ReadManifest();
                if (manifest is null
                    || manifest.OrderedIds is null
                    || !IsSupportedManifestVersion(manifest.Version))
                {
                    throw new InvalidDataException("The persisted history manifest is not supported.");
                }

                var items = LoadOrderedDtos(manifest, repairItemizedManifest: false).ToArray();
                if (items.Any(item => item is null)
                    || !manifest.OrderedIds.SequenceEqual(items.Select(item => item.Id), StringComparer.Ordinal))
                {
                    throw new InvalidDataException(
                        "The persisted history manifest does not match all readable item payloads.");
                }

                return items.Select(item => item.ToSnapshot()).ToArray();
            }
            catch (Exception exception) when (IsTransientReadException(exception))
            {
                MarkTransientReadFailure(exception, "Load complete history for export");
                throw;
            }
        }
    }

    /// <summary>
    /// Replaces persisted history with the supplied resident snapshots.
    /// </summary>
    public void Save(IEnumerable<ClipboardSnapshot> snapshots)
    {
        lock (_syncRoot)
        {
            ThrowIfWritesSuppressed();
            var items = snapshots.Select(ClipboardSnapshotDto.FromSnapshot).ToArray();
            var ids = items.Select(item => item.Id).ToArray();
            SaveItemized(items, ids);
        }
    }

    /// <summary>
    /// Saves resident snapshots while preserving older persisted items that are not loaded.
    /// </summary>
    /// <remarks>
    /// The runtime only keeps a prefix of large histories in memory. This method merges
    /// that prefix with still-persisted older ids so settings changes or new captures do
    /// not accidentally truncate the rest of the user's history.
    /// </remarks>
    public void SavePreservingOlder(IEnumerable<ClipboardSnapshot> snapshots, int loadedPersistedCount, int capacity)
    {
        lock (_syncRoot)
        {
            ThrowIfWritesSuppressed();
            var currentItems = snapshots.Select(ClipboardSnapshotDto.FromSnapshot).ToArray();
            var currentIds = currentItems.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var manifest = TryReadManifest();
            if (manifest is { Version: FormatVersion })
            {
                var persistedIds = GetExistingItemIds(manifest, repairManifest: true);
                var ids = currentItems
                    .Select(item => item.Id)
                    .Concat(persistedIds
                        .Skip(Math.Max(0, loadedPersistedCount))
                        .Where(id => !currentIds.Contains(id)))
                    .Take(capacity)
                    .ToArray();
                SaveItemized(currentItems, ids);
                return;
            }

            var remainingCapacity = Math.Max(0, capacity - currentItems.Length);
            var olderItems = remainingCapacity == 0
                ? []
                : LoadOrderedDtos()
                    .Skip(Math.Max(0, loadedPersistedCount))
                    .Where(item => !currentIds.Contains(item.Id))
                    .Take(remainingCapacity)
                    .ToArray();
            var items = currentItems.Concat(olderItems).Take(capacity).ToArray();
            SaveItemized(items, items.Select(item => item.Id).ToArray());
        }
    }

    /// <summary>
    /// Deletes every persisted history representation owned by this store.
    /// </summary>
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

    /// <summary>
    /// Removes source application and window-title metadata from every persisted item.
    /// </summary>
    public void ClearSourceMetadata()
    {
        lock (_syncRoot)
        {
            ClipboardSnapshotDto[] items;
            if (File.Exists(_manifestPath))
            {
                var manifest = ReadManifest();
                items = LoadOrderedDtos(manifest, repairItemizedManifest: false).ToArray();
                if (!manifest.OrderedIds.SequenceEqual(items.Select(item => item.Id), StringComparer.Ordinal))
                {
                    AppDiagnostics.Warning(
                        "Clear history source metadata",
                        "History could not be read completely; preserving the existing manifest and payload files.");
                    return;
                }
            }
            else
            {
                items = LoadOrderedDtos().ToArray();
            }

            if (items.Length == 0
                || items.All(item => string.IsNullOrWhiteSpace(item.SourceApplicationName)
                    && string.IsNullOrWhiteSpace(item.SourceWindowTitle)))
            {
                return;
            }

            foreach (var item in items)
            {
                item.SourceApplicationName = null;
                item.SourceWindowTitle = null;
            }

            SaveItemized(items, items.Select(item => item.Id).ToArray(), overwriteExistingItems: true);
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
        catch (Exception exception) when (IsTransientReadException(exception))
        {
            MarkTransientReadFailure(exception, "Load history");
            return [];
        }
        catch (Exception exception) when (IsPermanentReadException(exception))
        {
            AppDiagnostics.Log(exception, "Load history");
            return [];
        }
    }

    private IReadOnlyList<ClipboardSnapshotDto> LoadOrderedDtos()
    {
        if (File.Exists(_manifestPath))
        {
            return LoadOrderedDtos(ReadManifest(), repairItemizedManifest: true);
        }

        if (!File.Exists(_legacyPath))
        {
            return [];
        }

        var encrypted = File.ReadAllBytes(_legacyPath);
        var json = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<ClipboardSnapshotDto[]>(json) ?? [];
    }

    private IReadOnlyList<ClipboardSnapshotDto> LoadOrderedDtos(
        HistoryManifestDto manifest,
        bool repairItemizedManifest)
    {
        if (!IsSupportedManifestVersion(manifest.Version))
        {
            return [];
        }

        if (manifest.Version == FormatVersion)
        {
            return LoadDtosFromItems(manifest, 0, manifest.OrderedIds.Length, repairItemizedManifest);
        }

        if (manifest.Version == ChunkedFormatVersion)
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

    private IReadOnlyList<ClipboardSnapshot> LoadLegacyStrict()
    {
        if (!File.Exists(_legacyPath))
        {
            return [];
        }

        var encrypted = File.ReadAllBytes(_legacyPath);
        var json = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var items = JsonSerializer.Deserialize<ClipboardSnapshotDto[]>(json)
            ?? throw new InvalidDataException("The persisted legacy history payload is empty.");
        if (items.Any(item => item is null))
        {
            throw new InvalidDataException("The persisted legacy history contains an invalid item.");
        }

        return items.Select(item => item.ToSnapshot()).ToArray();
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
        WriteManifest(new HistoryManifestDto(ChunkedFormatVersion, ids, ids, [], chunkIds));
    }

    private void SaveItemized(IReadOnlyList<ClipboardSnapshotDto> items, string[] ids, bool overwriteExistingItems = false)
    {
        var idsToKeep = ids.ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            _ = ItemPath(id);
        }

        var itemPaths = items
            .Select(item => (Item: item, Path: ItemPath(item.Id)))
            .ToArray();

        Directory.CreateDirectory(_itemsDirectory);
        foreach (var (item, path) in itemPaths)
        {
            if (!idsToKeep.Contains(item.Id))
            {
                continue;
            }

            if (overwriteExistingItems || !File.Exists(path))
            {
                WriteProtected(path, item);
            }
        }

        // Commit the new ordering before destructive cleanup. If the manifest write fails,
        // the previously committed manifest still has all of its referenced payload files.
        WriteManifest(new HistoryManifestDto(FormatVersion, ids, ids, []));

        // The manifest is the ordering source of truth; item files not referenced by it
        // are stale encrypted payloads and should be removed after each complete save.
        if (Directory.Exists(_itemsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(_itemsDirectory, "*.dat"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (!idsToKeep.Contains(id))
                {
                    TryDeleteFile(file);
                }
            }
        }

        TryDeleteFile(_deltaPath);
        TryDeleteFile(_headPath);
        TryDeleteFile(_basePath);
        if (Directory.Exists(_chunksDirectory))
        {
            TryDeleteDirectory(_chunksDirectory);
        }

        CleanupStaleChunkDirectories();
    }

    private IReadOnlyList<ClipboardSnapshot> LoadRangeFromItems(HistoryManifestDto manifest, int offset, int count)
    {
        return LoadDtosFromItems(manifest, offset, count)
            .Select(item => item.ToSnapshot())
            .ToArray();
    }

    private IReadOnlyList<ClipboardSnapshotDto> LoadDtosFromItems(
        HistoryManifestDto manifest,
        int offset,
        int count,
        bool repairManifest = true)
    {
        var existingIds = GetExistingItemIds(manifest, repairManifest);
        if (existingIds.Length == 0 || offset >= existingIds.Length)
        {
            return [];
        }

        return existingIds
            .Skip(offset)
            .Take(count)
            .Select(id => TryReadProtected<ClipboardSnapshotDto>(ItemPath(id)))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
    }

    private string[] GetExistingItemIds(HistoryManifestDto manifest, bool repairManifest)
    {
        if (manifest.OrderedIds.Length == 0)
        {
            return [];
        }

        var existingIds = new List<string>(manifest.OrderedIds.Length);
        var missingCount = 0;
        foreach (var id in manifest.OrderedIds)
        {
            string path;
            try
            {
                path = ItemPath(id);
            }
            catch (InvalidDataException)
            {
                missingCount++;
                continue;
            }

            if (File.Exists(path))
            {
                existingIds.Add(id);
            }
            else
            {
                missingCount++;
            }
        }

        if (missingCount == 0)
        {
            return existingIds.ToArray();
        }

        AppDiagnostics.Warning(
            "History manifest",
            $"Manifest referenced {missingCount} missing item file(s); repairing itemized history order.");
        if (repairManifest)
        {
            var ids = existingIds.ToArray();
            WriteManifest(new HistoryManifestDto(FormatVersion, ids, ids, []));
            return ids;
        }

        return existingIds.ToArray();
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

    private string ItemPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("History item id is not a valid file name.");
        }

        try
        {
            var itemsDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_itemsDirectory));
            var path = Path.GetFullPath(Path.Combine(itemsDirectory, $"{id}.dat"));
            var directoryPrefix = $"{itemsDirectory}{Path.DirectorySeparatorChar}";
            if (!path.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetDirectoryName(path), itemsDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("History item path is outside the item storage directory.");
            }

            return path;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDataException("History item id cannot be converted to a safe storage path.", exception);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string ChunkFileName(int index)
    {
        return $"chunk-{index:0000}.dat";
    }

    private static bool IsSupportedManifestVersion(int version)
    {
        return version is FormatVersion or ChunkedFormatVersion or LegacySegmentedFormatVersion;
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

    private T? TryReadProtected<T>(string path)
    {
        try
        {
            return File.Exists(path) ? ReadProtected<T>(path) : default;
        }
        catch (Exception exception) when (IsTransientReadException(exception))
        {
            MarkTransientReadFailure(exception, $"Read protected history item {Path.GetFileName(path)}");
            return default;
        }
        catch (Exception exception) when (IsPermanentReadException(exception))
        {
            AppDiagnostics.Log(exception, $"Read protected history item {Path.GetFileName(path)}");
            return default;
        }
    }

    private HistoryManifestDto? TryReadManifest()
    {
        try
        {
            return File.Exists(_manifestPath) ? ReadManifest() : null;
        }
        catch (Exception exception) when (IsTransientReadException(exception))
        {
            MarkTransientReadFailure(exception, "Read history manifest");
            return null;
        }
        catch (Exception exception) when (IsPermanentReadException(exception))
        {
            AppDiagnostics.Log(exception, "Read history manifest");
            return null;
        }
    }

    private HistoryManifestDto ReadManifest()
    {
        try
        {
            return ReadProtected<HistoryManifestDto>(_manifestPath);
        }
        catch (Exception exception) when (File.Exists(_manifestPath) && IsPermanentReadException(exception))
        {
            // Compatibility with older builds that wrote manifest.dat as plaintext JSON.
            return JsonSerializer.Deserialize<HistoryManifestDto>(File.ReadAllBytes(_manifestPath))!;
        }
    }

    private void WriteManifest(HistoryManifestDto manifest)
    {
        WriteProtected(_manifestPath, manifest);
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

    private void ThrowIfWritesSuppressed()
    {
        if (_suppressWritesAfterTransientReadFailure)
        {
            throw new IOException("History store had a transient read failure; refusing to overwrite existing history in this session.");
        }
    }

    private void MarkTransientReadFailure(Exception exception, string context)
    {
        _suppressWritesAfterTransientReadFailure = true;
        AppDiagnostics.Log(exception, context);
    }

    private static bool IsTransientReadException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException;
    }

    private static bool IsPermanentReadException(Exception exception)
    {
        return exception is CryptographicException or JsonException;
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
        public string? SourceApplicationName { get; set; }
        public string? SourceWindowTitle { get; set; }

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
                FilePaths = snapshot.FilePaths.ToArray(),
                SourceApplicationName = snapshot.SourceApplicationName,
                SourceWindowTitle = snapshot.SourceWindowTitle
            };
        }

        public ClipboardSnapshot ToSnapshot()
        {
            return new ClipboardSnapshot(
                Id,
                CapturedAt,
                Formats,
                Text,
                Rtf,
                Html,
                ImagePng,
                FilePaths,
                SourceApplicationName,
                SourceWindowTitle);
        }
    }
}
