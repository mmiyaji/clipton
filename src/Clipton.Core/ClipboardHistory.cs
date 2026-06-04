using System.Security.Cryptography;
using System.Text;

namespace Clipton.Core;

public sealed class ClipboardHistory
{
    private readonly List<ClipboardSnapshot> _items = new();
    private readonly Dictionary<string, ClipboardSnapshot> _itemsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClipboardSnapshot> _itemsByFingerprint = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fingerprintsById = new(StringComparer.Ordinal);
    private string? _lastFingerprint;

    public ClipboardHistory(int capacity = 30)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        Capacity = capacity;
    }

    public int Capacity { get; private set; }

    public IReadOnlyList<ClipboardSnapshot> Items => _items;

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
            foreach (var item in _items.Skip(Capacity).ToArray())
            {
                RemoveTracked(item);
            }
        }

        return true;
    }

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
            foreach (var item in _items.Skip(Capacity).ToArray())
            {
                RemoveTracked(item);
            }
        }

        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _itemsById.Clear();
        _itemsByFingerprint.Clear();
        _fingerprintsById.Clear();
        _lastFingerprint = null;
    }

    public bool Remove(string id)
    {
        var removed = _itemsById.TryGetValue(id, out var item) && RemoveTracked(item);
        if (_items.Count == 0)
        {
            _lastFingerprint = null;
        }

        return removed;
    }

    public ClipboardSnapshot? Find(string id) => _itemsById.GetValueOrDefault(id);

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

        foreach (var item in _items.Skip(Capacity).ToArray())
        {
            RemoveTracked(item);
        }
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
        _itemsById.Remove(snapshot.Id);

        if (_fingerprintsById.Remove(snapshot.Id, out var fingerprint))
        {
            _itemsByFingerprint.Remove(fingerprint);
        }

        return removed;
    }

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
