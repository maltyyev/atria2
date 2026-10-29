using System.Buffers.Binary;
using Atria.Resources;

namespace Atria.Game.Combat;

internal static class CombatEncounterBalance
{
    public static SodV2 Apply(string sceneName, SodV2 source)
    {
        using var definitions = GameData.Open("CombatEncounterBalance");
        if (!definitions.RootElement.TryGetProperty(sceneName, out var groups))
        {
            return source;
        }

        var halvedTypes = groups.EnumerateObject().Select(group => group.Value.GetInt32()).ToHashSet();
        if (halvedTypes.Any(type => type <= 0 || type >= CombatSceneBaselines.Count))
        {
            throw new InvalidDataException($"Invalid enemy group in encounter balance: {sceneName}");
        }

        var occurrences = new Dictionary<(int Type, int Condition, int Wave), int>();
        var retained = new List<SodObject>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            var actor = source.GetObject(i);
            int type = actor.GetInteger(2);
            int team = actor.GetInteger(15);
            if (team < 100 && halvedTypes.Contains(type))
            {
                var key = (type, actor.GetInteger(13), actor.GetInteger(12));
                int occurrence = occurrences.GetValueOrDefault(key);
                occurrences[key] = occurrence + 1;
                // Keep alternating members across all placements in a wave, rounding up.
                if (occurrence % 2 != 0)
                {
                    continue;
                }
            }
            retained.Add(actor);
        }

        // Rebuild only the in-memory scene; the original archive remains immutable.
        byte[] adjusted = new byte[SodV2.HeaderSize + retained.Count * SodV2.RecordSize];
        source.Header.CopyTo(adjusted);
        const int ObjectCountOffset = 30;
        BinaryPrimitives.WriteInt32LittleEndian(adjusted.AsSpan(ObjectCountOffset), retained.Count);
        for (int i = 0; i < retained.Count; i++)
        {
            retained[i].Raw.Span.CopyTo(adjusted.AsSpan(SodV2.HeaderSize + i * SodV2.RecordSize));
        }
        return SodV2.Parse(adjusted);
    }
}
