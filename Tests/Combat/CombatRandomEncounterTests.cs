using Atria.Audio;
using Atria.Game.Combat;
using Atria.Game.Exploration;
using Atria.Resources;

namespace Atria.Tests.Combat;

public static class CombatRandomEncounterTests
{
    public static string Run(string resourceRoot)
    {
        void Check(bool value, string message)
        {
            if (!value)
            {
                throw new InvalidOperationException("Random encounter: " + message);
            }
        }

        CombatRandomEncounterResolution Resolve(short region, short category, short field, short forest, short mountain, int incoming, out LegacyRandom rng)
        {
            rng = new(1);
            return CombatRandomEncounterResolution.Resolve(new(region, category, field, forest, mountain, incoming), rng);
        }

        var field = Resolve(6, 0, 11, 6, 5, 999, out var random);
        Check(field.ResourceName == "R7N11" && field.FileName == "R7N11.SOD" && !field.UsedRandomFallback, "field count and zero-based region");
        Check(random.Calls == 1 && random.State == 2745024, "normal branch consumes CRT seed1 first draw even though unused");
        Check(Resolve(0, 1, 11, 6, 5, 999, out _).SceneNumber == 6, "forest uses global74 without field count");
        Check(Resolve(0, 2, 11, 6, 5, 999, out _).SceneNumber == 20, "mountain adds15 to global75 at inclusive20 boundary");
        Check(Resolve(7, 9, 11, 6, 5, 20, out _).ResourceName == "R8N20", "region7 remains normal, unsupported category retains incoming argument");
        var fallback = Resolve(8, 0, 1, 0, 0, 0, out random);
        //Microsoft CRT seed1 yields41,18467,6334: first selects number2, second region4, third discarded.
        Check(fallback.ResourceName == "R4N02" && fallback.UsedRandomFallback && random.Calls == 3, "region8 fallback uses scene draw before region draw and trailing discard");
        Check(random.State == 415139642 && random.Next() == 26500, "third discarded draw leaves exact next shared RNG value");
        Check(Resolve(0, 2, 0, 0, 6, 0, out random).ResourceName == "R4N02" && random.Calls == 3, "number21 triggers same fallback");
        Check(Resolve(-1, 0, -2, 0, 0, 0, out random).ResourceName == "R0N-02" && random.Calls == 1, "native signed comparisons do not clamp negative retained globals");
        short[] globals = new short[76];
        globals[70] = 2;
        globals[71] = 4;
        globals[72] = 2;
        globals[75] = 3;
        Check(CombatRandomEncounterResolution.Resolve(CombatRandomEncounterInputs.FromGlobals(globals, 1), new(1)).ResourceName == "R5N18", "global70..75 alias projection");
        globals[70] = 1;
        bool rejected = false;
        try
        {
            _ = CombatRandomEncounterInputs.FromGlobals(globals, 1);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Check(rejected, "wrong variant rejected before RNG");
        using var archive = BinderArchive.Open(Path.Combine(resourceRoot, "SOD.000"));
        var names = archive.Entries.Select(e => e.Name.ToUpperInvariant()).ToHashSet();
        int expected = 0;
        for (int region = 1; region <= 7; region++)
        {
            for (int number = 1; number <= 20; number++)
            {
                string name = $"R{region}N{number:D2}";
                Check(names.Contains(name), "canonical random member inventory");
                expected++;
                int index = archive.FindResource(System.Text.Encoding.ASCII.GetBytes(name));
                var scene = SodV2.Parse(archive.ReadMember(index));
                Check(scene.Count <= 62, "real scene fits native68 runtime actor allocation");
                bool marker = false;
                for (int actor = 0; actor < scene.Count; actor++)
                {
                    int type = scene.GetObject(actor).GetInteger(2);
                    if (type == 0)
                    {
                        marker = true;
                    }

                    Check((uint)type < 60, "real scene info type fits native local lookup");
                }

                Check(marker, "real random scene has party spawn marker required by common4128c0 loader");
            }
        }

        Check(expected == 140 && !names.Any(n => n.StartsWith("R8N", StringComparison.Ordinal)), "missing native R8 assets stay explicit, never substituted");
        var voices = new List<Voice>();
        int seedCalls = 0;
        IBufferedSoundEffect Load(ReadOnlyMemory<byte> bytes)
        {
            Check(!bytes.IsEmpty, "actual random encounter sound payload");
            var voice = new Voice();
            voices.Add(voice);
            return voice;
        }

        uint Seed()
        {
            Check(voices.Count == 31, "numeric0..49 preload precedes random entry reseed");
            seedCalls++;
            return 1;
        }

        random = new(999);
        using (var entry = CombatEntryPreparation.PrepareRandom(resourceRoot, new(0, 0, 1, 0, 0, 999), random, Seed, 1000, Load, _ =>
        {
        }))
        {
            Check(seedCalls == 1 && entry.Seed == 1 && entry.Request.Kind == FixedEncounterKind.RandomField && entry.Request.ResourceName == "R1N01", "random entry resolves from raw inputs after entry seed");
            Check(entry.MusicTrack == 21 && random.Calls == 1 && random.State == 2745024, "random music does not consume an additional RNG draw");
            Check(entry.Runtime.Scene.Actors.Count > 0 && entry.Runtime.Scene.PartySpawns.Count == 6 && entry.Assets.BackgroundWidth > 0, "actual random resource adapts actors, party markers and raster assets");
            var scene = entry.Runtime.Scene;
            int markers = 0;
            for (int i = 0; i < scene.Source.Count; i++)
            {
                if (scene.Source.GetObject(i).GetInteger(2) == 0)
                {
                    markers++;
                }
            }

            Check(scene.RuntimeActorCount == scene.Source.Count + 6 && scene.PartyStartIndex == scene.Source.Count
&& scene.Actors.Count == scene.Source.Count - markers, "412a23 allocation and412bf1 marker removal preserve original party start");
            foreach (var spawn in scene.PartySpawns)
            {
                var actor = entry.Runtime.Actors[spawn.RuntimeIndex].State;
                Check(LegacyActorFixture.ReadInt32LittleEndian(actor.AsSpan(0x70)) == spawn.X
&& LegacyActorFixture.ReadInt32LittleEndian(actor.AsSpan(0x78)) == spawn.Y && actor.Team == 100 && actor.CanTurn == 1, "412c28..96 projects actual random party marker coordinates and flags");
            }
        }

        Check(voices.All(v => v.Stops == 1 && v.Disposals == 1), "random entry disposes owned sound banks");
        voices.Clear();
        bool missing = false;
        try
        {
            using var entry = CombatEntryPreparation.PrepareRandom(resourceRoot, new(8, 0, 1, 0, 0, 0), random, () => 9, 1000, Load, _ =>
            {
            });
        }
        catch (FileNotFoundException)
        {
            missing = true;
        }

        Check(missing && random.Calls == 3 && voices.Count == 31 && voices.All(v => v.Stops == 1 && v.Disposals == 1), "native R8 missing resource preserves RNG draws and releases acquired voices");
        return "Random encounter signed thresholds, native argument order/CRT consumption, aliases and140 real scene identities passed.";
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
