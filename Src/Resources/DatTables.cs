using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Atria.Resources;

/// <summary>Known fixed-record DAT layouts. Preserve trailing localization padding and unidentified bytes.</summary>
public sealed class ItemCatalog
{
    public const int Count = 88;
    public const int RecordSize = 98;
    public const int DescriptionOffset = Count * RecordSize;
    private readonly ReadOnlyMemory<byte> source;
    public ReadOnlyMemory<byte> TrailingBytes => source[(DescriptionOffset + Count * 310)..];

    private ItemCatalog(ReadOnlyMemory<byte> source) => this.source = source;
    public static ItemCatalog Parse(ReadOnlyMemory<byte> source)
    {
        if (source.Length < DescriptionOffset + Count * 310)
        {
            throw new InvalidDataException("ATITEM lacks its88 records/descriptions.");
        }

        return new ItemCatalog(source);
    }

    public ItemRecord GetItem(int index)
    {
        if ((uint)index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new ItemRecord(source.Slice(index * RecordSize, RecordSize));
    }

    public ReadOnlyMemory<byte> GetDescriptionLine(int item, int line)
    {
        if ((uint)item >= Count || (uint)line >= 5)
        {
            throw new ArgumentOutOfRangeException(nameof(item));
        }

        return DatTables.Terminated(source.Slice(DescriptionOffset + item * 310 + line * 62, 62));
    }
}

public readonly struct ItemRecord
{
    public ReadOnlyMemory<byte> Raw
    {
        get;
    }

    internal ItemRecord(ReadOnlyMemory<byte> raw) => Raw = raw;
    public ReadOnlyMemory<byte> Name => DatTables.Terminated(Raw[..30]);
    public int Category => GetInteger(0);
    public int PurchasePrice => GetInteger(4);
    public int Power => GetInteger(5);
    public int Accuracy => GetInteger(6);
    public const int ModifierCapacity = 3;
    public int ModifierCount
    {
        get
        {
            int count = GetInteger(7);
            if (count > ModifierCapacity)
            {
                throw new InvalidDataException("Item modifier count exceeds its slots.");
            }

            return count;
        }
    }
    public ItemModifier GetModifier(int index)
    {
        if ((uint)index >= ModifierCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new((ItemModifierKind)GetInteger(8 + index), GetInteger(11 + index));
    }

    public int GetInteger(int field)
    {
        if ((uint)field >= 17)
        {
            throw new ArgumentOutOfRangeException(nameof(field));
        }

        return BinaryPrimitives.ReadInt32LittleEndian(Raw.Span.Slice(30 + field * 4, 4));
    }
}

public sealed class ShopCatalog
{
    public const int Count = 30;
    public const int RecordSize = 612;
    private readonly ReadOnlyMemory<byte> source;
    private ShopCatalog(ReadOnlyMemory<byte> source) => this.source = source;
    public static ShopCatalog Parse(ReadOnlyMemory<byte> source)
    {
        if (source.Length < Count * RecordSize)
        {
            throw new InvalidDataException("SHOP lacks its30 records.");
        }
        // 0x42f010 loads three count +25id +25quantity groups per shop.
        for (int shop = 0; shop < Count; shop++)
        {
            for (int group = 0; group < 3; group++)
            {
                int count = BinaryPrimitives.ReadInt32LittleEndian(source.Span[(shop * RecordSize + group * 204)..]);
                if ((uint)count > 25)
                {
                    throw new InvalidDataException("Shop count exceeds its25 slots.");
                }
            }
        }

        return new ShopCatalog(source);
    }

    public ShopGroup GetGroup(int shop, int group)
    {
        if ((uint)shop >= Count || (uint)group >= 3)
        {
            throw new ArgumentOutOfRangeException(nameof(shop));
        }

        return new ShopGroup(source.Slice(shop * RecordSize + group * 204, 204));
    }

    public ReadOnlyMemory<byte> TrailingBytes => source[(Count * RecordSize)..];
}

public readonly struct ShopGroup
{
    public ReadOnlyMemory<byte> Raw
    {
        get;
    }

    internal ShopGroup(ReadOnlyMemory<byte> raw) => Raw = raw;
    public int Count => BinaryPrimitives.ReadInt32LittleEndian(Raw.Span);

    public int GetItemId(int index) => ReadSlot(index, 4);
    public int GetQuantity(int index) => ReadSlot(index, 104);
    private int ReadSlot(int index, int offset)
    {
        if ((uint)index >= 25)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return BinaryPrimitives.ReadInt32LittleEndian(Raw.Span.Slice(offset + index * 4, 4));
    }
}

public sealed class QuestJournalCatalog
{
    public const int Count = 150;
    public const int RecordSize = 390;
    private readonly ReadOnlyMemory<byte> source;
    private QuestJournalCatalog(ReadOnlyMemory<byte> source) => this.source = source;
    public static QuestJournalCatalog Parse(ReadOnlyMemory<byte> source)
    {
        if (source.Length < Count * RecordSize)
        {
            throw new InvalidDataException("DANGEL lacks its150 journal records.");
        }

        return new QuestJournalCatalog(source);
    }

    public ReadOnlyMemory<byte> GetHeading(int entry, int variant)
    {
        if ((uint)entry >= Count || (uint)variant >= 2)
        {
            throw new ArgumentOutOfRangeException(nameof(entry));
        }

        return DatTables.Terminated(source.Slice(entry * RecordSize + variant * 40, 40));
    }

    public ReadOnlyMemory<byte> GetDescriptionLine(int entry, int line)
    {
        if ((uint)entry >= Count || (uint)line >= 5)
        {
            throw new ArgumentOutOfRangeException(nameof(entry));
        }

        return DatTables.Terminated(source.Slice(entry * RecordSize + 80 + line * 62, 62));
    }

    public ReadOnlyMemory<byte> TrailingBytes => source[(Count * RecordSize)..];
}

public static class DatTables
{
    // The record counts and byte widths below follow native fread/fseek calls; no file-length division.
    public static ReadOnlyMemory<byte> GetCharacterIntroductionLine(ReadOnlyMemory<byte> source, int character, int line) => FixedLine(source, character, 6, line, 10, 32, 0);
    public static ReadOnlyMemory<byte> GetItemExtraLine(ReadOnlyMemory<byte> source, int item, int line) => FixedLine(source, item, 88, line, 3, 21, 0);
    public static ReadOnlyMemory<byte> GetCreditLine(ReadOnlyMemory<byte> source, int line) => FixedLine(source, 0, 1, line, 400, 80, 100);
    public static ReadOnlyMemory<byte> GetEndingLine(ReadOnlyMemory<byte> source, int endingId, int line)
    {
        if (endingId < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(endingId));
        }

        long offset = 100L + (endingId - 1L) * 16000;
        if (offset + 16000 > source.Length)
        {
            throw new InvalidDataException("Ending text section exceeds source.");
        }

        return FixedLine(source, 0, 1, line, 200, 80, (int)offset);
    }

    public static ReadOnlyMemory<byte> GetEventItemDescriptionLine(ReadOnlyMemory<byte> source, int item, int line)
    {
        if (item < 0 || (uint)line >= 5)
        {
            throw new ArgumentOutOfRangeException(nameof(item));
        }

        long offset = (long)item * 310;
        if (offset + 310 > source.Length)
        {
            throw new InvalidDataException("Event item description exceeds source.");
        }

        return Terminated(source.Slice((int)offset + line * 62, 62));
    }

    internal static ReadOnlyMemory<byte> Terminated(ReadOnlyMemory<byte> bytes) => bytes[..RawBytes.NullTerminated(bytes.Span).Length];
    private static ReadOnlyMemory<byte> FixedLine(ReadOnlyMemory<byte> source, int record, int count, int line, int lines, int width, int offset)
    {
        if ((uint)record >= (uint)count || (uint)line >= (uint)lines)
        {
            throw new ArgumentOutOfRangeException(nameof(record));
        }

        if (source.Length < offset + count * lines * width)
        {
            throw new InvalidDataException("Truncated fixed text table.");
        }

        return Terminated(source.Slice(offset + (record * lines + line) * width, width));
    }
}

/// <summary>Original rt/fgets(80) input behavior, followed by the original string terminator adjustment.</summary>
public static class LegacyTextTables
{
    public static IReadOnlyList<ReadOnlyMemory<byte>> ParseMessages(ReadOnlySpan<byte> source)
    {
        ReadOnlyMemory<byte>[] result = new ReadOnlyMemory<byte>[372];
        int cursor = 0;
        for (int i = 0; i < result.Length; i++)
        {
            byte[] line = ReadFgets80(source, ref cursor);
            int length = RawBytes.NullTerminated(line).Length;
            if (length == 0)
            {
                throw new InvalidDataException("TEXTmsg has an empty/missing native fgets record.");
            }

            result[i] = line.AsMemory(0, length - 1);
        }

        return Array.AsReadOnly(result);
    }

    public static IReadOnlyList<ReadOnlyMemory<byte>> ParseBattle(ReadOnlySpan<byte> source)
    {
        ReadOnlyMemory<byte>[] result = new ReadOnlyMemory<byte>[54];
        ReadOnlySpan<int> counts = [5, 6, 2, 1, 6, 2, 6, 3, 2];
        int cursor = 0;
        for (int row = 0; row < counts.Length; row++)
        {
            for (int column = 0; column < counts[row]; column++)
            {
                byte[] line = ReadFgets80(source, ref cursor);
                int length = RawBytes.NullTerminated(line).Length;
                // 0x4137e0 removes the final character only when strlen >1, even if it is not LF.
                result[row * 6 + column] = line.AsMemory(0, length > 1 ? length - 1 : length);
            }
        }

        return Array.AsReadOnly(result);
    }

    private static byte[] ReadFgets80(ReadOnlySpan<byte> source, ref int cursor)
    {
        Span<byte> line = stackalloc byte[79];
        int length = 0;
        while (length < 79 && cursor < source.Length)
        {
            byte b = source[cursor++];
            if (b == 0x1a)
            {
                cursor = source.Length;
                break;
            } // MSVCRT text-mode EOF.

            if (b == 13 && cursor < source.Length && source[cursor] == 10)
            {
                cursor++;
                b = 10;
            }

            line[length++] = b;
            if (b == 10)
            {
                break;
            }
        }

        if (length == 0)
        {
            throw new InvalidDataException("Text table ended before the required native fgets records.");
        }

        return line[..length].ToArray();
    }
}
