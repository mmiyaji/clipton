using System.Security.Cryptography;
using System.Text;

namespace Clipton.Core;

public sealed class ClipboardHistory
{
    private readonly List<ClipboardSnapshot> _items = new();
    private string? _lastFingerprint;

    public ClipboardHistory(int capacity = 30)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        Capacity = capacity;
    }

    public int Capacity { get; }

    public IReadOnlyList<ClipboardSnapshot> Items => _items;

    public bool Add(ClipboardSnapshot snapshot)
    {
        var fingerprint = CreateFingerprint(snapshot);
        if (fingerprint == _lastFingerprint)
        {
            return false;
        }

        _lastFingerprint = fingerprint;
        _items.RemoveAll(item => CreateFingerprint(item) == fingerprint);
        _items.Insert(0, snapshot);

        if (_items.Count > Capacity)
        {
            _items.RemoveRange(Capacity, _items.Count - Capacity);
        }

        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _lastFingerprint = null;
    }

    public bool Remove(string id)
    {
        var removed = _items.RemoveAll(item => item.Id == id) > 0;
        if (_items.Count == 0)
        {
            _lastFingerprint = null;
        }

        return removed;
    }

    public ClipboardSnapshot? Find(string id) => _items.FirstOrDefault(item => item.Id == id);

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
