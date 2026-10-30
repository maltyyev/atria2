using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Dungeon;

/// <summary>New dungeon identities reuse immutable artwork and geometry, with their own events and saved tile banks.</summary>
internal static class AuthoredDungeons
{
    private const int MapSide = 10;
    private const int ResourceNameBytes = 13;
    private const int TileTerrainOffset = 4;
    private const int TileEventOffset = 88;
    private const int TileFunctionOffset = 128;
    private const int TileFunctionBytes = 20;
    private const int SceneTitleOffset = 73;
    private const int SceneScriptOffset = 94;
    private const int EntranceEvent = 1;
    private const int ExitEvent = 2;
    private const int RepeatableScriptEvent = 9;
    private const int WallTerrain = 16;
    private const int VoidTerrain = 64;
    private static readonly Dictionary<string, JsonElement> definitions = Load();

    public static byte[]? Read(string root, string archive, ReadOnlySpan<byte> member)
    {
        string name = Path.GetFileNameWithoutExtension(Encoding.ASCII.GetString(member)).ToUpperInvariant();
        if (!definitions.TryGetValue(name, out var definition)) return null;
        if (archive == "SCRIPT.000") return Encoding.ASCII.GetBytes(definition.GetProperty("source").GetString()!);
        if (archive != "AT2AMF.000") return null;

        using var maps = BinderArchive.Open(Path.Combine(root, archive));
        byte[] bytes = maps.ReadMember(maps.FindResource(Encoding.ASCII.GetBytes(definition.GetProperty("template").GetString()!)));
        var original = AmfMap.Parse(bytes);
        if (original.Width != MapSide || original.Height != MapSide || original.EmbeddedScene is null)
            throw new InvalidDataException("Authored dungeon template requires a 10x10 map and artwork.");

        WriteName(bytes.AsSpan(0, ResourceNameBytes), name + ".sod");
        int sceneOffset = AmfMap.HeaderSize + MapSide * MapSide * AmfMap.TileSize;
        WriteName(bytes.AsSpan(sceneOffset + SceneScriptOffset, ResourceNameBytes), name + ".scr");
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(sceneOffset + SceneTitleOffset), definition.GetProperty("titleId").GetInt32());
        for (int index = 0; index < MapSide * MapSide; index++)
            bytes.AsSpan(AmfMap.HeaderSize + index * AmfMap.TileSize + TileEventOffset, AmfMap.TileSize - TileEventOffset).Clear();

        Place(definition.GetProperty("entrance"), EntranceEvent);
        Place(definition.GetProperty("exit"), ExitEvent);
        foreach (var point in definition.GetProperty("points").EnumerateArray())
        {
            int offset = Place(point.GetProperty("at"), RepeatableScriptEvent);
            WriteName(bytes.AsSpan(offset + TileFunctionOffset, TileFunctionBytes), point.GetProperty("function").GetString()!);
        }
        return bytes;

        int Place(JsonElement at, int kind)
        {
            int x = at[0].GetInt32(), y = at[1].GetInt32();
            if ((uint)x >= MapSide || (uint)y >= MapSide) throw new InvalidDataException("Authored dungeon point is out of bounds.");
            int offset = AmfMap.HeaderSize + (y * MapSide + x) * AmfMap.TileSize;
            if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + TileTerrainOffset)) is WallTerrain or VoidTerrain)
                throw new InvalidDataException("Authored dungeon point is inside a wall.");
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + TileEventOffset), kind);
            return offset;
        }
    }

    public static string? TitleKey(ReadOnlySpan<byte> script, int titleId)
    {
        string name = Path.GetFileNameWithoutExtension(Encoding.ASCII.GetString(script)).ToUpperInvariant();
        return definitions.TryGetValue(name, out var definition) && definition.GetProperty("titleId").GetInt32() == titleId
            ? definition.GetProperty("titleKey").GetString() : null;
    }

    public static bool ApplyProgress(DungeonMap map, GameState state)
    {
        string name = Path.GetFileNameWithoutExtension(Encoding.ASCII.GetString(
            Atria.Game.World.WorldMapContext.CString(map.Context.HeaderBytes[..ResourceNameBytes]))).ToUpperInvariant();
        if (!definitions.TryGetValue(name, out var definition)) return false;
        bool changed = false;
        foreach (var point in definition.GetProperty("points").EnumerateArray())
        {
            var at = point.GetProperty("at");
            int index = at[1].GetInt32() * MapSide + at[0].GetInt32();
            bool complete = state.NarrativeHistory.Contains(point.GetProperty("completedScene").GetString()!);
            // Older scene editions could finish all seals in one battle, without individual guard histories.
            if (point.TryGetProperty("completedQuest", out var quest))
                complete |= state.Journal.Progress(state, quest[0].GetInt32()).HasFlag((QuestMilestones)quest[1].GetInt32());
            if (complete && map.Read(index, TileEventOffset) != 0)
            {
                map.Write(index, TileEventOffset, 0);
                changed = true;
            }
        }
        return changed;
    }

    private static void WriteName(Span<byte> destination, string value)
    {
        if (value.Length >= destination.Length || value.Any(c => c > 127))
            throw new InvalidDataException("Invalid authored dungeon resource reference.");
        destination.Clear();
        Encoding.ASCII.GetBytes(value, destination);
    }

    private static Dictionary<string, JsonElement> Load()
    {
        using var document = GameData.Open("AuthoredDungeons");
        return document.RootElement.EnumerateObject().ToDictionary(row => row.Name, row => row.Value.Clone(), StringComparer.Ordinal);
    }
}
