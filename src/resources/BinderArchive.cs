using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Atria.Resources;

public sealed record BinderEntry(int Index, ReadOnlyMemory<byte> RawName, uint Offset, uint Length)
{
    public string Name => Encoding.ASCII.GetString(RawBytes.NullTerminated(RawName.Span));
}

/// <summary>Read-only random access to the explicit Binder ranges. Overlap and unindexed tails are legal.</summary>
public sealed class BinderArchive : IDisposable
{
    private readonly SafeFileHandle handle;
    private readonly BinderEntry[] entries;
    public IReadOnlyList<BinderEntry> Entries { get; }
    public long Length { get; }

    private BinderArchive(SafeFileHandle handle, long length, BinderEntry[] entries)
    {
        this.handle = handle;
        this.entries = entries;
        Length = length;
        Entries = Array.AsReadOnly(entries);
    }

    public static BinderArchive Open(string path)
    {
        SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        try
        {
            long length = RandomAccess.GetLength(handle);
            Span<byte> header = stackalloc byte[32];
            ReadExactly(handle, header, 0);
            if (!header[..28].SequenceEqual("File Binder JAMIE SYSTEM.\0\0\x1a"u8))
                throw new InvalidDataException("Unrecognized Binder signature.");
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);
            long indexEnd = 32L + count * 16L;
            if (indexEnd > length || count > int.MaxValue / 16)
                throw new InvalidDataException("Binder index exceeds the archive.");
            byte[] index = new byte[checked((int)count * 16)];
            ReadExactly(handle, index, 32);
            BinderEntry[] entries = new BinderEntry[(int)count];
            for (int i = 0; i < entries.Length; i++)
            {
                ReadOnlySpan<byte> record = index.AsSpan(i * 16, 16);
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
                if (offset < indexEnd || (long)offset + size > length)
                    throw new InvalidDataException($"Binder member {i} exceeds the payload area.");
                entries[i] = new BinderEntry(i, record[..8].ToArray(), offset, size);
            }
            return new BinderArchive(handle, length, entries);
        }
        catch { handle.Dispose(); throw; }
    }

    public int FindExact(ReadOnlySpan<byte> key)
    {
        key = RawBytes.NullTerminated(key);
        for (int i = 0; i < entries.Length; i++)
            if (RawBytes.NullTerminated(entries[i].RawName.Span).SequenceEqual(key)) return i;
        return -1;
    }

    // 0x436390/0x43ccb0 remove the first dot suffix; 0x43da60 compares at most eight bytes,
    // case insensitive in the C locale (0x447a17/0x447ac0). No filesystem basename inference.
    public int FindResource(ReadOnlySpan<byte> filename)
    {
        filename = RawBytes.NullTerminated(filename);
        int dot = filename.IndexOf((byte)'.');
        if (dot >= 0) filename = filename[..dot];
        if (filename.Length > 8) filename = filename[..8];
        Span<byte> key = stackalloc byte[8];
        key.Clear();
        for (int i = 0; i < filename.Length; i++)
        {
            if (filename[i] >= 128) throw new NotSupportedException("Non-ASCII Binder filename case folding is not recovered.");
            key[i] = FoldAscii(filename[i]);
        }
        for (int i = 0; i < entries.Length; i++)
        {
            ReadOnlySpan<byte> candidate = entries[i].RawName.Span;
            bool equal = true;
            for (int j = 0; j < 8; j++)
            {
                if (FoldAscii(candidate[j]) != key[j]) { equal = false; break; }
                if (key[j] == 0) break;
            }
            if (equal) return i;
        }
        return -1;
    }

    public byte[] ReadMember(int index)
    {
        BinderEntry entry = entries[index];
        if (entry.Length > int.MaxValue) throw new InvalidDataException("Member exceeds supported buffer size.");
        byte[] bytes = new byte[(int)entry.Length];
        ReadExactly(handle, bytes, entry.Offset);
        return bytes;
    }

    public void ReadMemberRange(int index, int memberOffset, Span<byte> destination)
    {
        BinderEntry entry = entries[index];
        if (memberOffset < 0 || (long)memberOffset + destination.Length > entry.Length)
            throw new ArgumentOutOfRangeException(nameof(memberOffset));
        ReadExactly(handle, destination, (long)entry.Offset + memberOffset);
    }

    private static byte FoldAscii(byte b) => b >= (byte)'a' && b <= (byte)'z' ? (byte)(b - 32) : b;
    private static void ReadExactly(SafeFileHandle handle, Span<byte> destination, long offset)
    {
        while (!destination.IsEmpty)
        {
            int read = RandomAccess.Read(handle, destination, offset);
            if (read == 0) throw new EndOfStreamException("Truncated Binder archive.");
            destination = destination[read..];
            offset += read;
        }
    }
    public void Dispose() => handle.Dispose();
}

internal static class RawBytes
{
    public static ReadOnlySpan<byte> NullTerminated(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        return end < 0 ? field : field[..end];
    }
}
