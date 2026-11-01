using System.IO.Compression;
using System.Text.Json;
using Clipton.Core;

namespace Clipton.WinUI;

public sealed partial class EncryptedHistoryStore
{
    // Derived, disposable data. Never use this cache for paste or export. Compress
    // before DPAPI so large text remains searchable without retaining every string.
    private const long MenuCacheBudget = 8 * 1024 * 1024;
    private readonly Dictionary<string, MenuCacheEntry> _menuCache = new(StringComparer.Ordinal);
    private long _menuCacheBytes;
    private Dictionary<string, ClipboardSnapshot>? _legacyMenuSnapshots;
    private string MenuDirectory => Path.Combine(_directory, "menu");

    internal void PrepareMenuCache(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var id in GetOrderedIds())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = ItemPath(id);
                var cache = new FileInfo(Path.Combine(MenuDirectory, Path.GetFileName(path)));
                var source = new FileInfo(path);
                if (!source.Exists) continue;

                if (cache.Exists)
                {
                    try
                    {
                        lock (_syncRoot)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var entry = ReadProtected<MenuCacheEntry>(cache.FullName);
                            if (entry?.Matches(source) == true)
                            {
                                // Warm only the bounded compressed bytes. Search can
                                // decompress a matching entry on demand after startup.
                                RememberMenuEntry(id, entry);
                                continue;
                            }
                        }
                    }
                    catch (Exception exception) when (IsTransientReadException(exception) || IsPermanentReadException(exception))
                    {
                        // A missing or unreadable derived cache is rebuilt below.
                    }
                }

                LoadMenuSnapshot(id);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppDiagnostics.Log(exception, "Prepare history menu cache");
        }
    }

    internal string[] GetOrderedIds()
    {
        lock (_syncRoot)
        {
            if (TryReadManifest() is { Version: FormatVersion } manifest)
            {
                return GetExistingItemIds(manifest, repairManifest: true);
            }

            _legacyMenuSnapshots = Load().ToDictionary(item => item.Id, StringComparer.Ordinal);
            return _legacyMenuSnapshots.Keys.ToArray();
        }
    }

    internal ClipboardSnapshot? LoadItem(string id)
    {
        lock (_syncRoot)
        {
            if (TryReadManifest() is { Version: FormatVersion } manifest)
            {
                if (!manifest.OrderedIds.Contains(id, StringComparer.Ordinal)) return null;
                var dto = TryReadProtected<ClipboardSnapshotDto>(ItemPath(id));
                return dto?.Id == id ? dto.ToSnapshot() : null;
            }

            return Load().FirstOrDefault(item => item.Id == id);
        }
    }

    // Callers obtain ids from GetOrderedIds once per query/page, rather than
    // revalidating every manifest entry for each of the 1,000 search candidates.
    internal ClipboardSnapshot? LoadMenuSnapshot(string id)
    {
        lock (_syncRoot)
        {
            var path = ItemPath(id);
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                if (_legacyMenuSnapshots?.GetValueOrDefault(id) is not { } legacy) return null;
                var legacyDto = ClipboardSnapshotDto.FromSnapshot(legacy);
                legacyDto.ImagePng = null;
                return legacyDto.ToSnapshot();
            }
            MenuCacheEntry? entry = null;
            if (_menuCache.TryGetValue(id, out var cached) && cached.Matches(file))
            {
                entry = cached;
            }
            else
            {
                try
                {
                    var cachePath = Path.Combine(MenuDirectory, Path.GetFileName(path));
                    if (File.Exists(cachePath)) entry = ReadProtected<MenuCacheEntry>(cachePath);
                }
                catch (Exception exception) when (IsTransientReadException(exception) || IsPermanentReadException(exception))
                {
                    // A missing/unreadable derived cache must not disable history saves.
                    entry = null;
                }
            }

            if (entry?.Matches(file) == true)
            {
                try
                {
                    using var input = new MemoryStream(entry.Data, writable: false);
                    using var compressed = new BrotliStream(input, CompressionMode.Decompress);
                    var dto = JsonSerializer.Deserialize<ClipboardSnapshotDto>(compressed);
                    if (dto?.Id == id && dto.ImagePng is null)
                    {
                        RememberMenuEntry(id, entry);
                        return dto.ToSnapshot();
                    }
                }
                catch (Exception exception) when (exception is InvalidDataException or JsonException)
                {
                    // Rebuild corrupt cache entries from the authoritative payload.
                }
            }

            var original = TryReadProtected<ClipboardSnapshotDto>(path);
            if (original?.Id != id) return null;
            WriteMenuCache(original, path);
            original.ImagePng = null;
            return original.ToSnapshot();
        }
    }

    private void WriteMenuCache(ClipboardSnapshotDto original, string path)
    {
        try
        {
            var dto = new ClipboardSnapshotDto
            {
                Id = original.Id, CapturedAt = original.CapturedAt, Formats = original.Formats,
                Text = original.Text, Rtf = original.Rtf, Html = original.Html,
                FilePaths = original.FilePaths, SourceApplicationName = original.SourceApplicationName,
                SourceWindowTitle = original.SourceWindowTitle
            };
            using var output = new MemoryStream();
            using (var compressed = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                JsonSerializer.Serialize(compressed, dto);
            }
            var file = new FileInfo(path);
            var entry = new MenuCacheEntry(file.Length, file.LastWriteTimeUtc.Ticks, output.ToArray());
            WriteProtected(Path.Combine(MenuDirectory, Path.GetFileName(path)), entry);
            RememberMenuEntry(original.Id, entry);
        }
        catch (Exception exception) when (IsTransientReadException(exception) || IsPermanentReadException(exception))
        {
            AppDiagnostics.Log(exception, "Write history menu cache");
        }
    }

    private void RememberMenuEntry(string id, MenuCacheEntry entry)
    {
        if (_menuCache.Remove(id, out var previous)) _menuCacheBytes -= previous.Data.Length;
        if (entry.Data.Length > MenuCacheBudget) return;
        if (_menuCacheBytes + entry.Data.Length > MenuCacheBudget)
        {
            return;
        }
        _menuCache[id] = entry;
        _menuCacheBytes += entry.Data.Length;
    }

    private void PruneMenuCache(HashSet<string> ids)
    {
        foreach (var id in _menuCache.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _menuCacheBytes -= _menuCache[id].Data.Length;
            _menuCache.Remove(id);
        }
        if (!Directory.Exists(MenuDirectory)) return;
        foreach (var path in Directory.EnumerateFiles(MenuDirectory, "*.dat"))
        {
            if (!ids.Contains(Path.GetFileNameWithoutExtension(path))) TryDeleteFile(path);
        }
    }

    private sealed record MenuCacheEntry(long Length, long WriteTicks, byte[] Data)
    {
        public bool Matches(FileInfo file) => Data is not null
            && Length == file.Length && WriteTicks == file.LastWriteTimeUtc.Ticks;
    }
}
