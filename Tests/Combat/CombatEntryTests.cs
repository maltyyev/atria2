using System.Buffers.Binary;
using Atria.Audio;
using Atria.Game.Combat;
using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Tests.Combat;

public static class CombatEntryTests
{
    public static string Run(string root)
    {
        var voices = new List<Voice>();
        var random = new LegacyRandom(99);
        int reseeds = 0, exclusiveSounds = 0;
        IBufferedSoundEffect Load(ReadOnlyMemory<byte> bytes)
        {
            Check(!bytes.IsEmpty, "Actual WAV payload required.");
            var voice = new Voice();
            voices.Add(voice);
            return voice;
        }

        uint Seed()
        {
            Check(voices.Count == 31, "Original0..49 audio preload precedes reseed.");
            reseeds++;
            return 123;
        }

        var battle = new CombatEntryPreparation(root, new(FixedEncounterKind.Scripted, 2), random, Seed, 1000, Load, bytes =>
        {
            Check(PcmWave.Parse(bytes).Samples.Length > 0, "Exclusive playback requires decoded PCM, not a filename.");
            exclusiveSounds++;
        });
        Check(exclusiveSounds == 0, "Entry cannot play an exclusive sound without input.");
        Check(reseeds == 1 && random.State == 123 && random.Calls == 0 && battle.MusicTrack == 24, "Scripted entry reseeds shared RNG without consuming a music roll.");
        Check(voices.Count == 35 && battle.Runtime.InfoBindings.Count == 2 && battle.Assets.BackgroundWidth == 830, "Actual duplicate INF binding loads four additional numeric banks only once.");
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var state = InitialGameState.Create(items, LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat"))));
        var selection = new CombatPartySelection(state, 1);
        try
        {
            battle.BindSelectedParty(selection, 0, 1000);
            throw new Exception("Unfinished selection accepted.");
        }
        catch (InvalidOperationException)
        {
        }

        selection.ApplyInput((_, input) => input is CombatSelectionInput.Toggle or CombatSelectionInput.Finish, _ =>
        {
        });
        selection.Commit(state);
        battle.BindSelectedParty(selection, 0, 1000);
        Check(battle.Runtime.Actors[battle.Runtime.Scene.PartyStartIndex].ControlPlayer == 0, "Actual completed selection binds the original party leader.");
        var bindings = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG")));
        var execution = new CombatExecutionRuntime(
            root,
            battle,
            state,
            random,
            bindings,
            new(_ => false),
            () => -10000,
            (_, _) => throw new Exception("Unexpected rank notice in idle fixture."),
            (_, _) => throw new Exception("Unexpected consumed item in idle fixture.")
        );
        CombatFrameResult first = execution.AdvanceActors(1010, false);
        Check(first.ReturnCode == 0, "Actual intro scene has living and pending enemies.");
        execution.AdvanceHits(1010);
        execution.AdvanceLogicalVisual(1010);
        Check(execution.HasCachedFrame && execution.CachedFrame.Span.ContainsAnyExcept((ushort)0), "Actual actor/particle/HUD pipeline produces a complete nonempty frame.");
        byte[] snapshot = battle.Runtime.Actors.SelectMany(actor => actor.State.ToArray()).ToArray();
        uint rngState = random.State;
        ulong rngCalls = random.Calls;
        ushort[] duplicate = new ushort[640 * 480];
        for (int i = 0; i < 100; i++)
        {
            execution.CopyCachedFrame(duplicate);
        }

        Check(duplicate.AsSpan().SequenceEqual(execution.CachedFrame.Span)
&& snapshot.AsSpan().SequenceEqual(battle.Runtime.Actors.SelectMany(actor => actor.State.ToArray()).ToArray()) && random.State == rngState && random.Calls == rngCalls, "Repeated cached presentations cannot consume RNG, advance animation or change combat actors.");
        CombatActorState leader = battle.Runtime.Actors[battle.Runtime.Scene.PartyStartIndex].State;
        LegacyActorFixture.WriteInt32LittleEndian(leader.AsSpan(0x23c, 4), 2);
        execution.AdvanceLogicalVisual(1010, false);
        Check(LegacyActorFixture.ReadInt32LittleEndian(leader.AsSpan(0x23c, 4)) == 2, "Battle XP backdrop preserves actor visual counters through native40a820(0).");
        execution.AdvanceLogicalVisual(1010);
        Check(LegacyActorFixture.ReadInt32LittleEndian(leader.AsSpan(0x23c, 4)) == 1, "Normal combat visuals resume their original per-draw counters.");
        execution.Frames.Shake.ApplyFrame(0x400);
        execution.ApplyFrameEffect(battle.Runtime.Scene.PartyStartIndex, 106, 10, 1011);
        Check(exclusiveSounds > 0, "Actual magic106 projectiles route their WAV requests to the exclusive bank.");
        Check(execution.GlobalEffectBusy && execution.Magic.GlobalDepth == 330, "Actual magic routes through the shared pool and scene depth, without a second effect owner.");
        execution.AdvanceLogicalVisual(1011);
        Check(execution.Frames.Shake.Phase == 1, "Actors and particles share one logical background shake stage.");
        foreach (int spell in new[] { 130, 156 })
        {
            int soundCount = exclusiveSounds;
            int defense = leader.DefenseBonus;
            int particleSlot = execution.Particles.NextSlot;
            execution.ApplyFrameEffect(battle.Runtime.Scene.PartyStartIndex, spell, 10, 1012);
            Check(execution.GlobalEffectBusy && exclusiveSounds == soundCount && leader.DefenseBonus > defense
                && execution.Particles.Particles[particleSlot].Assets == execution.Magic.ActiveMagic
                && execution.Particles.Particles[particleSlot].Facing != 0,
                "Spell particles remain active when their original optional WAV is absent: " + spell);
            execution.AdvanceLogicalVisual(1012);
        }
        battle.Dispose();
        battle.Dispose();
        Check(voices.All(v => v.Stops == 1 && v.Disposals == 1), "Owned numeric voices stop/dispose once after complete entry.");
        voices.Clear();
        try
        {
            using var missing = new CombatEntryPreparation(root, new(FixedEncounterKind.Scripted, 99999), random, () => 123, 1000, Load, _ =>
            {
            });
            throw new Exception("Missing scene unexpectedly loaded.");
        }
        catch (FileNotFoundException)
        {
        }

        Check(voices.Count == 31 && voices.All(v => v.Stops == 1 && v.Disposals == 1), "A missing actual scene releases every voice already acquired during entry.");
        return "Combat entry: original preload/reseed/INF order, actual selection binding and failure cleanup passed.";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private sealed class Voice : IBufferedSoundEffect
    {
        public int Stops, Disposals;
        public void SetCurrentVolume(int value)
        {
        }

        public void Play()
        {
        }

        public void StopAll() => Stops++;
        public void Dispose() => Disposals++;
    }
}
