using System.Text;
using Atria.Game.Dungeon;
using Atria.Game.Exploration;
using Atria.Game.Interiors;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Game.World;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Script;

namespace Atria.Game.Persistence;

/// <summary>Prepares replacement resources without committing shared game storage.</summary>
internal sealed class SessionSnapshotLoader(
    string root,
    GameState state,
    LegacyRandom random,
    BitmapFont smallFont,
    IReadOnlyList<ReadOnlyMemory<byte>> messages,
    ItemCatalog items,
    Action<int> validateMusicTrack
)
{
    private (WorldMapContext Map, byte[] Raw) PrepareSessionMap(SessionMapSnapshot saved, bool world = true)
    {
        byte[] raw = Read("AT2AMF.000", saved.Member);
        // Parse the immutable source before replacing only recovered mutable header/tile banks.
        _ = WorldMapContext.Parse(raw);
        saved.Header.CopyTo(raw, 0);
        saved.Tiles.CopyTo(raw, AmfMap.HeaderSize);
        var map = WorldMapContext.Parse(raw);
        _ = map.BackgroundReference;
        if (world)
        {
            _ = WorldMapContext.CString(map.HeaderBytes[..13]);
            for (int tile = 0; tile < 100; tile++)
            {
                for (int member = 0; member < 10; member++)
                {
                    _ = WorldMapContext.CString(map.TileBytes.Slice(tile * 328 + 128 + member * 20, 20));
                }
            }
        }
        else
        {
            var dungeonMap = new DungeonMap(map);
            for (int tile = 0; tile < 100; tile++)
            {
                if (dungeonMap.Read(tile, 88) is 7 or 9 || (dungeonMap.Read(tile, 88) == 6 && dungeonMap.Read(tile, 92) == 4))
                {
                    _ = dungeonMap.Member(tile);
                }
            }
        }

        return (map, raw);
    }

    public PreparedSession Prepare(NativeRuntimeContext context, GameState savedState)
    {
        var saved = SessionRuntimeCodec.Decode(context);
        if ((uint)savedState.WorldX >= 10 || (uint)savedState.WorldY >= 10)
        {
            throw new InvalidDataException("Saved world coordinates exceed the recovered10x10 map.");
        }

        if (saved.SavedMode != savedState.Mode || (!saved.ExactResume && (savedState.ScriptActive != 0 || savedState.EndingResetFlag != 0)))
        {
            throw new InvalidDataException("Snapshot mode/script transition contradicts the supported activation context.");
        }

        if (saved.PlayingMusicTrack > 0)
        {
            validateMusicTrack(saved.PlayingMusicTrack);
        }

        if (saved.MusicId > 0)
        {
            validateMusicTrack(saved.MusicId);
        }

        var current = PrepareSessionMap(saved.CurrentMap, saved.CurrentMapKind != 2);
        int entryTrack = saved.SavedMode == GameMode.Town ? TownCatalog.Get(saved.Location!.SceneIndex).MusicTrack : saved.SavedMode == GameMode.World ? checked(current.Map.AreaIndex + 1) : saved.MusicId;
        if (entryTrack > 0)
        {
            validateMusicTrack(entryTrack);
        }

        var result = new PreparedSession
        {
            Snapshot = saved,
            Map = current.Map,
            Town = saved.Town
        };
        var validatedStack = new ModeStack();
        validatedStack.RestoreRuntime(saved.Modes);
        if (!saved.ExactResume && validatedStack.LegacySaveMode() != saved.SavedMode)
        {
            throw new InvalidDataException("Underlying save mode disagrees with retained mode slots.");
        }

        var retainedTown = new TownController();
        TownSceneAssets? retainedTownAssets = saved.Town.SceneName.Length == 0 ? null : TownSceneAssets.Load(root, saved.Town.SceneName);
        retainedTown.RestoreRuntime(saved.Town, retainedTownAssets?.Scene, retainedTownAssets?.Objects);
        result.TownAssets = retainedTownAssets;
        PrepareWorld(saved, savedState, result);
        PrepareDungeon(saved, savedState, result);
        ValidateMapAliases(saved, result);
        PrepareEntry(saved, savedState, result, retainedTown);
        PrepareInterior(saved, result, retainedTown);
        ValidateScripts(saved, result);
        return result;
    }

    private void PrepareWorld(SessionRuntimeSnapshot saved, GameState savedState, PreparedSession result)
    {
        if (saved.Exploration is { } world)
        {
            var loaded = PrepareSessionMap(world.Map);
            result.WorldRaw = loaded.Raw;
            if ((uint)loaded.Map.AreaIndex >= 8)
            {
                throw new InvalidDataException("Retained world area is outside eight saved records.");
            }

            result.WorldEntry = new(loaded.Map, loaded.Map.AreaIndex, world.PrimaryName, world.SecondaryName, world.ScriptMember, world.Map.Member);
            result.WorldAssets = WorldSceneAssets.Load(root, loaded.Map, loaded.Raw);
            result.WorldRenderer = new(result.WorldAssets);
            if (world.Renderer is { } renderer)
            {
                result.WorldRenderer.RestoreRuntime(renderer);
            }

            if (world.Controller is { } controller)
            {
                if (controller.Moving && saved.SavedMode == GameMode.World)
                {
                    throw new NotSupportedException("Moving world snapshot clock rebasing is not recovered.");
                }

                result.World = new(
                    state,
                    loaded.Map,
                    result.WorldAssets.CharacterAnimation,
                    random,
                    result.WorldRenderer.IsSceneReferenceBlocked,
                    controller.Direction,
                    controller.TravelSteps
                );
                result.World.RestoreRuntime(controller);
            }

            if (saved.CurrentMapKind == 1)
            {
                if (!saved.CurrentMap.Member.AsSpan().SequenceEqual(world.Map.Member))
                {
                    throw new InvalidDataException("Current world alias resource identity differs.");
                }

                result.Map = loaded.Map;
            }

            result.WorldRenderer.RebuildBase(loaded.Map, savedState);
        }
    }

    private void PrepareDungeon(SessionRuntimeSnapshot saved, GameState savedState, PreparedSession result)
    {
        if (saved.Dungeon is { } dungeonSaved)
        {
            var loaded = PrepareSessionMap(dungeonSaved.Map, false);
            var dungeonMap = new DungeonMap(loaded.Map);
            var assets = DungeonSceneAssets.Load(root, loaded.Map, loaded.Raw);
            result.DungeonEntry = new(
                (DungeonEntrySource)dungeonSaved.Source,
                dungeonSaved.Map.Member,
                dungeonSaved.ScriptMember,
                dungeonSaved.ReturnX,
                dungeonSaved.ReturnY,
                dungeonSaved.CaptureArea
            );
            result.Dungeon = new(state, dungeonMap, assets.Base.CharacterAnimation, random, initializeState: false);
            if (dungeonSaved.Controller.Motion.Moving && saved.SavedMode == GameMode.Dungeon)
            {
                throw new NotSupportedException("Moving dungeon snapshot clock rebasing is not recovered.");
            }

            result.Dungeon.RestoreRuntime(dungeonSaved.Controller);
            result.DungeonComposer = new(
                state,
                result.Dungeon,
                dungeonMap,
                assets,
                smallFont,
                GameText.Messages(messages),
                Enumerable.Range(0, ItemCatalog.Count).Select(i => GameText.ItemName(items, i)).ToArray(),
                dungeonSaved.DynamicText
            );
            result.DungeonComposer.Raster.RestoreRuntime(dungeonSaved.Raster);
            result.DungeonComposer.Raster.RebuildObjects(dungeonMap, result.Dungeon.Visibility, savedState.DungeonFog != 0);
            if (saved.CurrentMapKind == 2)
            {
                if (!saved.CurrentMap.Member.AsSpan().SequenceEqual(dungeonSaved.Map.Member))
                {
                    throw new InvalidDataException("Current dungeon alias resource identity differs.");
                }

                result.Map = loaded.Map;
            }
        }
    }

    private void ValidateMapAliases(SessionRuntimeSnapshot saved, PreparedSession result)
    {
        if (saved.CurrentMapKind is < 0 or > 2 || (saved.CurrentMapKind == 1 && result.WorldEntry is null)
|| (saved.CurrentMapKind == 2 && result.Dungeon is null))
        {
            throw new InvalidDataException("Current map alias references absent retained context.");
        }

        if (!result.Map.GetHeader().AsSpan().SequenceEqual(saved.CurrentMap.Header)
|| !result.Map.GetTiles().AsSpan().SequenceEqual(saved.CurrentMap.Tiles))
        {
            throw new InvalidDataException("Current AMF alias contradicts the retained map banks.");
        }
    }

    private void PrepareEntry(SessionRuntimeSnapshot saved, GameState savedState, PreparedSession result, TownController retainedTown)
    {
        if (saved.SavedMode == GameMode.Town && !saved.ExactResume)
        {
            var location = saved.Location!;
            var assets = TownSceneAssets.Load(root, location.SceneMember);
            // Dry-run native entry on a detached controller, collecting host commands without executing them.
            retainedTown.Enter(
                assets.Scene,
                assets.Objects,
                location.SceneIndex,
                0,
                location.SceneMember,
                TownCatalog.Name(messages, location.SceneIndex),
                TownCatalog.Get(location.SceneIndex).MusicTrack,
                result.TownEntryCommands.Add
            );
            result.TownAssets = assets;
            result.Town = retainedTown.CaptureRuntime();
        }
        else if (saved.SavedMode == GameMode.World)
        {
            if (result.World is null || result.WorldRenderer is null || result.WorldEntry is null)
            {
                throw new InvalidDataException("Active world controller was not saved.");
            }

            if (!ReferenceEquals(result.Map, result.WorldEntry.Map))
            {
                throw new InvalidDataException("Active world map alias is invalid.");
            }

            if (!saved.ExactResume)
            {
                result.World.InputDelay = 2;
                ExplorationRules.MarkArrival(result.Map, savedState.WorldX, savedState.WorldY);
            }

            result.WorldRenderer.RebuildBase(result.Map, savedState);
        }
        else if (saved.SavedMode == GameMode.Dungeon && !ReferenceEquals(result.Map, result.Dungeon!.RuntimeMap.Context))
        {
            throw new InvalidDataException("Active dungeon map alias is invalid.");
        }
    }

    private void PrepareInterior(SessionRuntimeSnapshot saved, PreparedSession result, TownController retainedTown)
    {
        if (saved.Interior is { } inside)
        {
            var outer = retainedTown.Scene.GetObject(retainedTown.Selection);
            result.InteriorAssets = TownSceneAssets.Load(root, outer.GetString(0).Span, loadObjects: inside.Mode != GameMode.Inn);
            new InteriorController().RestoreRuntime(inside, result.InteriorAssets.Scene);
            if (inside.Mode == GameMode.Shop)
            {
                result.ItemSprites = SpriteFile.Decode(Read("AT2SPR.000", "item.spr"u8));
            }
        }
    }

    private void ValidateScripts(SessionRuntimeSnapshot saved, PreparedSession result)
    {
        if (saved.Script is { } replay)
        {
            byte[] source = Read("SCRIPT.000", Encoding.ASCII.GetBytes(replay.Member));
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)) != replay.SourceHash)
            {
                throw new InvalidDataException("Saved scenario source changed.");
            }

            var program = Atria.Script.ScriptProgram.Parse(replay.Member, source);
            if (!program.Functions.ContainsKey(replay.Function))
            {
                throw new InvalidDataException("Saved scenario function absent.");
            }

            Atria.Script.CheckpointScriptHost.Validate(program, replay);
        }

        // Validate actual script identity/entry handler before any shared state is replaced.
        ReadOnlySpan<byte> requiredScript = saved.SavedMode == GameMode.Town ? result.TownAssets!.Scene.ScriptReference.Span : saved.ScriptMember;
        if (!requiredScript.IsEmpty)
        {
            var program = Atria.Script.ScriptProgram.Parse(Encoding.ASCII.GetString(requiredScript), Read("SCRIPT.000", requiredScript));
            if (saved.SavedMode == GameMode.Town && !program.Functions.ContainsKey("enter"))
            {
                throw new InvalidDataException("Town load entry script lacks enter handler.");
            }
        }
    }

    private byte[] Read(string archiveName, ReadOnlySpan<byte> member) => GameResources.Read(root, archiveName, member);
}

internal sealed class PreparedSession
{
    public required SessionRuntimeSnapshot Snapshot;
    public required WorldMapContext Map;
    public TownSceneAssets? TownAssets;
    public required TownSnapshot Town;
    public List<TownCommand> TownEntryCommands = [];
    public WorldMapEntry? WorldEntry;
    public WorldSceneAssets? WorldAssets;
    public WorldSceneRenderer? WorldRenderer;
    public ExplorationController? World;
    public byte[] WorldRaw = [];
    public DungeonEntryPlan? DungeonEntry;
    public DungeonController? Dungeon;
    public DungeonComposer? DungeonComposer;
    public TownSceneAssets? InteriorAssets;
    public SpriteFile? ItemSprites;
}
