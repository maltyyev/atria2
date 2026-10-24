using System.Buffers.Binary;
using Atria.Game.Combat;
using Atria.Resources;

namespace Atria.Tests.Combat;

public static class CombatInfoModelTests
{
    public static string Run(string root)
    {
        using var archive = BinderArchive.Open(Path.Combine(root, "INF.000"));
        int characters = 0;
        foreach (var entry in archive.Entries)
        {
            var source = archive.ReadMember(entry.Index);
            Compare(source);
            characters += CombatInfoDefinition.Parse(source).Characters.Count;
        }

        // Repeated/interleaved tags preserve untouched fields in their shared A/V bank.
        using var fixture = new MemoryStream();
        fixture.WriteByte((byte)'R');
        fixture.Write(new byte[13] { (byte)'A', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        Write('T', 0, 2, 0, 0, 100, 0);
        Write('A', 1, 0, 32, 100, 20);
        Write('V', 2, 32, 64, '6', 30);
        Write('A', 3, 64, 96, 200, 40);
        Write('X', 3, 10, 20, 30);
        Write('X', 1, 40);
        fixture.WriteByte((byte)'E');
        Compare(fixture.ToArray());
        return $"Typed INF parity: {archive.Entries.Count} members / {characters} characters, ordered sounds and overlapping A/V/X records passed.";

        void Write(char tag, params int[] values)
        {
            fixture.WriteByte((byte)tag);
            Span<byte> word = stackalloc byte[sizeof(int)];
            foreach (int value in values)
            {
                BinaryPrimitives.WriteInt32LittleEndian(word, value);
                fixture.Write(word);
            }
        }
    }

    private static void Compare(ReadOnlyMemory<byte> source)
    {
        var oldSounds = new List<int>();
        var newSounds = new List<int>();
        var expected = LegacyCombatInfoFixture.Parse(source, oldSounds.Add);
        var actual = CombatInfoDefinition.Parse(source, newSounds.Add);
        Check(oldSounds.SequenceEqual(newSounds) && expected.Characters.Count == actual.Characters.Count
            && expected.TrailingBytes.Span.SequenceEqual(actual.TrailingBytes.Span), "INF boundaries or sound loading changed.");

        for (int i = 0; i < expected.Characters.Count; i++)
        {
            var left = expected.Characters[i];
            var right = actual.Characters[i];
            Check(left.AnimationResource.Span.SequenceEqual(right.AnimationResource.Span)
                && left.AnimationOrdinal == right.AnimationOrdinal && left.Type == right.Type
                && left.ReactionStartFrame == right.ReactionStartFrame && left.HeaderParameter == right.HeaderParameter
                && left.GuardFailureThreshold == right.GuardFailureThreshold && left.AttackDelay == right.AttackDelay
                && left.TrailingHeaderParameter == right.TrailingHeaderParameter
                && left.BasicAttackCount == right.BasicAttackCount && left.FirstVariation == right.FirstVariation
                && left.VariationEnd == right.VariationEnd && left.DeclaredSoundCount == right.DeclaredSoundCount
                && left.Motions.SequenceEqual(right.Motions) && left.Jumps.SequenceEqual(right.Jumps)
                && left.Attacks.SequenceEqual(right.Attacks) && left.Reactions.SequenceEqual(right.Reactions)
                && left.Commands.SequenceEqual(right.Commands) && left.Sounds.SequenceEqual(right.Sounds)
                && left.CommandCounts.SequenceEqual(right.CommandCounts), $"INF character {i} changed.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
