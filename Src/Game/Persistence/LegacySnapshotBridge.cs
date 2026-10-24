using System.Buffers.Binary;
using System.Text;
using Atria.Game.Exploration;
using Atria.Game.Interiors;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Resources;

namespace Atria.Game.Persistence;

public sealed record LegacyLoadDestination(int Mode, int AreaIndex, ReadOnlyMemory<byte> MapMember, WorldSceneReference? Town, WorldMapEntry? World);
/// <summary>Detached legacy load projection. Prepare resource identities before committing any live storage.</summary>
public sealed class LegacyLoadPlan
{
    public LegacySaveImage Image
    {
        get;
    }
    public GameState State
    {
        get;
    }
    public WorldAreaStore Areas
    {
        get;
    }
    public ShopStock[] Shops
    {
        get;
    }
    public LegacySlotMetadata Metadata
    {
        get;
    }
    public static IReadOnlyList<string> UnmappedLegacyFields => ["46dc1c/46dc20 (8 bytes)", "46dc27..46dc87 (97 bytes after three recovered settings bytes)", "46fc8a (4 bytes)"];

    internal LegacyLoadPlan(LegacySaveImage image, IReadOnlyList<ShopStock> baseline)
    {
        Image = image;
        State = image.ReadRecoveredState();
        Metadata = LegacySlotMetadata.Parse(image.GetSlotMetadata());
        Areas = new();
        for (int i = 0; i < 8; i++)
        {
            Areas.Import(i, image.GetAreaInline(i), image.GetAreaPayload(i));
        }

        Shops = LegacyShopProjection.Overlay(image.GetShopPrefix(), baseline);
    }

    public LegacyLoadDestination PrepareDestination(Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> readAmf)
    {
        int area = Metadata.AreaIndex;
        if ((uint)area >= 8)
        {
            throw new InvalidDataException("Legacy metadata area exceeds eight records.");
        }

        var mapName = Encoding.ASCII.GetBytes($"ground{area + 1}.amf");
        if (State.Mode == 300)
        {
            if ((uint)State.WorldX >= 10 || (uint)State.WorldY >= 10)
            {
                throw new InvalidDataException("Loaded world coordinates exceed10x10 map.");
            }

            return new(300, area, mapName, null, WorldMapBoundary.Load(mapName, Areas, readAmf));
        }

        if (State.Mode != 400)
        {
            throw new NotSupportedException($"Native load destination mode {State.Mode} has no recovered resource activation contract.");
        }

        byte[] tiles = Areas.GetPayload(area);
        int town = Metadata.TownId;
        for (int i = 0; i < 100; i++)
        {
            var tile = tiles.AsSpan(i * 328, 328);
            if (BinaryPrimitives.ReadInt32LittleEndian(tile[88..]) != town)
            {
                continue;
            }

            byte[] scene = WorldMapContext.CString(tile.Slice(128, 20));
            if (town == 7 && !scene.AsSpan().SequenceEqual("07mill.sod"u8) || town == 8 && !scene.AsSpan().SequenceEqual("08loose.sod"u8))
            {
                continue;
            }

            return new(400, area, mapName, new(town, scene, WorldMapContext.CString(tile.Slice(148, 20)), area), null);
        }

        throw new InvalidDataException("Loaded town has no matching saved area tile (421ed0).");
    }

    /// <summary>Only mapped persisted storage is copied. Caller must resolve UnmappedLegacyFields and prepare all resources before activation.</summary>
    public void CommitRecoveredStorage(GameState target, WorldAreaStore targetAreas, ShopStock[] targetShops)
    {
        if (targetShops.Length != 30)
        {
            throw new InvalidDataException("Runtime shop bank must contain30 records.");
        }
        // All source blocks were length/count-validated when preparing this detached plan.
        var inline = new byte[8][];
        var payload = new byte[8][];
        for (int i = 0; i < 8; i++)
        {
            inline[i] = Areas.GetInline(i);
            payload[i] = Areas.GetPayload(i);
        }

        var shops = Shops.Select(stock => stock.Clone()).ToArray();
        LegacySnapshotBridge.CopyRecoveredState(State, target);
        for (int i = 0; i < 8; i++)
        {
            targetAreas.Import(i, inline[i], payload[i]);
        }

        for (int i = 0; i < 30; i++)
        {
            targetShops[i] = shops[i];
        }
    }
}

public static class LegacySnapshotBridge
{
    public static LegacyLoadPlan Decode(LegacySaveImage image, IReadOnlyList<ShopStock> baseline) => new(image, baseline);
    public static LegacySaveImage Capture(
        LegacySaveImage template,
        GameState state,
        int savedMode,
        WorldAreaStore areas,
        IReadOnlyList<ShopStock> shops,
        LegacySlotMetadata metadata
    )
    {
        var result = template.WithRecoveredState(state).WithSavedMode(savedMode).WithSlotMetadata(metadata).WithShopPrefix(LegacyShopProjection.EncodePrefix(shops));
        for (int i = 0; i < 8; i++)
        {
            result = result.WithArea(i, areas.GetInline(i), areas.GetPayload(i));
        }

        return result;
    }

    internal static void CopyRecoveredState(GameState source, GameState target)
    {
        target.Mode = source.Mode;
        target.WorldIndex = source.WorldIndex;
        target.WorldX = source.WorldX;
        target.WorldY = source.WorldY;
        target.ResourceCounterDc88 = source.ResourceCounterDc88;
        target.InitializedPartyCount = source.InitializedPartyCount;
        target.Money = source.Money;
        target.Score = source.Score;
        target.ResourceCounterFc9a = source.ResourceCounterFc9a;
        target.EventCounter = source.EventCounter;
        target.Year = source.Year;
        target.Month = source.Month;
        target.Day = source.Day;
        target.DayCounterStorage = source.DayCounterStorage;
        PartyCodec.Copy(source.Party, target.Party);
        source.OwnedItems.CopyTo(target.OwnedItems, 0);
        source.AvailableItems.CopyTo(target.AvailableItems, 0);
        source.QuestItemRecords.CopyTo(target.QuestItemRecords, 0);
        source.Events.CopyTo(target.Events, 0);
        source.EventStatuses.CopyTo(target.EventStatuses, 0);
        target.Journal.CopyFrom(source.Journal);
        source.Globals.CopyTo(target.Globals, 0);
    }
}
