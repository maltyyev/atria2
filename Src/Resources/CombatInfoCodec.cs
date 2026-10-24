using System.Buffers.Binary;
using Atria.Game.Combat;

namespace Atria.Resources;

/// <summary>Decodes original INF records directly to actions; no emulated native structure is allocated.</summary>
public static class CombatInfoCodec
{
    private const int AnimationNameBytes = 13;
    private const int MaximumCharacters = 20;
    private const int MaximumMotions = 6;
    private const int MaximumAttacks = 29;
    private const int MaximumReactions = 21;
    private const int SoundSlots = 9;
    private const int CommandCategories = 10;

    public static CombatInfoDefinition Parse(ReadOnlyMemory<byte> source, Action<int>? loadNumericSound = null)
    {
        int position = 0;
        var characters = new List<CombatInfoCharacter>();
        var animations = new List<ReadOnlyMemory<byte>>();
        ReadOnlyMemory<byte> animation = default;
        CharacterBuilder? current = null;

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

        int Integer() => BinaryPrimitives.ReadInt32LittleEndian(Take(sizeof(int)).Span);

        while (true)
        {
            char tag = (char)Take(1).Span[0];
            switch (tag)
            {
                case 'E':
                    if (current is not null)
                    {
                        characters.Add(current.Build());
                    }

                    return new CombatInfoDefinition(characters, animations, source[position..]);

                case 'R':
                    var raw = Take(AnimationNameBytes);
                    int terminator = raw.Span.IndexOf((byte)0);
                    if (terminator < 0)
                    {
                        throw new InvalidDataException("Combat INF animation name lacks its terminator.");
                    }

                    animation = raw[..terminator];
                    animations.Add(animation);
                    continue;

                case 'T':
                    if (animation.IsEmpty)
                    {
                        throw new InvalidDataException("Combat INF character has no animation resource.");
                    }

                    if (current is not null)
                    {
                        characters.Add(current.Build());
                    }

                    if (characters.Count >= MaximumCharacters)
                    {
                        throw new InvalidDataException("Combat INF has too many characters.");
                    }

                    current = new CharacterBuilder(animation, animations.Count - 1,
                        Integer(), Integer(), Integer(), Integer(), Integer(), Integer());
                    continue;
            }

            if (current is null)
            {
                throw new InvalidDataException("Combat INF command precedes its character declaration.");
            }

            switch (tag)
            {
                case 'M':
                case 'S':
                    Append(current.Motions, MaximumMotions, new CombatMotion(Integer(), Integer(), Integer(), Integer(), Integer()));
                    break;
                case 'J':
                    Append(current.Jumps, MaximumMotions, new CombatJump(Integer(), Integer(), Integer(), Integer(), Integer()));
                    break;
                case 'D':
                    Append(current.Reactions, MaximumReactions, new CombatReaction(Integer(), Integer(), Integer()));
                    break;
                case 'A':
                    current.SetAttack(current.BasicAttackCount++, new CombatAttack(Integer(), Integer(), Integer(), Integer(), 0, Integer()), variation: false);
                    break;
                case 'V':
                    // A and V use a shared action bank; retain first-row precedence and the zero-start sentinel.
                    if (current.FirstVariation == 0)
                    {
                        current.FirstVariation = current.VariationEnd = current.BasicAttackCount;
                    }

                    current.SetAttack(current.VariationEnd++, new CombatAttack(Integer(), Integer(), Integer(), 0, unchecked(Integer() - '0'), Integer()), variation: true);
                    break;
                case 'P':
                    int sequence = Integer(), icon = Integer(), category = Integer(), cost = Integer(), parameter = Integer();
                    if ((uint)category >= CommandCategories)
                    {
                        throw new InvalidDataException("Combat INF command category is outside the category bank.");
                    }

                    Append(current.Commands, MaximumAttacks, new CombatCommand(sequence, cost, icon, category, parameter));
                    current.CommandCounts[category]++;
                    break;
                case 'X':
                    int count = Integer();
                    if ((uint)count > SoundSlots)
                    {
                        throw new InvalidDataException("Combat INF sound list exceeds its bank.");
                    }

                    current.DeclaredSoundCount = count;
                    for (int i = 0; i < count; i++)
                    {
                        int id = Integer();
                        current.Sounds[i] = id;
                        if (id != 0)
                        {
                            loadNumericSound?.Invoke(id);
                        }
                    }
                    break;
                default:
                    throw new InvalidDataException($"Unknown combat INF tag: {tag}.");
            }
        }
    }

    private static void Append<T>(List<T> records, int capacity, T record)
    {
        if (records.Count >= capacity)
        {
            throw new InvalidDataException("Combat INF record count exceeds its format capacity.");
        }

        records.Add(record);
    }

    private sealed class CharacterBuilder(ReadOnlyMemory<byte> animation, int ordinal, int reactionStartFrame,
        int type, int headerParameter, int guardFailureThreshold, int attackDelay, int trailingHeaderParameter)
    {
        public readonly List<CombatMotion> Motions = [];
        public readonly List<CombatJump> Jumps = [];
        public readonly List<CombatReaction> Reactions = [];
        public readonly List<CombatCommand> Commands = [];
        private readonly List<CombatAttack> attacks = [];
        public readonly int[] Sounds = new int[SoundSlots];
        public readonly int[] CommandCounts = new int[CommandCategories];
        public int BasicAttackCount, FirstVariation, VariationEnd, DeclaredSoundCount;

        public void SetAttack(int index, CombatAttack attack, bool variation)
        {
            if ((uint)index >= MaximumAttacks)
            {
                throw new InvalidDataException("Combat INF attack count exceeds its action bank.");
            }

            if (index == attacks.Count)
            {
                attacks.Add(attack);
            }
            else
            {
                // Repeated/interleaved A/V tags overwrite only the fields present in that tag.
                var previous = attacks[index];
                attacks[index] = variation
                    ? attack with
                    {
                        ChainWindow = previous.ChainWindow
                    }
                    : attack with
                    {
                        Direction = previous.Direction
                    };
            }
        }

        public CombatInfoCharacter Build() => new()
        {
            AnimationResource = animation,
            AnimationOrdinal = ordinal,
            ReactionStartFrame = reactionStartFrame,
            Type = type,
            HeaderParameter = headerParameter,
            GuardFailureThreshold = guardFailureThreshold,
            AttackDelay = attackDelay,
            TrailingHeaderParameter = trailingHeaderParameter,
            Motions = Motions.AsReadOnly(),
            Jumps = Jumps.AsReadOnly(),
            Attacks = attacks.AsReadOnly(),
            Reactions = Reactions.AsReadOnly(),
            Commands = Commands.AsReadOnly(),
            Sounds = Array.AsReadOnly(Sounds),
            CommandCounts = Array.AsReadOnly(CommandCounts),
            BasicAttackCount = BasicAttackCount,
            FirstVariation = FirstVariation,
            VariationEnd = VariationEnd,
            DeclaredSoundCount = DeclaredSoundCount
        };
    }
}
