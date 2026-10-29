using System.Text;
using Atria.Game.Combat;
using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Combat;

public static class CombatFrameSoundTests
{
    public static string Run(string resourceRoot)
    {
        CombatAnimationAssets assets = CombatAnimationAssets.Load(resourceRoot, "WARRIOR.ANI"u8);
        AnimationFrame strike = assets.Animation.Sequences[27].Frames[10];
        foreach ((string resource, int expectedSound) in new[] { ("50.inf", 7), ("TIGER4.inf", 4), ("TIGER5.inf", 4) })
        {
            CombatInfoCharacter info = CombatInfoDefinition.Load(resourceRoot, Encoding.ASCII.GetBytes(resource)).Characters[0];
            Check(Play(info, strike).SequenceEqual(new[] { expectedSound }), $"{resource}: defective strike preserves original effective sound.");
            Check(Play(info, assets.Animation.Sequences[27].Frames[6]).SequenceEqual(new[] { 181 }), "Valid character sound lookup remains unchanged.");

            var actor = new CombatActorImage { Binding = new CombatInfoBinding(0, 0, info, assets) };
            actor.Animation = new CombatAnimationCursor(actor.State, info, assets);
            actor.Animation.BeginSequenceCommand(27, 32, 0);
            actor.State.FrameIndex = 10;
            actor.Animation.RebindFromFrameCounter();
            List<int> runtimeSounds = [];
            CombatAnimationRuntime.Advance(actor, new GameState(), 32, 0, () => 0, runtimeSounds.Add,
                _ => { }, (_, _) => { }, _ => { }, () => { }, new CombatCameraShake());
            Check(runtimeSounds.SequenceEqual(new[] { expectedSound }) && actor.State.FrameIndex == 11,
                "Real animation runtime must advance past the formerly crashing frame.");
        }

        using var archive = BinderArchive.Open(Path.Combine(resourceRoot, "ANI.000"));
        int invalidSlots = 0;
        foreach (BinderEntry entry in archive.Entries)
        {
            AnimationFile raw = AnimationFile.Decode(archive.ReadMember(entry.Index));
            foreach (AnimationSequence sequence in raw.Sequences)
            {
                foreach (AnimationFrame frame in sequence.Frames)
                {
                    if (frame.CharacterSoundSlot < 9)
                    {
                        continue;
                    }

                    invalidSlots++;
                    Check(entry.Name == "WARRIOR" && ReferenceEquals(frame, raw.Sequences[27].Frames[10]), "Only the verified WARRIOR frame has an invalid slot in the original archive.");
                    CombatInfoCharacter info = CombatInfoDefinition.Load(resourceRoot, "50.inf"u8).Characters[0];
                    bool rejected = false;
                    try
                    {
                        Play(info, frame);
                    }
                    catch (InvalidDataException)
                    {
                        rejected = true;
                    }
                    Check(rejected, "Uncorrected invalid sound slots must still fail validation.");
                    Check(frame.Words.SequenceEqual(strike.Words) && frame.FinalDword == strike.FinalDword,
                        "Combat correction must not alter animation timing, effects, motion or original words.");
                }
            }
        }
        Check(invalidSlots == 1, "Complete animation corpus contains exactly one known defective sound slot.");
        return "Combat frame sound regression: real WARRIOR strike, three INF variants, valid sound slot, complete ANI corpus and strict invalid-slot rejection passed.";
    }

    private static List<int> Play(CombatInfoCharacter info, AnimationFrame frame)
    {
        List<int> sounds = [];
        CombatAnimationFrameEffects.ApplyEvents(new CombatActorState(), info, frame, 0, () => 0, sounds.Add, _ => { }, (_, _) => { });
        return sounds;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
