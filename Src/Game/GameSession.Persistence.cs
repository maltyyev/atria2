using System.Text;
using Atria.Game.Dungeon;
using Atria.Game.Exploration;
using Atria.Game.Interiors;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Game.World;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    private readonly SaveLoadMenuController persistenceMenu = new();
    private SaveLoadMenuRenderer? persistenceRenderer;
    private NativeSaveStore? persistenceStore;
    private SaveSlotInfo[] persistenceSlots = [];
    private LegacySlotMetadata[] persistenceMetadata = [];
    private ushort[]? persistenceBackground;
    private int persistenceSelection;
    private LegacySaveImage? persistenceLegacyTemplate;
    private NativeSaveStore PersistenceStore => persistenceStore ??= new(Path.Combine(sessionOutputDirectory, "saves"), root);

    private void EnterPersistence(int mode)
    {
        if (mode is not (GameMode.Save or GameMode.Load))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (persistenceBackground is null)
        {
            var bitmap = BmpImage.Decode(Read("AT2BMP.000", "saveload.bmp"u8));
            if (bitmap.Width != 640 || bitmap.Height != 480)
            {
                throw new InvalidDataException("Save/load background must be640x480.");
            }

            persistenceBackground = bitmap.ToRgb565();
        }

        persistenceRenderer ??= new(etc, smallFont, BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG.FNT")), false), displayMessages);
        persistenceMenu.Enter((SaveLoadMenuKind)mode, persistenceSelection, HandlePersistence);
    }

    private void HandlePersistenceInput()
    {
        if (persistenceMenu.Failure is not null)
        {
            if (Query(bindings.Confirm) || Query(bindings.Cancel))
            {
                Sound(4);
                persistenceMenu.AcknowledgeFailure();
            }

            return;
        }

        if (persistenceMenu.PendingOperation)
        {
            bool loading = persistenceMenu.Kind == SaveLoadMenuKind.Load;
            //42b060 saves+refreshes BEFORE polling this opportunity's keys. Loading422660 instead returns after activation.
            persistenceMenu.Input(default, persistenceMetadata, HandlePersistence);
            if (loading || persistenceMenu.Failure is not null)
            {
                persistenceSelection = persistenceMenu.Selection;
                return;
            }
        }

        bool down = false, up = false, left = false, right = false, accept = false, cancel = false;
        if (persistenceMenu.Countdown == 0)
        {
            if (persistenceMenu.Confirmation)
            {
                left = Query(bindings.Previous);
                if (!left)
                {
                    right = Query(bindings.Next);
                }
            }
            else
            {
                down = Query(bindings.Down);
                if (!down)
                {
                    up = Query(bindings.Up);
                }
            }

            if (!down && !up && !left && !right)
            {
                accept = Query(bindings.Confirm);
                if (!accept)
                {
                    cancel = Query(bindings.Cancel);
                }
            }
        }

        int wheel = controls.TakeWheel();
        if (wheel != 0 && !persistenceMenu.Confirmation)
        {
            down = wheel < 0;
            up = wheel > 0;
            accept = false;
        }

        if (accept && persistenceSlots.Length > 0 && !persistenceMenu.Confirmation && !persistenceMenu.EmptyNotice
&& persistenceMenu.Kind == SaveLoadMenuKind.Load && persistenceSlots[persistenceMenu.Selection].Status is SaveSlotStatus.Invalid or SaveSlotStatus.Unreadable)
        {
            Sound(3);
            persistenceMenu.ReportFailure(persistenceSlots[persistenceMenu.Selection].Failure ?? "Selected save cannot be read.");
            return;
        }

        persistenceMenu.Input(new(down, up, accept, cancel, left, right), persistenceMetadata, HandlePersistence);
        persistenceSelection = persistenceMenu.Selection;
    }

    private void ComposePersistence(Span<ushort> target)
    {
        (persistenceBackground ?? throw new InvalidOperationException("Save/load UI not entered.")).CopyTo(target);
        // The original has no reliable disk-error contract. This explicit native-engine notice is acknowledged before retry.
        ReadOnlyMemory<byte> failure = persistenceMenu.Failure is null ? default : GameText.EncodeDisplay(GameText.Format("ui.persistence.failed", ("message", persistenceMenu.Failure)));
        persistenceRenderer!.Draw(persistenceRenderer.BuildPlan(persistenceMenu.AdvanceLogicalFrame(), persistenceSlots, statusAlternateJournalHeading, failure), target);
    }

    private void HandlePersistence(SaveLoadMenuCommand command)
    {
        try
        {
            switch (command.Kind)
            {
                case SaveLoadMenuEffect.Sound:
                    Sound(command.Value);
                    break;
                case SaveLoadMenuEffect.RefreshSlots:
                    persistenceSlots = PersistenceStore.InspectLibrary(persistenceMenu.Kind == SaveLoadMenuKind.Save);
                    persistenceMetadata = persistenceSlots.Select(slot => slot.Metadata).ToArray();
                    persistenceMenu.SetCount(persistenceSlots.Length);
                    break;
                case SaveLoadMenuEffect.PopMode:
                    if (!modes.TryPop(out var transition))
                    {
                        throw new InvalidDataException("Save/load return mode is absent.");
                    }

                    EnterMode(transition.Mode);
                    break;
                case SaveLoadMenuEffect.RestoreBackground:
                    hasFrame = false;
                    break;
                case SaveLoadMenuEffect.SaveSlot:
                    var row = persistenceSlots[command.Value];
                    SaveSessionSlot(row.IsNew || row.Automatic ? PersistenceStore.NextManualSlot() : row.Index);
                    break;
                case SaveLoadMenuEffect.LoadSlot:
                    LoadSessionSlot(persistenceSlots[command.Value].Index);
                    break;
                default:
                    throw new InvalidDataException("Unknown save/load command.");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException or OverflowException or Atria.Script.ScriptException)
        {
            persistenceMenu.ReportFailure(error.Message);
            Console.WriteLine("Persistence operation rejected: " + error.Message);
        }
    }

    private static SessionMapSnapshot CaptureSessionMap(WorldMapContext value, ReadOnlySpan<byte> member) => new(member.ToArray(), value.GetHeader(), value.GetTiles());
    private SessionRuntimeSnapshot CaptureSessionRuntime(int savedMode, bool exact = false)
    {
        if (dialogueActive || (!exact && (state.ScriptActive != 0 || state.EndingResetFlag != 0)) || moviePrepared)
        {
            throw new NotSupportedException("An active script, battle transition, or movie cannot be snapshotted yet.");
        }

        if ((savedMode == GameMode.World && exploration?.Moving == true) || (savedMode == GameMode.Dungeon && dungeon?.Motion.Moving == true))
        {
            throw new NotSupportedException("Moving-controller absolute clock rebasing is not recovered for snapshots.");
        }

        var current = map ?? throw new InvalidDataException("Current AMF context is missing.");
        int currentKind = ReferenceEquals(current, dungeon?.RuntimeMap.Context) ? 2 : ReferenceEquals(current, explorationEntry?.Map) ? 1 : 0;
        byte[] currentMember = currentKind == 2 ? dungeonEntry!.MapMember.ToArray() : currentKind == 1 ? explorationEntry!.MapMember.ToArray() : Encoding.ASCII.GetBytes($"ground{current.AreaIndex + 1}.amf");
        SessionExplorationSnapshot? world = null;
        if (explorationEntry is { } entry)
        {
            world = new(
                CaptureSessionMap(entry.Map, entry.MapMember.Span),
                entry.PrimaryName.ToArray(),
                entry.SecondaryName.ToArray(),
                entry.ScriptMember.ToArray(),
                exploration?.CaptureRuntime(),
                explorationRenderer?.CaptureRuntime(),
                explorationMapReload,
                explorationTravelSteps,
                explorationRevealX,
                explorationRevealY
            );
        }

        SessionDungeonSnapshot? retainedDungeon = null;
        if (dungeon is { } controller)
        {
            var plan = dungeonEntry ?? throw new InvalidDataException("Retained dungeon resource identity is missing.");
            var composer = dungeonComposer ?? throw new InvalidDataException("Retained dungeon composer is missing.");
            retainedDungeon = new(
                CaptureSessionMap(controller.RuntimeMap.Context, plan.MapMember.Span),
                (int)plan.Source,
                plan.ScriptMember.ToArray(),
                plan.ReturnX,
                plan.ReturnY,
                plan.CaptureArea,
                controller.CaptureRuntime(),
                composer.Raster.CaptureRuntime(),
                composer.DynamicText.ToArray(),
                dungeonReturnMap.ToArray(),
                dungeonReturnTown.ToArray(),
                dungeonReturnTownScript.ToArray(),
                dungeonPadding,
                dungeonHudY,
                dungeonTreasureKind,
                dungeonTreasureAmount,
                dungeonTreasureItem,
                dungeonHudEnabled
            );
        }

        var selected = location is { } loc ? new SessionLocationSnapshot(loc.SceneIndex, loc.SceneMember.ToArray(), loc.ScriptMember.ToArray(), loc.AreaIndex) : null;
        return new SessionRuntimeSnapshot
        {
            SavedMode = savedMode,
            Modes = modes.CaptureRuntime(),
            Random = random.CaptureRuntime(),
            CurrentMap = CaptureSessionMap(current, currentMember),
            CurrentMapKind = currentKind,
            Dialogue = dialogue.CaptureRuntime(),
            Music = music.CaptureRuntime(),
            PlayingMusicTrack = musicTransport.CapturePlayingTrack(),
            Location = selected,
            Town = town.CaptureRuntime(),
            Exploration = world,
            Dungeon = retainedDungeon,
            ScriptMember = scriptMember.ToArray(),
            PendingHandler = pendingHandler.ToArray(),
            TownName = townName.ToArray(),
            ReturnMap = returnMap.ToArray(),
            TownFile = townFile.ToArray(),
            PreviousTown = previousTown,
            MusicId = musicId,
            LabelDelay = labelDelay,
            ObjectActionField = objectActionField,
            EntryTextMode = entryTextMode,
            SelectedEquipmentActor = selectedEquipmentActor,
            AwardMusicTrack = awardMusicTrack,
            SaveDisabled = saveDisabled,
            StatusAlternateJournalHeading = statusAlternateJournalHeading,
            Settings = settings.Encode(),
            FrameThreshold = gate.ThresholdMilliseconds,
            EndingTextPrefix = dungeonEndingTextPrefix.ToArray(),
            MainMenu = menu.CaptureRetained(),
            Credits = credits.CaptureRetained(),
            Defeat = defeat.CaptureRetained(),
            RetainedBattleCounter = retainedBattleCounter,
            RetainedBattleVisualDetail = retainedBattleVisualDetail,
            RetainedBattleRenderPeriod = retainedBattleRenderPeriod,

            ExactResume = exact,
            Script = CaptureScriptCheckpoint(),
            Interior = savedMode is GameMode.Interior or GameMode.Inn or GameMode.Shop ? interior.CaptureRuntime() : null,
            NextBattleSeed = nextBattleSeed,
            ScriptReturnLocation = scriptReturnLocation is { } prior ? new(prior.SceneIndex, prior.SceneMember.ToArray(), prior.ScriptMember.ToArray(), prior.AreaIndex) : null
        };
    }

    private void SaveSessionSlot(int slot)
    {
        int savedMode = modes.LegacySaveMode();
        if (saveDisabled)
        {
            throw new NotSupportedException("Original save gate currently prevents saving.");
        }

        PersistenceStore.WriteNative(slot, CaptureSaveImage(savedMode, true, ""));
    }

    private NativeSaveImage CaptureSaveImage(int savedMode, bool exact, string reason)
    {
        var context = CaptureSessionRuntime(savedMode, exact) with
        {
            SaveReason = reason
        };
        // Native saves capture active world storage before writing, except town400. Prepare a detached area store.
        var capturedAreas = new WorldAreaStore();
        for (int i = 0; i < 8; i++)
        {
            capturedAreas.Import(i, areas.GetInline(i), areas.GetPayload(i));
        }

        if (savedMode == GameMode.World)
        {
            capturedAreas.Capture(map!.AreaIndex, map);
        }

        int area = savedMode is GameMode.Town or GameMode.Interior or GameMode.Inn or GameMode.Shop ? (location?.AreaIndex ?? throw new InvalidDataException("Town area is missing.")) : savedMode == GameMode.Dungeon ? (dungeonEntry?.CaptureArea ?? location?.AreaIndex ?? map!.AreaIndex) : map!.AreaIndex;
        byte[] primary = townName, secondary = capturedAreas.GetInline(area).AsSpan(50, 50).ToArray();
        var metadata = LegacySlotMetadata.Empty([]).WithCurrent(primary, secondary, savedMode, area, town.TownId, state.Party[0].Level, messages[309].Span);
        var stocks = shopStocks ?? throw new InvalidDataException("Full30-shop state has not been initialized.");
        var image = NativeSaveImage.Capture(state, savedMode, capturedAreas, stocks, metadata, SessionRuntimeCodec.Encode(context), persistenceLegacyTemplate);
        return image;
    }

    private void LoadSessionSlot(int slot)
    {
        loadingSnapshot = true;
        try
        {
            var content = PersistenceStore.ReadSlot(slot) ?? throw new InvalidDataException("Selected slot is empty.");
            if (content.Legacy is not null)
            {
                throw new NotSupportedException("Legacy import activation requires authentic fixture verification and the remaining native context fields.");
            }

            var image = content.Native!;
            PreparedSession? prepared = null;
            var loader = new SessionSnapshotLoader(root, state, random, smallFont, messages, items, musicTransport.ValidateTrack);
            // Do not initialize live shops as a side effect of a rejected load.
            var targetShops = shopStocks ?? new ShopStock[30];
            image.CommitStorage(state, areas, targetShops, (context, savedState) =>
            {
                prepared = loader.Prepare(context, savedState);
                InventoryMaintenance.ReconcileBattleSupplies(savedState);
            });
            shopStocks = targetShops;
            persistenceLegacyTemplate = image.LegacyTemplate;
            ApplyPreparedSession(prepared!);
        }
        finally
        {
            loadingSnapshot = false;
        }
    }

    private void ApplyPreparedSession(PreparedSession next)
    {
        var saved = next.Snapshot;
        RestoreSessionFields(next);
        gate.SetThresholdMilliseconds(saved.FrameThreshold);
        random.RestoreRuntime(saved.Random);
        modes.RestoreRuntime(saved.Modes);
        modes.ResetDepth();
        modes.Push(saved.SavedMode);
        state.ModeStackDepth = 0;
        state.Mode = saved.SavedMode;
        actions.CloseAll();
        dialogue.RestoreRuntime(saved.Dialogue);
        townAssets = next.TownAssets;
        town.RestoreRuntime(next.Town, townAssets?.Scene, townAssets?.Objects);
        interiorAssets = next.InteriorAssets;
        trade = null;
        interiorActions = null;
        if (saved.Interior is { } inside)
        {
            interior.RestoreRuntime(inside, interiorAssets!.Scene);
            if (inside.Mode == GameMode.Shop)
            {
                itemSprites = next.ItemSprites;
            }
        }

        RestoreExplorationContexts(next);
        saved.EndingTextPrefix.CopyTo(dungeonEndingTextPrefix, 0);
        menu.RestoreRetained(saved.MainMenu);
        credits.RestoreRetained(saved.Credits);
        defeat.RestoreRetained(saved.Defeat);
        retainedBattleCounter = saved.RetainedBattleCounter;
        retainedBattleVisualDetail = saved.RetainedBattleVisualDetail;
        retainedBattleRenderPeriod = saved.RetainedBattleRenderPeriod;
        // Asset preparation and validation are complete. Audio/UI resources are transient and restart from the recovered track identity.
        StopExclusiveScenarioSound();
        StopAudio();
        RestoreSettingsSnapshot(saved.Settings);
        music.RestoreRuntime(saved.Music);
        if (saved.PlayingMusicTrack > 0)
        {
            musicTransport.Play(saved.PlayingMusicTrack);
        }

        foreach (var command in next.TownEntryCommands)
        {
            HandleTown(command);
        }

        if (saved.SavedMode == GameMode.World && !saved.ExactResume)
        {
            town.SetSavedSelection(0);
            townName = next.WorldEntry!.PrimaryName.ToArray();
            musicId = NativeModeEntry.MusicId(GameMode.World, musicId, next.Map.AreaIndex);
            music.Update(musicId);
        }

        hasFrame = false;
        resumeScript = saved.Script;
        nextBattleSeed = saved.NextBattleSeed;
        scriptReturnLocation = saved.ScriptReturnLocation is { } prior ? new(prior.SceneIndex, prior.SceneMember, prior.ScriptMember, prior.AreaIndex) : null;
        autosavePending = "";
        controls.Clear();
        if (saved.Interior is not null && resumeScript is null && state.ScriptActive == 0)
        {
            InteriorActions.OpenAction();
        }
    }

    private void RestoreSessionFields(PreparedSession next)
    {
        var saved = next.Snapshot;
        map = next.Map;
        location = saved.Location is { } loc ? new(loc.SceneIndex, loc.SceneMember, loc.ScriptMember, loc.AreaIndex) : null;
        scriptMember = saved.ScriptMember;
        pendingHandler = saved.PendingHandler;
        townName = saved.TownName;
        returnMap = saved.ReturnMap;
        townFile = saved.TownFile;
        previousTown = saved.ExactResume ? saved.PreviousTown : 0;
        musicId = saved.MusicId;
        labelDelay = saved.LabelDelay;
        objectActionField = saved.ObjectActionField;
        entryTextMode = saved.EntryTextMode;
        selectedEquipmentActor = saved.SelectedEquipmentActor;
        awardMusicTrack = saved.AwardMusicTrack;
        saveDisabled = saved.ExactResume ? saved.SaveDisabled : false;
        statusAlternateJournalHeading = saved.StatusAlternateJournalHeading;
    }

    private void RestoreExplorationContexts(PreparedSession next)
    {
        var saved = next.Snapshot;
        explorationEntry = next.WorldEntry;
        explorationAssets = next.WorldAssets;
        explorationRenderer = next.WorldRenderer;
        exploration = next.World;
        explorationOriginalAmf = next.WorldRaw;
        explorationMapReload = saved.Exploration?.MapReload ?? false;
        explorationTravelSteps = saved.Exploration?.TravelSteps ?? 0;
        explorationRevealX = saved.Exploration?.RevealX ?? 0;
        explorationRevealY = saved.Exploration?.RevealY ?? 0;
        dungeonEntry = next.DungeonEntry;
        dungeon = next.Dungeon;
        dungeonComposer = next.DungeonComposer;
        if (saved.Dungeon is { } retained)
        {
            dungeonReturnMap = retained.ReturnMap;
            dungeonReturnTown = retained.ReturnTown;
            dungeonReturnTownScript = retained.ReturnTownScript;
            dungeonPadding = retained.Padding;
            dungeonHudY = retained.HudY;
            dungeonTreasureKind = retained.TreasureKind;
            dungeonTreasureAmount = retained.TreasureAmount;
            dungeonTreasureItem = retained.TreasureItem;
            dungeonHudEnabled = retained.HudEnabled;
        }
        else
        {
            dungeonReturnMap = [];
            dungeonReturnTown = [];
            dungeonReturnTownScript = [];
            dungeonPadding = dungeonHudY = dungeonTreasureKind = dungeonTreasureAmount = dungeonTreasureItem = 0;
            dungeonHudEnabled = false;
        }
    }
}
