using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Atria.Resources;

public sealed record DialogueEntry(ReadOnlyMemory<byte> RawKey, uint Offset, uint Length);

/// <summary>Byte-identity dialogue keys; no CP949/CP1251 conversion in lookup.</summary>
public sealed class DialogueCatalog
{
    private readonly ReadOnlyMemory<byte> data;
    private readonly DialogueEntry[] entries;
    private readonly Dictionary<ulong, List<int>> buckets = new();
    public IReadOnlyList<DialogueEntry> Entries { get; }

    private DialogueCatalog(ReadOnlyMemory<byte> data, DialogueEntry[] entries)
    {
        this.data = data;
        this.entries = entries;
        Entries = Array.AsReadOnly(entries);
        for (int i = 0; i < entries.Length; i++)
        {
            ReadOnlySpan<byte> key = entries[i].RawKey.Span;
            ulong hash = Hash(key);
            if (!buckets.TryGetValue(hash, out List<int>? bucket)) buckets.Add(hash, bucket = new List<int>());
            foreach (int existing in bucket)
                if (entries[existing].RawKey.Span.SequenceEqual(key)) throw new InvalidDataException("Duplicate dialogue key.");
            bucket.Add(i);
        }
    }

    public static DialogueCatalog Load(string indexPath, string dataPath) => Parse(File.ReadAllBytes(indexPath), File.ReadAllBytes(dataPath));

    public static DialogueCatalog Parse(ReadOnlyMemory<byte> index, ReadOnlyMemory<byte> data)
    {
        if (index.Length < 4) throw new InvalidDataException("Truncated dialogue index.");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(index.Span);
        if (4L + 56L * count != index.Length) throw new InvalidDataException("Dialogue index record count does not match length.");
        DialogueEntry[] entries = new DialogueEntry[(int)count];
        for (int i = 0; i < entries.Length; i++)
        {
            ReadOnlySpan<byte> record = index.Span.Slice(4 + i * 56, 56);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(record[48..]);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(record[52..]);
            if ((long)offset + length > data.Length) throw new InvalidDataException($"Dialogue range {i} exceeds text data.");
            entries[i] = new DialogueEntry(RawBytes.NullTerminated(record[..48]).ToArray(), offset, length);
        }
        return new DialogueCatalog(data, entries);
    }

    public bool TryGetText(ReadOnlySpan<byte> rawKey, out ReadOnlyMemory<byte> text)
    {
        rawKey = RawBytes.NullTerminated(rawKey);
        if (buckets.TryGetValue(Hash(rawKey), out List<int>? bucket))
            foreach (int i in bucket)
                if (entries[i].RawKey.Span.SequenceEqual(rawKey)) { text = GetText(i); return true; }
        text = default;
        return false;
    }

    public ReadOnlyMemory<byte> GetText(int index)
    {
        DialogueEntry entry = entries[index];
        return data.Slice((int)entry.Offset, (int)entry.Length);
    }

    private static ulong Hash(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 14695981039346656037;
        foreach (byte b in bytes) hash = unchecked((hash ^ b) * 1099511628211);
        return hash;
    }
}
