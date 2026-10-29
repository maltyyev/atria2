using System.Text;
using System.Text.Json;
using Atria.Resources;

namespace Atria.Game.State;

/// <summary>Authored scenes run through the same checkpointed interpreter as archive scenarios.</summary>
internal static class NarrativeScenes
{
    internal sealed record Scene(string Id, string AfterMember, string AfterFunction, JsonElement Conditions, byte[] Source);
    internal static IReadOnlyList<Scene> All { get; } = Load();

    public static Scene? After(GameState state, string member, string function) =>
        All.FirstOrDefault(scene => !state.NarrativeHistory.Contains(scene.Id) && scene.AfterMember == member && scene.AfterFunction == function && Matches(scene.Conditions, state));

    public static byte[]? Read(ReadOnlySpan<byte> member)
    {
        string name = Path.GetFileNameWithoutExtension(Encoding.ASCII.GetString(member)).ToUpperInvariant();
        return All.FirstOrDefault(scene => scene.Id == name)?.Source;
    }

    internal static bool Matches(JsonElement condition, GameState state)
    {
        foreach (var property in condition.EnumerateObject())
        {
            bool matches = property.Name switch
            {
                "all" => property.Value.EnumerateArray().All(id => state.HasHero(id.GetString()!)),
                "any" => property.Value.EnumerateArray().Any(id => state.HasHero(id.GetString()!)),
                "none" => property.Value.EnumerateArray().All(id => !state.HasHero(id.GetString()!)),
                "quests" => property.Value.EnumerateArray().All(row => (int)state.Journal.Progress(state, row[0].GetInt32()) == row[1].GetInt32()),
                "events" => property.Value.EnumerateArray().All(row => state.Events[row[0].GetInt32()] == row[1].GetInt32()),
                "globals" => property.Value.EnumerateArray().All(row => state.Globals[row[0].GetInt32()] == row[1].GetInt32()),
                "world" => state.WorldIndex == property.Value[0].GetInt32() && state.WorldX == property.Value[1].GetInt32() && state.WorldY == property.Value[2].GetInt32(),
                _ => throw new InvalidDataException($"Unknown narrative condition: {property.Name}")
            };
            if (!matches)
            {
                return false;
            }
        }
        return true;
    }

    private static Scene[] Load()
    {
        using var document = GameData.Open("NarrativeScenes");
        var scenes = document.RootElement.EnumerateArray().Select(row => new Scene(
            row.GetProperty("id").GetString()!, row.GetProperty("afterMember").GetString()!,
            row.GetProperty("afterFunction").GetString()!, row.GetProperty("conditions").Clone(),
            Encoding.ASCII.GetBytes(row.GetProperty("source").GetString()!))).ToArray();
        if (scenes.Select(scene => scene.Id).Distinct().Count() != scenes.Length
            || scenes.Any(scene => scene.Id.Length > 8 || !scene.Id.StartsWith("NAR", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Invalid authored scenario identities.");
        }

        return scenes;
    }
}
