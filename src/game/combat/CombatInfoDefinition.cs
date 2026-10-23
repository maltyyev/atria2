using System.Buffers.Binary;
using Atria.Resources;

namespace Atria.Game.Combat;

/// <summary>Disk INF token reconstruction. Runtime pointers remain unbound until actual assets load.</summary>
public sealed record CombatInfoCharacter(ReadOnlyMemory<byte> AnimationResource, ReadOnlyMemory<byte> Fields)
{
    public int AnimationOrdinal { get; init; }
    public const int RuntimeSize = 0xb38;
    public int Read(int offset)
    {
        if ((uint)offset > RuntimeSize - 4) throw new ArgumentOutOfRangeException(nameof(offset));
        return BinaryPrimitives.ReadInt32LittleEndian(Fields.Span[offset..]);
    }
    public int Type => Read(0xafc);
}

public sealed class CombatInfoDefinition
{
    public IReadOnlyList<CombatInfoCharacter> Characters { get; }
    public IReadOnlyList<ReadOnlyMemory<byte>> AnimationResources { get; }
    public ReadOnlyMemory<byte> TrailingBytes { get; }
    private CombatInfoDefinition(List<CombatInfoCharacter> characters,List<ReadOnlyMemory<byte>> animations, ReadOnlyMemory<byte> trailing)
    { Characters = characters.AsReadOnly(); AnimationResources=animations.AsReadOnly(); TrailingBytes = trailing; }

    //4126e0 tokens are byte tags followed by binary LE fields, not scenario statements.
    public static CombatInfoDefinition Parse(ReadOnlyMemory<byte> source,Action<int>? loadNumericSound=null)
    {
        int position = 0; byte[]? current = null; ReadOnlyMemory<byte> animation = default;
        var characters = new List<CombatInfoCharacter>();
        var animations = new List<ReadOnlyMemory<byte>>();
        ReadOnlyMemory<byte> Take(int count)
        {
            if (count < 0 || count > source.Length - position) throw new InvalidDataException("Truncated combat INF token.");
            var value = source.Slice(position, count); position += count; return value;
        }
        int Integer() => BinaryPrimitives.ReadInt32LittleEndian(Take(4).Span);
        int Read(int offset) => BinaryPrimitives.ReadInt32LittleEndian(current!.AsSpan(offset, 4));
        void Write(int offset, int value)
        {
            if (current is null || (uint)offset > CombatInfoCharacter.RuntimeSize - 4)
                throw new InvalidDataException("Combat INF writes outside a character record.");
            BinaryPrimitives.WriteInt32LittleEndian(current.AsSpan(offset, 4), value);
        }
        int Append(int counter, int limit)
        {
            int index = Read(counter);
            if ((uint)index >= (uint)limit) throw new InvalidDataException("Combat INF record count exceeds native storage.");
            Write(counter, index + 1); return index;
        }
        void Fields(int offset, params int[] offsets)
        { foreach (int relative in offsets) Write(offset + relative, Integer()); }
        while (true)
        {
            byte tag = Take(1).Span[0];
            if (tag == (byte)'E') return new CombatInfoDefinition(characters,animations, source[position..]);
            if (tag == (byte)'R')
            {
                var raw = Take(13); int nul = raw.Span.IndexOf((byte)0);
                if (nul < 0) throw new InvalidDataException("Combat INF animation name lacks its terminator.");
                animation = raw[..nul]; animations.Add(animation); continue;
            }
            if (tag == (byte)'T')
            {
                if (animation.IsEmpty) throw new InvalidDataException("Combat INF character has no preceding animation resource.");
                if (characters.Count >= 20) throw new InvalidDataException("Combat INF exceeds native character record storage.");
                current = new byte[CombatInfoCharacter.RuntimeSize];
                Fields(0xaf8, 0, 4, 8, 12, 16, 20);
                // b30/b34 are runtime animation index/pointer and are deliberately unbound here.
                characters.Add(new CombatInfoCharacter(animation, current){AnimationOrdinal=animations.Count-1}); continue;
            }
            if (current is null) throw new InvalidDataException("Combat INF command precedes its character declaration.");
            int start;
            switch ((char)tag)
            {
                case 'M': case 'S':
                    start = Append(0xb18, 6) * 0x18; Fields(start, 0, 4, 8, 12, 16); break;
                case 'A':
                    start = 0x150 + Append(0xb10, 29) * 0x2c; Fields(start, 0, 4, 8, 12, 24); break;
                case 'J':
                    start = (Append(0xb14, 6) + 7) * 0x18; Fields(start, 0, 4, 8, 12, 16); break;
                case 'D':
                    start = 0x678 + Append(0xb24, 21) * 0x10; Fields(start, 0, 4, 8); break;
                case 'P':
                    start = 0x7d8 + Append(0xb28, 29) * 0x18; Fields(start, 0, 8, 12, 4, 16);
                    int kind = Read(start + 12);
                    if ((uint)kind >= 10) throw new InvalidDataException("Combat INF magic index exceeds native count bank.");
                    Write(0xaa8 + kind * 4, unchecked(Read(0xaa8 + kind * 4) + 1)); break;
                case 'V':
                    if (Read(0xb1c) == 0) { Write(0xb1c, Read(0xb10)); Write(0xb20, Read(0xb10)); }
                    start = 0x150 + Append(0xb20, 29) * 0x2c; Fields(start, 0, 4, 8, 16, 24);
                    Write(start + 16, unchecked(Read(start + 16) - 0x30)); break;
                case 'X':
                    int count = Integer();
                    if ((uint)count > 9) throw new InvalidDataException("Combat INF sound bank exceeds native nine slots.");
                    Write(0xb2c, count);
                    for (int i = 0; i < count; i++)
                    {
                        int id=Integer();Write(0xad0+i*4,id);if(id!=0) loadNumericSound?.Invoke(id);
                    }
                    break;
                default: throw new InvalidDataException("Unknown combat INF token.");
            }
        }
    }

    public static CombatInfoDefinition Load(string resourceRoot, ReadOnlySpan<byte> resource,Action<int>? loadNumericSound=null)
    {
        using var archive = BinderArchive.Open(Path.Combine(resourceRoot, "INF.000"));
        int index = archive.FindResource(resource);
        if (index < 0) throw new FileNotFoundException("Required combat INF resource is missing.");
        return Parse(archive.ReadMember(index),loadNumericSound);
    }
}

