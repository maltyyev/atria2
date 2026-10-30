using System.Buffers.Binary;
using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Exploration;

public static class ExplorationTests
{
    public static string Run(string root)
    {
        WorldRenderTests.Run(root);
        static void Check(bool ok, string text)
        {
            if (!ok)
            {
                throw new InvalidOperationException(text);
            }
        }

        var rng = new LegacyRandom(1);
        int[] expected = [41, 18467, 6334, 26500, 19169, 15724, 11478, 29358, 26962, 24464];
        foreach (int value in expected)
        {
            Check(rng.Next() == value, "Original CRT seed1 known output vector.");
        }

        Check(rng.Calls == 10, "Shared RNG call counter.");
        var state = new GameState
        {
            Year = 1450,
            Month = 12,
            Day = 30,
            Money = 6,
            DayCounterStorage = unchecked((int)0xaabbccff)
        };
        ExplorationRules.AdvanceTravelDay(state);
        Check(state.Year == 1451 && state.Month == 1 && state.Day == 1 && state.Money == 1 && state.DayCounterStorage == unchecked((int)0xaabbcc00), "Calendar year boundary, five gold per completed step, byte-only day storage.");
        state.Day = 31;
        state.Money = 5;
        ExplorationRules.AdvanceTravelDay(state);
        Check(state.Day == 2 && state.Month == 2 && state.Money == 0, "Native day32 rollover and exact five-gold balance is spent.");
        foreach (int gold in new[] { 0, 1, 4, 5, 6, int.MaxValue })
        {
            state.Money = gold;
            ExplorationRules.AdvanceTravelDay(state);
            Check(state.Money == Math.Max(0, gold - 5), "Travel consumes up to five gold without debt or overflow.");
        }
        byte[] tile = new byte[328];
        int[] statuses = new int[200];
        Put(tile, 120, 63);
        Put(tile, 124, 1);
        statuses[63] = 1;
        Check(ExplorationRules.MatchingEvent(tile, statuses) == 0, "Matching first event0 suppresses second match.");
        statuses[0] = 4;
        Check(ExplorationRules.MatchingEvent(tile, statuses) == 63, "Ordered second event match.");
        Put(tile, 112, 161);
        Put(tile, 116, 1);
        statuses[161] = 1;
        Check(ExplorationRules.MatchingEvent(tile, statuses) == 161, "Quest161 is emitted as actual event, not fabricated outcome.");
        using var archive = BinderArchive.Open(Path.Combine(root, "AT2AMF.000"));
        byte[] raw = archive.ReadMember(archive.FindResource("ground1.amf"u8));
        raw.AsSpan(313, 32800).Clear();
        for (int i = 0; i < 100; i++)
        {
            raw[313 + i * 328] = 1;
            Put(raw, 313 + i * 328 + 4, 1);
        }

        WorldMapContext Map() => WorldMapContext.Parse(raw);
        var map = Map();
        ExplorationRules.MarkArrival(map, 0, 1);
        var tiles = map.GetTiles();
        Check(tiles[0 * 328 + 1] == 0 && tiles[9 * 328 + 1] == 1 && tiles[11 * 328 + 1] == 1, "Native flattened neighbors exclude0 and retain row-edge wrap.");
        raw[313 + 9 * 328] = 0;
        map = Map();
        Check(ExplorationRules.CanReveal(map, 0, 1, 9, 0), "Reveal uses flattened adjacency including native row wrap.");
        state = new GameState
        {
            WorldX = 0,
            WorldY = 1,
            ResourceCounterDc88 = 1
        };
        Check(ExplorationRules.Reveal(map, state, 9, 0) && state.ResourceCounterDc88 == 0, "One resource consumed on reveal.");
        // Dedicated cleared movement fixture, independent ANI offsets and timer boundary.
        raw[313 + 9 * 328] = 1;
        map = Map();
        state = new GameState
        {
            WorldX = 4,
            WorldY = 4,
            Year = 1450,
            Month = 3,
            Day = 14,
            Money = 200
        };
        var controller = new ExplorationController(state, map, AnimationFixture(), new(1), _ => false);
        var idleMotion = controller.CaptureRuntime();
        controller.DelayMovement(1000);
        Check(controller.CaptureRuntime() == idleMotion, "Pausing idle world animation leaves its pass counter unchanged.");
        Check(controller.BeginTravel(WorldDirection.Left) == TravelStartResult.Started, "Explored passable destination starts travel.");
        Check(state.Money == 200, "Starting a movement does not charge before arrival.");
        var retainedMotion = controller.CaptureRuntime();
        controller.DelayMovement(1000);
        Check(controller.CaptureRuntime() == (retainedMotion with { MovementDeadline = retainedMotion.MovementDeadline + 1000 }),
            "UI pause shifts only the world movement deadline.");
        Check(controller.AdvanceAnimation(() => 1000).Pose.Frame == 0, "UI elapsed time cannot consume a movement frame.");
        controller.RestoreRuntime(retainedMotion);
        controller.ClearMovementAfterBattle();
        Check(controller.CaptureRuntime() == (retainedMotion with
        {
            Moving = false
        }), "Battle return clears only46027c, retaining movement frame, pixels, clocks, direction and travel state.");
        controller.RestoreRuntime(retainedMotion);
        uint time = 0;
        Check(controller.AdvanceAnimation(() => time).Pose.Frame == 0, "Movement deadline equality does not advance.");
        time = 1;
        controller.AdvanceAnimation(() => time);
        time = 31;
        Check(controller.AdvanceAnimation(() => time).Pose.Frame == 1, "Strict30ms deadline retains frame at equality.");
        int sound = 0;
        bool arrived = false;
        for (int i = 0; i < 30 && !arrived; i++)
        {
            time += 31;
            var step = controller.AdvanceAnimation(() => time);
            if (step.PlayWalkSound)
            {
                sound++;
            }

            arrived = step.Arrived;
        }

        Check(arrived && state.WorldX == 3 && state.WorldY == 4 && state.Day == 15 && state.Money == 195 && sound == 1, "Animation displacement completes exactly one paid travel step/day and cycle sound.");
        for (int i = 0; i < 20; i++)
        {
            time += 31;
            controller.AdvanceAnimation(() => time);
        }
        state.WorldX = 0;
        Check(controller.BeginTravel(WorldDirection.Left) == TravelStartResult.Blocked && state.Money == 195, "Idle frames and blocked travel do not charge again.");
        // Event99 precedes event-bank routing and random encounter consumption.
        int at = 313 + (4 * 10 + 4) * 328;
        Put(raw, at + 88, 99);
        "special.scr\0"u8.CopyTo(raw.AsSpan(at + 148, 20));
        state = new GameState
        {
            WorldX = 4,
            WorldY = 4
        };
        rng = new(1);
        var arrival = new ExplorationArrival(state, Map(), rng, 6);
        var effect = arrival.CompleteStep(_ => false);
        Check(effect?.Kind == ExplorationEffectKind.RunCurrentScriptFunction && effect.Function.Span.SequenceEqual("special.scr"u8) && rng.Calls == 0
&& state.ScriptActive == 1, "Tile99 script precedes encounter roll.");
        Put(raw, at + 88, 0);
        raw.AsSpan(at + 148, 20).Clear();
        state = new GameState
        {
            WorldX = 4,
            WorldY = 4
        };
        rng = new(1);
        arrival = new(state, Map(), rng, 6);
        effect = arrival.CompleteStep(_ => false);
        Check(effect?.Kind == ExplorationEffectKind.BattleQueued && rng.Calls == 1 && state.Globals[70] == 2 && state.Globals[72] == 0
&& state.Globals[73] == 1 && arrival.TravelSteps == 0, "Seventh-step odd roll queues bounded terrain-category battle.");
        state = new GameState
        {
            WorldX = 4,
            WorldY = 4
        };
        state.Globals[73] = 11;
        rng = new(1);
        arrival = new(state, Map(), rng, 6);
        Check(arrival.CompleteStep(_ => false) == null && rng.Calls == 1 && state.Globals[73] == 11, "Native encounter quota consumes roll but queues no battle.");
        return "Exploration: CRT vector, byte-day/calendar, ordered events, flattened reveal/fog, animation deadline/displacement and encounter-order/quota fixtures passed; original runtime parity pending.";
    }

    private static void Put(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    private static AnimationFile AnimationFixture()
    {
        var sequences = new AnimationSequence[8];
        for (int s = 0; s < 8; s++)
        {
            var frames = new AnimationFrame[s < 4 ? 2 : 10];
            for (int f = 0; f < frames.Length; f++)
            {
                var words = new ushort[28];
                if (s >= 4)
                {
                    int d = s - 4;
                    words[1] = unchecked((ushort)(d == 0 ? -7 * f : d == 1 ? 7 * f : 0));
                    words[2] = unchecked((ushort)(d == 2 ? -6 * f : d == 3 ? 6 * f : 0));
                }

                frames[f] = new(words, 0, unchecked((short)words[1]), unchecked((short)words[2]));
            }

            sequences[s] = new(0, frames);
        }

        return new([], sequences);
    }
}
