using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Atria.Game.ActionMenu;
using Atria.Game.Exploration;
using Atria.Game.Persistence;
using Atria.Game.World;

namespace Atria.Game;

public sealed partial class GameSession
{
    // All later battle/music RNG paths must share this exact instance/call order. New game does not reseed it.
    private readonly LegacyRandom random = new(LegacyRandom.ProcessInitialSeed);
    private WorldMapEntry? explorationEntry;
    private WorldSceneAssets? explorationAssets;
    private WorldSceneRenderer? explorationRenderer;
    private ExplorationController? exploration;
    private byte[] explorationOriginalAmf = [];
    private bool explorationMapReload;
    private int explorationTravelSteps, explorationRevealX, explorationRevealY;
    private QuestMapIndex? questMap;
    private WorldRevealPose? questMarker;
    private void ResetExplorationSession()
    {
        explorationEntry = null;
        explorationAssets = null;
        explorationRenderer = null;
        exploration = null;
        explorationOriginalAmf = [];
        explorationMapReload = false;
        questMarker = null;
        explorationTravelSteps = 0;
        explorationRevealX = explorationRevealY = 0;
    }

    //HandleTown.LoadReturnMap calls this BEFORE its later ResetModeStack/ChangeMode300 commands.
    private void LoadExplorationReturnMap(ReadOnlyMemory<byte> member)
    {
        explorationTravelSteps = exploration?.TravelSteps ?? explorationTravelSteps;
        byte[] loaded = [];
        var entry = WorldMapBoundary.Load(member, areas, key => loaded = Read("AT2AMF.000", key.Span));
        map = entry.Map;
        explorationEntry = entry;
        explorationOriginalAmf = loaded;
        explorationMapReload = true;
        scriptMember = entry.ScriptMember.ToArray();
        townName = entry.PrimaryName.ToArray();
        returnMap = entry.MapMember.ToArray();
        state.ChangedChip = 0;
    }

    private void EnterExploration(int mode)
    {
        if (mode is not (GameMode.World or GameMode.WorldEntry))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (mode == GameMode.WorldEntry)
        {
            BeginExplorationReveal();
            return;
        }

        if (explorationEntry is null)
        {
            if (returnMap.Length == 0)
            {
                throw new InvalidOperationException("Mode300 entry lacks the exact current AMF resource identity.");
            }

            LoadExplorationReturnMap(returnMap);
        }

        var currentMap = map ?? throw new InvalidOperationException("World map not loaded.");
        if (exploration is null || explorationMapReload)
        {
            explorationAssets = WorldSceneAssets.Load(root, currentMap, explorationOriginalAmf);
            explorationRenderer = new(explorationAssets);
            exploration = new(
                state,
                currentMap,
                explorationAssets.CharacterAnimation,
                random,
                explorationRenderer.IsSceneReferenceBlocked,
                WorldDirection.Down,
                explorationTravelSteps
            )
            {
                InputDelay = 1
            };
            explorationMapReload = false;
        }

        //428f00/428ea0 mode300: clear town saved selection; refresh arrival visibility+cached ground.
        town.SetSavedSelection(0);
        exploration.InputDelay = 2; //428ea0 resets this guard on every300 entry, including status/menu return.
        ExplorationRules.MarkArrival(currentMap, state.WorldX, state.WorldY);
        explorationRenderer!.RebuildBase(currentMap, state);
        townName = (explorationEntry ?? throw new InvalidOperationException("World entry identity was lost.")).PrimaryName.ToArray();
        musicId = NativeModeEntry.MusicId(GameMode.World, musicId, currentMap.AreaIndex);
        music.Update(musicId);
    }

    private void BeginExplorationReveal()
    {
        if (exploration is null || explorationRenderer is null)
        {
            throw new InvalidOperationException("Reveal requires the loaded world controller.");
        }

        explorationRevealX = state.WorldX;
        explorationRevealY = state.WorldY;
        explorationRenderer.ResetRevealAnimation();
        state.Mode = GameMode.WorldEntry;
        // Native directly changes current mode; stack content/depth remain the underlying300 entry.
    }

    private void FinishExplorationReveal()
    {
        modes.ResetDepth();
        modes.Push(GameMode.World);
        state.Mode = GameMode.World;
        state.ModeStackDepth = 0;
        //41bdc0 does not execute mode300 entry, reset ANI timing, reset save gate, or update music.
    }

    private void HandleExplorationInput()
    {
        var controller = exploration ?? throw new InvalidOperationException("World controller not loaded.");
        if (state.Mode == GameMode.WorldEntry)
        {
            if (Query(bindings.Previous))
            {
                if (explorationRevealX > 0)
                {
                    explorationRevealX--;
                }

                return;
            }

            if (Query(bindings.Next))
            {
                if (explorationRevealX < 9)
                {
                    explorationRevealX++;
                }

                return;
            }

            if (Query(bindings.Up))
            {
                if (explorationRevealY > 0)
                {
                    explorationRevealY--;
                }

                return;
            }

            if (Query(bindings.Down))
            {
                if (explorationRevealY < 9)
                {
                    explorationRevealY++;
                }

                return;
            }

            if (Query(bindings.Confirm))
            {
                var current = map!;
                if (!ExplorationRules.CanReveal(current, state.WorldX, state.WorldY, explorationRevealX, explorationRevealY))
                {
                    return;
                }

                if (state.ResourceCounterDc88 < 1)
                {
                    OpenExplorationMessage(88);
                }
                else
                {
                    ExplorationRules.Reveal(current, state, explorationRevealX, explorationRevealY);
                    Sound(2);
                    ExplorationRules.MarkArrival(current, state.WorldX, state.WorldY);
                    explorationRenderer!.RebuildBase(current, state);
                }

                FinishExplorationReveal();
                return; //Even insufficient-resource message returns current mode to300.
            }

            if (Query(bindings.Cancel))
            {
                FinishExplorationReveal();
            }

            return;
        }

        // Do not query lower-priority key edge latches when native41ba20 returns through a state gate.
        ExplorationInput input = default;
        if (controller.InputDelay == 0 && state.ChangedChip == 0 && !(state.DungeonEntryPending != 0 && state.ScriptActive == 0)
&& !controller.RefreshPending && !controller.Moving)
        {
            bool left = host.IsHeld(bindings.Previous), right = !left && host.IsHeld(bindings.Next);
            bool up = !left && !right && host.IsHeld(bindings.Up), down = !left && !right && !up && host.IsHeld(bindings.Down);
            bool confirm = false, cancel = false;
            if (!left && !right && !up && !down)
            {
                confirm = Query(bindings.Confirm);
                if (!confirm)
                {
                    cancel = Query(bindings.Cancel);
                }
            }

            input = new(left, right, up, down, confirm, cancel);
        }

        switch (controller.HandleInput(input))
        {
            case ExplorationInputEffect.None:
                break;
            case ExplorationInputEffect.TravelStartedPlayWalk12:
                Sound(12);
                break;
            case ExplorationInputEffect.RevealMode310ClearDirection:
                host.ClearScanLatch(input.Left ? bindings.Previous : input.Right ? bindings.Next : input.Up ? bindings.Up : bindings.Down);
                BeginExplorationReveal();
                break;
            case ExplorationInputEffect.RefreshWorld:
                explorationRenderer!.RebuildBase(map!, state);
                break;
            case ExplorationInputEffect.WorldTransitionPending:
                TransitionExplorationMap();
                break;
            case ExplorationInputEffect.EnterDungeon330:
                StartDungeonFromWorld(true);
                break;
            case ExplorationInputEffect.ActionDialogue:
                actions.OpenWorld(map!, state, explorationRenderer!.IsSceneReferenceBlocked, displayMessages);
                break;
            case ExplorationInputEffect.Map500With300Sentinel:
                modes.Push(GameMode.World);
                modes.Push(GameMode.Status);
                EnterMode(GameMode.Status);
                break;
            default:
                throw new InvalidDataException("Unknown exploration input effect.");
        }
    }

    private void TransitionExplorationMap()
    {
        byte[] loaded = [];
        var transition = WorldMapBoundary.PrepareTransition(state, map!, areas, key => loaded = Read("AT2AMF.000", key.Span));
        map = transition.Entry.Map;
        explorationEntry = transition.Entry;
        explorationOriginalAmf = loaded;
        explorationTravelSteps = 0;
        exploration = null;
        explorationMapReload = true;
        scriptMember = transition.Entry.ScriptMember.ToArray();
        townName = transition.Entry.PrimaryName.ToArray();
        returnMap = transition.Entry.MapMember.ToArray();
        WorldMapBoundary.ApplyCoordinatesAndNativeResets(transition, state);
        saveDisabled = false;
        modes.ResetDepth();
        modes.Push(GameMode.World);
        EnterMode(GameMode.World);
        RequestAutosave(AutosaveReason.WorldArea);
    }

    private void ComposeExploration(Span<ushort> target)
    {
        var controller = exploration ?? throw new InvalidOperationException("World controller not loaded.");
        var renderer = explorationRenderer ?? throw new InvalidOperationException("World renderer not loaded.");
        WorldRevealPose? cursor = state.Mode == GameMode.WorldEntry ? renderer.AdvanceReveal(map!, state.WorldX, state.WorldY, explorationRevealX, explorationRevealY) : null;
        //Both native tick observations use the scheduled logical clock, never the GPU refresh/wall-time catch-up instant.
        var step = controller.AdvanceAnimation(() => gate.LastFrameMilliseconds);
        questMarker = null;
        if (state.Mode == GameMode.World && state.Journal.ActiveQuest(state).HasValue)
        {
            questMap ??= new(root);
            if (questMap.Marker(state, map!.AreaIndex) is { } point)
            {
                const int revealMarkerFrame = 38, markerFrames = 10, markerFrameMilliseconds = 100;
                int tileWidth = BinaryPrimitives.ReadInt32LittleEndian(map.HeaderBytes[21..]);
                int tileHeight = BinaryPrimitives.ReadInt32LittleEndian(map.HeaderBytes[25..]);
                int frame = revealMarkerFrame + (int)(gate.LastFrameMilliseconds / markerFrameMilliseconds % markerFrames);
                questMarker = new(point.X * tileWidth, point.Y * tileHeight, frame);
            }
        }
        renderer.Draw(step.Pose, cursor, target, questMarker);
        if (step.PlayWalkSound)
        {
            Sound(12);
        }

        if (step.Effect is { } effect)
        {
            HandleExplorationEffect(effect);
        }
        //41c220 arrival marks visibility, but does NOT invoke41b980 cached-background rebuild.
    }

    private void CopyExplorationStaticBackdrop(Span<ushort> target) => (explorationRenderer ?? throw new InvalidOperationException("World renderer not loaded.")).CopyStaticBackdrop(target);
    private void OpenExplorationMessage(int type)
    {
        if (state.Mode == GameMode.World)
        {
            actions.OpenWorldMessage(type, displayMessages, map, state, explorationRenderer!.IsSceneReferenceBlocked);
        }
        else
        {
            actions.OpenWorldMessage(type, displayMessages);
        }
    }

    private void HandleExplorationEffect(ExplorationEffect effect)
    {
        switch (effect.Kind)
        {
            case ExplorationEffectKind.RunCurrentScriptFunction:
                pendingHandler = effect.Function.ToArray();
                break;
            case ExplorationEffectKind.RunAreaFunction:
                scriptMember = effect.Member.ToArray();
                pendingHandler = effect.Function.ToArray();
                break;
            case ExplorationEffectKind.StartEventScript:
                if ((uint)effect.Value >= Atria.Game.State.GameState.EventStatusCount)
                {
                    throw new InvalidDataException("World subevent exceeds status bank.");
                }

                RunScript("subevent.scr"u8, Encoding.ASCII.GetBytes("suba_" + effect.Value.ToString("D3", CultureInfo.InvariantCulture)));
                break;
            case ExplorationEffectKind.Message:
                OpenExplorationMessage(effect.Value);
                break;
            case ExplorationEffectKind.EnterDungeon330:
                StartDungeonFromWorld();
                break;
            case ExplorationEffectKind.BattleQueued:
                break; //Shared request is serviced by the outer dispatcher after arrival.
            default:
                throw new InvalidDataException("Unknown exploration arrival effect.");
        }
    }

    private bool TryHandleExplorationAction(ActionMenuCommand command)
    {
        if (command.Kind != ActionMenuEffect.InvokeChoice || command.MenuType is not (1 or 2 or 77 or 99))
        {
            return false;
        }

        int choice = command.Value;
        if (command.MenuType == 1)
        {
            if (choice == 0)
            {
                actions.CloseAll();
                BeginExplorationReveal();
            }
            else if (choice is 1 or 2)
            {
                modes.Push(GameMode.WorldOverlay);
                actions.SetRenderEnabled(false);
                int mode = choice == 1 ? 500 : 200;
                modes.Push(mode);
                EnterMode(mode);
            }
            else if (choice == 3)
            {
                actions.CloseAll();
                TryEnterExplorationTown(true);
            }
            else
            {
                throw new InvalidDataException("Native world menu has no handler for this row.");
            }

            return true;
        }

        if (command.MenuType == 2)
        {
            if (choice == 0)
            {
                actions.CloseTop();
                if (ExplorationRules.CanReveal(map!, state.WorldX, state.WorldY, explorationRevealX, explorationRevealY))
                {
                    Sound(2); //427fa0 plays2 before41b570, which plays2 again on success.
                    if (state.ResourceCounterDc88 < 1)
                    {
                        OpenExplorationMessage(88);
                    }
                    else
                    {
                        ExplorationRules.Reveal(map!, state, explorationRevealX, explorationRevealY);
                        Sound(2);
                    }

                    ExplorationRules.MarkArrival(map!, state.WorldX, state.WorldY);
                    explorationRenderer!.RebuildBase(map!, state);
                }
            }
            else if (choice is 1 or 2)
            {
                modes.Push(GameMode.WorldOverlay);
                actions.SetRenderEnabled(false);
                int mode = choice == 1 ? 500 : 200;
                modes.Push(mode);
                EnterMode(mode);
            }
            else
            {
                throw new InvalidDataException("Invalid reveal menu choice.");
            }

            return true;
        }

        if (choice is not (0 or 1))
        {
            throw new InvalidDataException("Invalid world yes/no choice.");
        }

        actions.CloseAll();
        if (choice == 1)
        {
            return true;
        }

        if (command.MenuType == 77)
        {
            StartDungeonFromWorld();
            return true;
        }

        TryEnterExplorationTown(false);
        return true;
    }

    private void TryEnterExplorationTown(bool testNativeSceneGate)
    {
        var current = map ?? throw new InvalidOperationException("World map not loaded.");
        var tile = current.TileBytes.Slice((state.WorldY * 10 + state.WorldX) * 328, 328);
        bool blocked = explorationRenderer!.IsSceneReferenceBlocked(BinaryPrimitives.ReadInt32LittleEndian(tile[92..]))
&& BinaryPrimitives.ReadInt32LittleEndian(tile[8..]) != 0;
        if (testNativeSceneGate && !blocked)
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(tile[24..]) == 1)
            {
                StartDungeonFromWorld();
            }

            return;
        }

        var previous = location;
        location = current.SelectScene(state.WorldX, state.WorldY);
        state.EnterPermission = 0;
        scriptReturnLocation = previous;
        RunScript(location.ScriptMember.Span, "pre_enter"u8, 5, testNativeSceneGate ? 1 : 0);
        CompleteWorldPreEnter(testNativeSceneGate, previous);
    }

    private void CompleteWorldPreEnter(bool testNativeSceneGate, WorldSceneReference? previous)
    {
        var current = map ?? throw new InvalidOperationException("World map not loaded.");
        if (state.EnterPermission == 0)
        {
            location = previous;
            return;
        }

        if (testNativeSceneGate && state.Mode == GameMode.Defeat)
        {
            return; //427d70 guard;4284e0(message99) has no such guard.
        }

        areas.Capture(current.AreaIndex, current);
        location = current.SelectScene(state.WorldX, state.WorldY);
        modes.ResetDepth();
        saveDisabled = false;
        modes.Push(GameMode.Town);
        EnterMode(GameMode.Town);
    }
}
