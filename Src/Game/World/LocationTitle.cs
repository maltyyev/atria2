using System.Buffers.Binary;
using Atria.Game.Town;
using Atria.Resources;

namespace Atria.Game.World;

/// <summary>Language-independent location identity; resolve text only when drawing.</summary>
public readonly record struct LocationTitle(int MessageId, bool IncludeWorldSuffix = false)
{
    private const int FirstWorldMessage = 298;
    private const int WorldAreaCount = 8;
    private const int FirstDungeonMessage = 173;
    private const int DungeonTitleCount = 18;
    private const int DungeonTitleOffset = 73;
    private const string WorldSuffixKey = "messages.309";

    public string Text => GameText.Get($"messages.{MessageId:D3}").Trim()
        + (IncludeWorldSuffix ? " " + GameText.Get(WorldSuffixKey).Trim() : "");

    public static LocationTitle World(int area, bool includeSuffix = false)
    {
        if ((uint)area >= WorldAreaCount)
            throw new InvalidDataException("Location title references an unknown world area.");
        return new(FirstWorldMessage + area, includeSuffix);
    }

    public static LocationTitle Town(int town)
    {
        if ((uint)town >= (uint)TownCatalog.Count)
            throw new InvalidDataException("Location title references an unknown town.");
        return new(TownCatalog.Get(town).NameMessageId);
    }

    public static LocationTitle Dungeon(AmfMap map)
    {
        var scene = map.EmbeddedScene
            ?? throw new InvalidDataException("Dungeon title requires embedded scene metadata.");
        int title = BinaryPrimitives.ReadInt32LittleEndian(scene.Header.Span.Slice(DungeonTitleOffset, sizeof(int)));
        if ((uint)title >= DungeonTitleCount)
            throw new InvalidDataException("Dungeon title references a non-title message.");
        return new(FirstDungeonMessage + title);
    }
}
