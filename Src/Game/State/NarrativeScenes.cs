using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Atria.Resources;

namespace Atria.Game.State;

/// <summary>Authored scenes run through the same checkpointed interpreter as archive scenarios.</summary>
internal static class NarrativeScenes
{
    internal sealed record Scene(string Id, string AfterMember, string AfterFunction, JsonElement Conditions, byte[] Source,
        bool Repeatable = false, bool Replaces = false);
    internal static IReadOnlyList<Scene> All { get; } = Load();
    private static readonly Dictionary<(string Member, string Hash), byte[]> legacySources = LoadLegacySources();

    public static Scene? After(GameState state, string member, string function, int? worldArea = null) =>
        Find(state, member, function, worldArea, replaces: false);

    public static Scene? Replacement(GameState state, string member, string function) =>
        Find(state, member, function, null, replaces: true);

    private static Scene? Find(GameState state, string member, string function, int? worldArea, bool replaces) =>
        All.FirstOrDefault(scene => scene.Replaces == replaces && (scene.Repeatable || !state.NarrativeHistory.Contains(scene.Id))
            && scene.AfterMember == member && scene.AfterFunction == function && Matches(scene.Conditions, state, worldArea));

    public static byte[]? Read(ReadOnlySpan<byte> member, string? sourceHash = null)
    {
        string name = Path.GetFileNameWithoutExtension(Encoding.ASCII.GetString(member)).ToUpperInvariant();
        byte[]? current = All.FirstOrDefault(scene => scene.Id == name)?.Source;
        if (current is null || sourceHash is null || Convert.ToHexString(SHA256.HashData(current)) == sourceHash)
        {
            return current;
        }

        // Recorded host-call offsets belong to the exact source edition saved at the checkpoint.
        return legacySources.TryGetValue((name, sourceHash), out var previous)
            ? previous : throw new InvalidDataException("Unknown authored scenario continuation source.");
    }

    private static Dictionary<(string Member, string Hash), byte[]> LoadLegacySources()
    {
        using var document = GameData.Open("NarrativeSceneLegacySources");
        var sources = new Dictionary<(string, string), byte[]>();
        foreach (var member in document.RootElement.EnumerateObject())
        {
            if (!All.Any(scene => scene.Id == member.Name))
            {
                throw new InvalidDataException("Legacy authored source has no current scene identity.");
            }

            foreach (var source in member.Value.EnumerateArray())
            {
                byte[] bytes = Encoding.ASCII.GetBytes(source.GetString()!);
                sources.Add((member.Name, Convert.ToHexString(SHA256.HashData(bytes))), bytes);
            }
        }
        return sources;
    }

    internal static bool Matches(JsonElement condition, GameState state, int? worldArea = null)
    {
        foreach (var property in condition.EnumerateObject())
        {
            bool matches = property.Name switch
            {
                "all" => property.Value.EnumerateArray().All(id => state.HasHero(id.GetString()!)),
                "any" => property.Value.EnumerateArray().Any(id => state.HasHero(id.GetString()!)),
                "none" => property.Value.EnumerateArray().All(id => !state.HasHero(id.GetString()!)),
                "quests" => property.Value.EnumerateArray().All(row => (int)state.Journal.Progress(state, row[0].GetInt32()) == row[1].GetInt32()),
                "questsActive" => property.Value.EnumerateArray().All(id => state.Journal.CanTrack(state, id.GetInt32())),
                "events" => property.Value.EnumerateArray().All(row => state.Events[row[0].GetInt32()] == row[1].GetInt32()),
                "globals" => property.Value.EnumerateArray().All(row => state.Globals[row[0].GetInt32()] == row[1].GetInt32()),
                // WorldIndex is a retained legacy field, not the loaded AMF's area identity.
                "world" => worldArea == property.Value[0].GetInt32() && state.WorldX == property.Value[1].GetInt32() && state.WorldY == property.Value[2].GetInt32(),
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
            Encoding.ASCII.GetBytes(row.GetProperty("source").GetString()!),
            row.TryGetProperty("repeatable", out var repeatable) && repeatable.GetBoolean(),
            row.TryGetProperty("replaces", out var replaces) && replaces.GetBoolean())).ToArray();
        if (scenes.Select(scene => scene.Id).Distinct().Count() != scenes.Length
            || scenes.Any(scene => scene.Id.Length > 8 || !scene.Id.StartsWith("NAR", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Invalid authored scenario identities.");
        }

        return scenes;
    }
}
