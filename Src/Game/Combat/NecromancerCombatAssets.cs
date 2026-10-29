using System.Text;
using System.Text.Json;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Combat;

internal static class NecromancerCombatAssets
{
    internal const string AnimationName = "EIREN.ANI";
    internal sealed record Spell(int Sequence, int Effect, int Mana, int Icon, string NameKey);
    private static readonly JsonElement definition = ReadDefinition();
    internal static readonly Spell[] Spells = definition.GetProperty("commands").EnumerateArray().Select(row => new Spell(
        row.GetProperty("sequence").GetInt32(), row.GetProperty("effect").GetInt32(), row.GetProperty("mana").GetInt32(),
        row.GetProperty("icon").GetInt32(), row.GetProperty("key").GetString()!)).ToArray();
    internal static int AirEngagementDistance => definition.GetProperty("airEngagementDistance").GetInt32();
    internal static JsonElement SummonDefinition => definition.GetProperty("summons");
    internal static bool IsHeroResource(ReadOnlySpan<byte> name) => name.Length == 6 && name[..5].SequenceEqual("EIREN"u8) && name[5] is >= (byte)'1' and <= (byte)'5';
    internal static bool IsInfo(CombatInfoCharacter info) => info.AnimationResource.Span.SequenceEqual("EIREN.ANI"u8);

    public static CombatInfoDefinition Info(string root, Action<int>? loadSound)
    {
        var original = CombatInfoDefinition.Load(root, Encoding.ASCII.GetBytes(definition.GetProperty("sourceInfo").GetString()!), loadSound).Characters[^1];
        var template = CombatInfoDefinition.Load(root, Encoding.ASCII.GetBytes(definition.GetProperty("jumpTemplate").GetString()!), loadSound).Characters[^1];
        int melee = definition.GetProperty("meleeSequence").GetInt32();
        var attacks = template.Attacks.Select(attack => attack with { Animation = melee }).ToArray();
        attacks[template.FirstVariation] = attacks[template.FirstVariation] with
        {
            Animation = definition.GetProperty("idleSequence").GetInt32()
        };
        int jumpStart = Spells[^1].Sequence + 1;
        var commands = template.Commands.Where(command => command.Category == 0)
            .Concat(Spells.Select(spell => new CombatCommand(spell.Sequence, spell.Mana, spell.Icon, 1, 0))).ToArray();
        var counts = new int[10];
        foreach (var command in commands)
        {
            counts[command.Category]++;
        }

        var info = original with
        {
            AnimationResource = Encoding.ASCII.GetBytes(AnimationName),
            AnimationOrdinal = 0,
            Type = definition.GetProperty("playerType").GetInt32(),
            Jumps = template.Jumps.Select((jump, index) => jump with { Animation = jumpStart + index }).ToArray(),
            Attacks = attacks,
            BasicAttackCount = template.BasicAttackCount,
            FirstVariation = template.FirstVariation,
            VariationEnd = template.VariationEnd,
            Commands = commands,
            CommandCounts = counts,
            Sounds = template.Sounds
        };
        return new([info], [info.AnimationResource], ReadOnlyMemory<byte>.Empty);
    }

    public static CombatAnimationAssets Animation(string root)
    {
        var source = CombatAnimationAssets.Load(root, Encoding.ASCII.GetBytes(definition.GetProperty("sourceAnimation").GetString()!));
        var sequences = source.Animation.Sequences.Select(Clone).ToList();
        var sprites = source.Sprites.Frames.ToList();
        int melee = definition.GetProperty("meleeSequence").GetInt32();
        if (sequences.Count != melee)
        {
            throw new InvalidDataException("Invalid authored melee sequence index.");
        }

        var meleeFrames = new List<AnimationFrame>();
        var rectangle = definition.GetProperty("meleeRectangle");
        foreach (var pose in definition.GetProperty("meleePoses").EnumerateArray())
        {
            int index = pose.GetInt32();
            var sprite = source.Sprites.Frames[index];
            var rectangles = (SpriteRectangle[])sprite.Rectangles.Clone();
            var frame = Clone(source.Animation.Sequences[definition.GetProperty("castSequence").GetInt32()]).Frames
                .First(candidate => candidate.SpriteFrameIndex == index);
            frame.SpriteFrameIndex = checked((ushort)sprites.Count);
            frame.Particle = frame.Effect = frame.SecondaryEffect = 0;
            if (index == definition.GetProperty("meleeStrikePose").GetInt32())
            {
                rectangles[0] = new(rectangle[0].GetInt16(), rectangle[1].GetInt16(), rectangle[2].GetInt16(), rectangle[3].GetInt16());
                frame.Damage = definition.GetProperty("meleeDamage").GetUInt16();
                frame.HitPriority = definition.GetProperty("meleePriority").GetUInt16();
                frame.Reaction = definition.GetProperty("meleeReaction").GetUInt16();
            }
            sprites.Add(sprite with
            {
                Rectangles = rectangles
            });
            meleeFrames.Add(frame);
        }
        sequences.Add(new(0, meleeFrames.ToArray()));
        foreach (var spell in Spells)
        {
            if (spell.Sequence != sequences.Count)
            {
                throw new InvalidDataException("Unordered necromancer command sequences.");
            }

            var sequence = Clone(source.Animation.Sequences[definition.GetProperty("castSequence").GetInt32()]);
            foreach (var frame in sequence.Frames)
            {
                if (frame.Effect != 0)
                {
                    frame.Effect = checked((ushort)spell.Effect);
                }
            }
            sequences.Add(sequence);
        }
        var template = CombatInfoDefinition.Load(root, Encoding.ASCII.GetBytes(definition.GetProperty("jumpTemplate").GetString()!)).Characters[^1];
        var jumpAssets = CombatAnimationAssets.Load(root, template.AnimationResource.Span);
        foreach (var jump in template.Jumps)
        {
            var sequence = Clone(jumpAssets.Animation.Sequences[jump.Animation]);
            var poses = sequences[0].Frames;
            for (int i = 0; i < sequence.Frames.Length; i++)
            {
                var frame = sequence.Frames[i];
                // Keep tested jump physics, but bind every pose and effect to this hero's own artwork.
                frame.SpriteFrameIndex = poses[i * poses.Length / sequence.Frames.Length].SpriteFrameIndex;
                frame.Effect = frame.Particle = frame.SecondaryEffect = 0;
                if (frame.Damage > 0)
                {
                    frame.Damage = (ushort)definition.GetProperty("meleeDamage").GetInt32();
                }
            }
            sequences.Add(sequence);
        }
        return new(source.Animation with
        {
            Sequences = sequences.ToArray()
        }, source.Sprites with
        {
            Frames = sprites.ToArray()
        });
    }

    private static AnimationSequence Clone(AnimationSequence sequence) => sequence with
    {
        Frames = sequence.Frames.Select(frame => frame with { Words = (ushort[])frame.Words.Clone() }).ToArray()
    };
    private static JsonElement ReadDefinition()
    {
        using var document = GameData.Open("Necromancer");
        return document.RootElement.Clone();
    }
}
