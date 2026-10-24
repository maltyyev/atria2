using System.Text.Json;

namespace Atria.Resources;

/// <summary>Read-only data shipped with the engine for tables originally stored in the executable.</summary>
internal static class GameData
{
    public static JsonDocument Open(string name)
    {
        string resource = $"Atria.Content.Atria2.{name}.json";
        using var stream = typeof(GameData).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException($"Missing game definition: {resource}");
        return JsonDocument.Parse(stream);
    }
}
