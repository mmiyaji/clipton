using System.Security.Cryptography;
using System.Text;

namespace Clipton.Core;

/// <summary>
/// In-memory clipboard history with newest-first ordering and content de-duplication.
/// </summary>
/// <remarks>
/// History keeps parallel indexes for ids and fingerprints so common UI operations stay
/// O(1) even when a larger persisted history is paged into memory. Fingerprints are based
/// on clipboard payload only; source metadata is display context and should not split
/// otherwise identical history items.
/// </remarks>
public sealed class ClipboardHistory
{
    private readonly List<ClipboardSnapshot> _items = new();
    private readonly Dictionary<string, ClipboardSnapshot> _itemsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClipboardSnapshot> _itemsByFingerprint = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fingerprintsById = new(StringComparer.Ordinal);
    private string? _lastFingerprint;

    /// <summary>
    /// Creates an empty history with a maximum resident item count.
    /// </summary>
    /// <param name="capacity">Maximum number of snapshots retained in memory.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is not positive.</exception>
    public ClipboardHistory(int capacity = 30)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        Capacity = capacity;
    }

    /// <summary>Maximum number of snapshots retained in newest-first order.</summary>
    public int Capacity { get; private set; }

    /// <summary>Resident history items, newest first.</summary>
    public IReadOnlyList<ClipboardSnapshot> Items => _items;

    /// <summary>
    /// Adds a newly captured snapshot to the front of history.
    /// </summary>
    /// <remarks>
    /// Consecutive duplicates are ignored so clipboard-change storms do not create noise.
    /// Non-consecutive duplicates are moved to the front by removing the older occurrence.
    /// </remarks>
    /// <returns><see langword="true"/> when history changed.</returns>
    public bool Add(ClipboardSnapshot snapshot)
    {
        var fingerprint = CreateFingerprint(snapshot);
        if (fingerprint == _lastFingerprint)
        {
            return false;
        }

        _lastFingerprint = fingerprint;
        if (_itemsByFingerprint.TryGetValue(fingerprint, out var duplicate))
        {
            RemoveTracked(duplicate);
        }

        _items.Insert(0, snapshot);
        Track(snapshot, fingerprint);

        if (_items.Count > Capacity)
        {
            RemoveOlderBeyond(Capacity);
        }

        return true;
    }

    /// <summary>
    /// Appends an older persisted snapshot while paging history from storage.
    /// </summary>
    /// <remarks>
    /// This preserves newest-first ordering without updating the consecutive duplicate
    /// guard used for live clipboard captures.
    /// </remarks>
    /// <returns><see langword="true"/> when the snapshot was not already represented.</returns>
    public bool AppendOlder(ClipboardSnapshot snapshot)
    {
        var fingerprint = CreateFingerprint(snapshot);
        if (_itemsByFingerprint.ContainsKey(fingerprint))
        {
            return false;
        }

        _items.Add(snapshot);
        Track(snapshot, fingerprint);

        if (_items.Count > Capacity)
        {
            RemoveOlderBeyond(Capacity);
        }

        return true;
    }

    /// <summary>Removes all resident items and resets duplicate tracking.</summary>
    public void Clear()
    {
        _items.Clear();
        _itemsById.Clear();
        _itemsByFingerprint.Clear();
        _fingerprintsById.Clear();
        _lastFingerprint = null;
    }

    /// <summary>
    /// Removes one resident snapshot by id.
    /// </summary>
    /// <returns><see langword="true"/> when an item was removed.</returns>
    public bool Remove(string id)
    {
        var removed = _itemsById.TryGetValue(id, out var item) && RemoveTracked(item);
        if (_items.Count == 0)
        {
            _lastFingerprint = null;
        }

        return removed;
    }

    /// <summary>
    /// Evicts resident snapshots after the specified newest item count.
    /// </summary>
    /// <remarks>
    /// Callers use the returned snapshots to keep persistence in sync while reducing
    /// memory pressure from long histories.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is negative.</exception>
    public IReadOnlyList<ClipboardSnapshot> UnloadOlderBeyond(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Count must not be negative.");
        }

        if (_items.Count <= count)
        {
            return [];
        }

        var removed = RemoveOlderBeyond(count);

        if (_items.Count == 0)
        {
            _lastFingerprint = null;
        }

        return removed;
    }

    /// <summary>Finds a resident snapshot by id.</summary>
    public ClipboardSnapshot? Find(string id) => _itemsById.GetValueOrDefault(id);

    /// <summary>
    /// Changes the resident capacity and trims older items if needed.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is not positive.</exception>
    public void SetCapacity(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        Capacity = capacity;
        if (_items.Count <= Capacity)
        {
            return;
        }

        RemoveOlderBeyond(Capacity);
    }

    private void Track(ClipboardSnapshot snapshot, string fingerprint)
    {
        _itemsById[snapshot.Id] = snapshot;
        _itemsByFingerprint[fingerprint] = snapshot;
        _fingerprintsById[snapshot.Id] = fingerprint;
    }

    private bool RemoveTracked(ClipboardSnapshot snapshot)
    {
        var removed = _items.Remove(snapshot);
        Untrack(snapshot);

        return removed;
    }

    private IReadOnlyList<ClipboardSnapshot> RemoveOlderBeyond(int count)
    {
        if (_items.Count <= count)
        {
            return [];
        }

        var removeCount = _items.Count - count;
        var removed = _items.GetRange(count, removeCount);
        _items.RemoveRange(count, removeCount);
        foreach (var item in removed)
        {
            Untrack(item);
        }

        return removed;
    }

    private void Untrack(ClipboardSnapshot snapshot)
    {
        _itemsById.Remove(snapshot.Id);

        if (_fingerprintsById.Remove(snapshot.Id, out var fingerprint))
        {
            _itemsByFingerprint.Remove(fingerprint);
        }
    }

    /// <summary>
    /// Creates the payload fingerprint used for history de-duplication.
    /// </summary>
    /// <remarks>
    /// The hash includes supported content formats and payload bytes, but deliberately
    /// ignores id, capture time and source metadata.
    /// </remarks>
    public static string CreateFingerprint(ClipboardSnapshot snapshot)
    {
        using var sha = SHA256.Create();
        var builder = new StringBuilder();
        builder.AppendJoin(",", snapshot.Formats).Append('\n');
        builder.Append(snapshot.Text).Append('\n');
        builder.Append(snapshot.Rtf).Append('\n');
        builder.Append(snapshot.Html).Append('\n');
        builder.AppendJoin("|", snapshot.FilePaths).Append('\n');

        var textBytes = Encoding.UTF8.GetBytes(builder.ToString());
        sha.TransformBlock(textBytes, 0, textBytes.Length, null, 0);

        if (snapshot.ImagePng is { Length: > 0 })
        {
            sha.TransformBlock(snapshot.ImagePng, 0, snapshot.ImagePng.Length, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }
}
