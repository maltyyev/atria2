using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Atria.Resources;

namespace Atria.Game.Combat;

internal static class AuthoredEncounters
{
    private const int ObjectCountOffset = 30;
    private const int MinimumDepthOffset = 73;
    private const int MaximumDepthOffset = 77;
    private const int PartySpawnX = 160;
    private const int EnemyWaveCondition = 12;
    private const int SpawnAtBothEdges = 2;
    private enum Field
    {
        X = 0, Depth = 1, Type = 2, Facing = 10, Wave = 12, Condition = 13, Placement = 14
    }

    private static readonly Dictionary<string, JsonElement> scenes = Load();

    public static SodV2? Read(string root, string name)
    {
        if (!scenes.TryGetValue(name, out var scene))
        {
            return null;
        }

        var template = SodV2.Parse(GameResources.Read(root, "SOD.000", Encoding.ASCII.GetBytes(scene.GetProperty("template").GetString()!)));
        var rows = scene.GetProperty("actors");
        byte[] bytes = new byte[SodV2.HeaderSize + (rows.GetArrayLength() + 1) * SodV2.RecordSize];
        template.Header.Span.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(ObjectCountOffset), rows.GetArrayLength() + 1);
        int top = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(MinimumDepthOffset));
        int bottom = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(MaximumDepthOffset));
        void Write(int actor, Field field, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(SodV2.HeaderSize + actor * SodV2.RecordSize + (int)field * sizeof(int)), value);
        Write(0, Field.X, PartySpawnX);
        Write(0, Field.Depth, top);
        int index = 1;
        foreach (var row in rows.EnumerateArray())
        {
            int type = row.GetProperty("type").GetInt32();
            Write(index, Field.X, row.GetProperty("x").GetInt32());
            Write(index, Field.Depth, top + (bottom - top) * row.GetProperty("depthPercent").GetInt32() / 100 - CombatSceneBaselines.GetDepthOffset(type));
            Write(index, Field.Type, type);
            Write(index, Field.Facing, 1);
            if (row.TryGetProperty("wave", out var wave) && wave.GetInt32() > 0)
            {
                Write(index, Field.Wave, wave.GetInt32());
                Write(index, Field.Condition, EnemyWaveCondition);
                Write(index, Field.Placement, SpawnAtBothEdges);
            }
            index++;
        }
        return SodV2.Parse(bytes);
    }

    internal static void Apply(CombatSceneRuntime runtime)
    {
        if (!scenes.TryGetValue(runtime.Scene.Request.ResourceName, out var scene))
        {
            return;
        }

        int index = 0;
        foreach (var row in scene.GetProperty("actors").EnumerateArray())
        {
            var image = runtime.Actors[index++];
            var actor = image.State;
            actor.Health = actor.MaximumHealth = actor.DisplayedHealth = actor.HealthBarMaximum = row.GetProperty("health").GetInt32();
            actor.Attack = row.GetProperty("attack").GetInt32();
            actor.Defense = row.GetProperty("defense").GetInt32();
            image.IsRitualAnchor = row.TryGetProperty("anchor", out var anchor) && anchor.GetBoolean();
            // New encounters have finite reinforcements; no recycled enemy resurrection loops.
            var info = image.Binding!.Info;
            var attacks = info.Attacks.Select(attack => image.Binding.Assets.Animation.Sequences[attack.Animation].Frames.Any(frame => (CombatSpellEffect)frame.Effect is CombatSpellEffect.RaiseUndead or CombatSpellEffect.RaiseAllies)
                ? attack with
                {
                    Animation = info.Attacks.Last(candidate => !image.Binding.Assets.Animation.Sequences[candidate.Animation].Frames.Any(frame => (CombatSpellEffect)frame.Effect is CombatSpellEffect.RaiseUndead or CombatSpellEffect.RaiseAllies)).Animation
                }
                : attack).ToArray();
            if (!attacks.SequenceEqual(info.Attacks))
            {
                var adapted = info with
                {
                    Attacks = attacks,
                    BasicAttackCount = attacks.Length
                };
                image.Binding = new(image.Binding.NativeIndex, 0, adapted, image.Binding.Assets);
                image.Animation = new(actor, adapted, image.Binding.Assets);
                image.Animation.BeginMotion(0, 0);
            }
        }
    }

    private static Dictionary<string, JsonElement> Load()
    {
        using var document = GameData.Open("AuthoredEncounters");
        return document.RootElement.EnumerateObject().ToDictionary(row => row.Name, row => row.Value.Clone());
    }
}
