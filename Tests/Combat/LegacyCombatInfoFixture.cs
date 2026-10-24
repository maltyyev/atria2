using System.Buffers.Binary;
using Atria.Game.Combat;

namespace Atria.Tests.Combat;

/// <summary>INF token and original layout boundary. Packed offsets never escape into action consumers.</summary>
internal static class LegacyCombatInfoFixture
{
    public const int ImageSize = 0xb38;
    private const int MotionStride = 24;
    private const int AttackStart = 0x150;
    private const int AttackStride = 44;
    private const int JumpStart = 7 * MotionStride;
    private const int ReactionStart = 0x678;
    private const int ReactionStride = 16;
    private const int CommandStart = 0x7d8;
    private const int CommandStride = 24;
    private const int SoundSlots = 9;
    private const int CommandCategories = 10;

    public static CombatInfoCharacter DecodeImage(ReadOnlyMemory<byte> animation, byte[] image, int ordinal = 0)
    {
        if (image.Length != ImageSize)
        {
            throw new InvalidDataException("Incomplete INF image.");
        }

        int Read(int at) => BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at));
        int Count(int at, int capacity)
        {
            int count = Read(at);
            if ((uint)count > (uint)capacity)
            {
                throw new InvalidDataException("INF count exceeds its record storage.");
            }
            return count;
        }

        int basics = Count(0xb10, 29), firstVariation = Count(0xb1c, 29), endVariation = Count(0xb20, 29);
        if (firstVariation > endVariation)
        {
            throw new InvalidDataException("INF variation range is reversed.");
        }
        var motions = Enumerable.Range(0, Count(0xb18, 6)).Select(i =>
        {
            int at = i * MotionStride;
            return new CombatMotion(Read(at), Read(at + 4), Read(at + 8), Read(at + 12), Read(at + 16));
        }).ToArray();
        var jumps = Enumerable.Range(0, Count(0xb14, 6)).Select(i =>
        {
            int at = JumpStart + i * MotionStride;
            return new CombatJump(Read(at), Read(at + 4), Read(at + 8), Read(at + 12), Read(at + 16));
        }).ToArray();
        var attacks = Enumerable.Range(0, Math.Max(basics, endVariation)).Select(i =>
        {
            int at = AttackStart + i * AttackStride;
            return new CombatAttack(Read(at), Read(at + 4), Read(at + 8), Read(at + 12), Read(at + 16), Read(at + 24));
        }).ToArray();
        var reactions = Enumerable.Range(0, Count(0xb24, 21)).Select(i =>
        {
            int at = ReactionStart + i * ReactionStride;
            return new CombatReaction(Read(at), Read(at + 4), Read(at + 8));
        }).ToArray();
        var commands = Enumerable.Range(0, Count(0xb28, 29)).Select(i =>
        {
            int at = CommandStart + i * CommandStride;
            return new CombatCommand(Read(at), Read(at + 4), Read(at + 8), Read(at + 12), Read(at + 16));
        }).ToArray();
        return new CombatInfoCharacter
        {
            AnimationResource = animation,
            AnimationOrdinal = ordinal,
            ReactionStartFrame = Read(0xaf8),
            Type = Read(0xafc),
            HeaderParameter = Read(0xb00),
            GuardFailureThreshold = Read(0xb04),
            AttackDelay = Read(0xb08),
            TrailingHeaderParameter = Read(0xb0c),
            Motions = Array.AsReadOnly(motions),
            Jumps = Array.AsReadOnly(jumps),
            Attacks = Array.AsReadOnly(attacks),
            Reactions = Array.AsReadOnly(reactions),
            Commands = Array.AsReadOnly(commands),
            Sounds = Array.AsReadOnly(Enumerable.Range(0, SoundSlots).Select(i => Read(0xad0 + i * sizeof(int))).ToArray()),
            CommandCounts = Array.AsReadOnly(Enumerable.Range(0, CommandCategories).Select(i => Read(0xaa8 + i * sizeof(int))).ToArray()),
            BasicAttackCount = basics,
            FirstVariation = firstVariation,
            VariationEnd = endVariation,
            DeclaredSoundCount = Count(0xb2c, SoundSlots)
        };
    }

    //4126e0 tokens are byte tags followed by binary LE fields, not scenario statements.
    public static CombatInfoDefinition Parse(ReadOnlyMemory<byte> source, Action<int>? loadNumericSound = null)
    {
        int position = 0;
        byte[]? current = null;
        ReadOnlyMemory<byte> animation = default;
        var characters = new List<CombatInfoCharacter>();
        var images = new List<(ReadOnlyMemory<byte> Animation, byte[] Data, int Ordinal)>();
        var animations = new List<ReadOnlyMemory<byte>>();
        ReadOnlyMemory<byte> Take(int count)
        {
            if (count < 0 || count > source.Length - position)
            {
                throw new InvalidDataException("Truncated combat INF token.");
            }

            var value = source.Slice(position, count);
            position += count;
            return value;
        }

        int Integer() => BinaryPrimitives.ReadInt32LittleEndian(Take(4).Span);
        int Read(int offset) => BinaryPrimitives.ReadInt32LittleEndian(current!.AsSpan(offset, 4));
        void Write(int offset, int value)
        {
            if (current is null || (uint)offset > ImageSize - 4)
            {
                throw new InvalidDataException("Combat INF writes outside a character record.");
            }

            BinaryPrimitives.WriteInt32LittleEndian(current.AsSpan(offset, 4), value);
        }

        int Append(int counter, int limit)
        {
            int index = Read(counter);
            if ((uint)index >= (uint)limit)
            {
                throw new InvalidDataException("Combat INF record count exceeds native storage.");
            }

            Write(counter, index + 1);
            return index;
        }

        void Fields(int offset, params int[] offsets)
        {
            foreach (int relative in offsets)
            {
                Write(offset + relative, Integer());
            }
        }

        while (true)
        {
            byte tag = Take(1).Span[0];
            if (tag == (byte)'E')
            {
                foreach (var image in images)
                {
                    characters.Add(DecodeImage(image.Animation, image.Data, image.Ordinal));
                }
                return new CombatInfoDefinition(characters, animations, source[position..]);
            }

            if (tag == (byte)'R')
            {
                var raw = Take(13);
                int nul = raw.Span.IndexOf((byte)0);
                if (nul < 0)
                {
                    throw new InvalidDataException("Combat INF animation name lacks its terminator.");
                }

                animation = raw[..nul];
                animations.Add(animation);
                continue;
            }

            if (tag == (byte)'T')
            {
                if (animation.IsEmpty)
                {
                    throw new InvalidDataException("Combat INF character has no preceding animation resource.");
                }

                if (images.Count >= 20)
                {
                    throw new InvalidDataException("Combat INF exceeds native character record storage.");
                }

                current = new byte[ImageSize];
                Fields(0xaf8, 0, 4, 8, 12, 16, 20);
                // b30/b34 are runtime animation index/pointer and are deliberately unbound here.
                images.Add((animation, current, animations.Count - 1));
                continue;
            }

            if (current is null)
            {
                throw new InvalidDataException("Combat INF command precedes its character declaration.");
            }

            int start;
            switch ((char)tag)
            {
                case 'M':
                case 'S':
                    start = Append(0xb18, 6) * 0x18;
                    Fields(start, 0, 4, 8, 12, 16);
                    break;
                case 'A':
                    start = 0x150 + Append(0xb10, 29) * 0x2c;
                    Fields(start, 0, 4, 8, 12, 24);
                    break;
                case 'J':
                    start = (Append(0xb14, 6) + 7) * 0x18;
                    Fields(start, 0, 4, 8, 12, 16);
                    break;
                case 'D':
                    start = 0x678 + Append(0xb24, 21) * 0x10;
                    Fields(start, 0, 4, 8);
                    break;
                case 'P':
                    start = 0x7d8 + Append(0xb28, 29) * 0x18;
                    Fields(start, 0, 8, 12, 4, 16);
                    int kind = Read(start + 12);
                    if ((uint)kind >= 10)
                    {
                        throw new InvalidDataException("Combat INF magic index exceeds native count bank.");
                    }

                    Write(0xaa8 + kind * 4, unchecked(Read(0xaa8 + kind * 4) + 1));
                    break;
                case 'V':
                    if (Read(0xb1c) == 0)
                    {
                        Write(0xb1c, Read(0xb10));
                        Write(0xb20, Read(0xb10));
                    }

                    start = 0x150 + Append(0xb20, 29) * 0x2c;
                    Fields(start, 0, 4, 8, 16, 24);
                    Write(start + 16, unchecked(Read(start + 16) - 0x30));
                    break;
                case 'X':
                    int count = Integer();
                    if ((uint)count > 9)
                    {
                        throw new InvalidDataException("Combat INF sound bank exceeds native nine slots.");
                    }

                    Write(0xb2c, count);
                    for (int i = 0; i < count; i++)
                    {
                        int id = Integer();
                        Write(0xad0 + i * 4, id);
                        if (id != 0)
                        {
                            loadNumericSound?.Invoke(id);
                        }
                    }

                    break;
                default:
                    throw new InvalidDataException("Unknown combat INF token.");
            }
        }
    }

}
