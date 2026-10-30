using System.Buffers.Binary;
using Atria.Game.ActionMenu;
using Atria.Game.Dungeon;
using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    private DungeonEntryPlan? dungeonEntry;
    private LocationTitle dungeonTitle;
    private DungeonController? dungeon;
    private DungeonComposer? dungeonComposer;
    private byte[] dungeonReturnMap = [], dungeonReturnTown = [], dungeonReturnTownScript = [];
    //464020 is retained ending-text storage; termination211d0 updates these aliased first12 bytes.
    private readonly byte[] dungeonEndingTextPrefix = new byte[12];
    private const bool InitialDungeonHudVisible = true; //immutable EXE DWORD44d5a0=1.
    private int dungeonPadding = 0; //464014 BSS: no writes in complete export; retained, never replaced with a gameplay value.
    private int dungeonHudY, dungeonTreasureKind, dungeonTreasureAmount, dungeonTreasureItem;
    private bool dungeonHudEnabled;
    private bool DungeonHudVisible => dungeon?.HudVisible ?? false;

    private void StartDungeonFromWorld(bool scriptOverride = false)
    {
        PrepareDungeon(scriptOverride);
        modes.Push(GameMode.Dungeon);
        EnterMode(GameMode.Dungeon);
    }

    //Town InitializeEncounter calls this BEFORE its existing ChangeMode330 command.
    private void StartDungeonFromTown() => PrepareDungeon(false);
    private void PrepareDungeon(bool scriptOverride)
    {
        var current = map ?? throw new InvalidOperationException("Dungeon entry requires source AMF context.");
        var plan = DungeonEntry.Prepare(state, current, scriptOverride);
        if (plan.CaptureArea is { } area)
        {
            areas.Capture(area, current);
        }

        dungeonReturnMap = returnMap.ToArray();
        dungeonReturnTown = townFile.ToArray();
        dungeonReturnTownScript = scriptMember.ToArray();
        byte[] raw = Read("AT2AMF.000", plan.MapMember.Span);
        var decoded = AmfMap.Parse(raw);
        var loaded = WorldMapContext.Parse(raw);
        var nextMap = new DungeonMap(loaded);
        var assets = DungeonSceneAssets.Load(root, loaded, raw);
        var functionMember = DungeonEntry.ResolveScript(plan, decoded);
        if (functionMember.IsEmpty)
        {
            throw new InvalidDataException("Dungeon entry lacks an actual script member.");
        }

        int ring = dungeon?.Visibility.NextTorch ?? 0;
        int[] scratch = dungeon?.Visibility.GetScratchNeighbors() ?? new int[9];
        int cost = dungeon?.TorchCost ?? 0, adjust = dungeon?.AdjustmentCounter ?? 0;
        bool hudVisible = dungeon?.HudVisible ?? InitialDungeonHudVisible;
        DungeonTorch[] torchBank = dungeon?.Visibility.GetTorchBank() ?? new DungeonTorch[20];
        byte[] retainedMessage = dungeonComposer?.DynamicText.ToArray() ?? [];
        if (nextMap.FindEntrance() is { } entrance)
        {
            state.WorldX = entrance.X;
            state.WorldY = entrance.Y;
        }

        map = loaded;
        dungeonEntry = plan;
        scriptMember = functionMember.ToArray();
        state.ScriptActive = 0;
        pendingHandler = [];
        modes.ResetDepth();
        state.ModeStackDepth = -1;
        saveDisabled = false;
        dungeon = new(state, nextMap, assets.Base.CharacterAnimation, random, hudVisible, ring, scratch, cost, adjust, torchBank, dungeonTreasureKind);
        dungeonComposer = new(
            state,
            dungeon,
            nextMap,
            assets,
            smallFont,
            displayMessages,
            GameText.ItemNames(items),
            retainedMessage
        );
        dungeonTitle = LocationTitle.Dungeon(decoded);
        townName = messages[dungeonTitle.MessageId].ToArray();
    }

    private void EnterDungeon()
    {
        if (dungeon is null || dungeonComposer is null)
        {
            throw new InvalidOperationException("330 entry requires native dungeon preparation.");
        }

        town.SetSavedSelection(0);
        //428f00->428ea0->41eae0 refreshes raw ground/SOD/fog on EVERY330 entry, including status return.
        dungeonComposer.ApplyRasterCommand(new(DungeonCommandKind.RebuildObjects));
        musicId = NativeModeEntry.MusicId(GameMode.Dungeon, musicId, 0, random.Next);
        music.Update(musicId);
    }

    private void HandleDungeonInput()
    {
        var current = dungeon ?? throw new InvalidOperationException("Dungeon controller absent.");
        var input = new DungeonBindings(bindings.Previous, bindings.Next, bindings.Up, bindings.Down, bindings.Confirm, bindings.Cancel, bindings[0, 7]);
        foreach (var command in current.Input(input, Query, host.IsHeld))
        {
            HandleDungeonCommand(command);
        }
    }

    private void HandleDungeonCommand(DungeonCommand command)
    {
        switch (command.Kind)
        {
            case DungeonCommandKind.Sound:
                Sound(command.Value);
                break;
            case DungeonCommandKind.RebuildObjects:
                saveDisabled = dungeon!.SaveGate;
                dungeonComposer!.ApplyRasterCommand(command);
                break;
            case DungeonCommandKind.RefreshVisibility:
                dungeonComposer!.ApplyRasterCommand(command);
                break;
            case DungeonCommandKind.RunPendingScript:
                pendingHandler = dungeon!.PendingFunction.ToArray();
                break;
            case DungeonCommandKind.Leave:
                LeaveDungeon();
                break;
            case DungeonCommandKind.OpenActionMenu:
                actions.OpenDungeon(dungeon!.ActionTileIsSwitch, displayMessages);
                break;
            case DungeonCommandKind.ToggleHud:
            case DungeonCommandKind.WalkStarted:
                break; //Controller owns completed flag/movement mutation.
            case DungeonCommandKind.Arrival:
                var effect = command.Arrival ?? throw new InvalidDataException("Dungeon arrival command lacks effect.");
                if (effect.Kind == DungeonArrivalEffectKind.TreasureMoney)
                {
                    dungeonTreasureKind = 1;
                }
                else if (effect.Kind == DungeonArrivalEffectKind.TreasureItem)
                {
                    dungeonTreasureKind = 2;
                    dungeonTreasureItem = effect.Value;
                    dungeonTreasureAmount = effect.Value2;
                }
                else if (effect.Kind == DungeonArrivalEffectKind.TreasureTorch)
                {
                    dungeonTreasureKind = 2;
                    dungeonTreasureAmount = effect.Value;
                }

                //BattlePending is actual shared state mutation; normal dispatcher must invoke the combat subsystem next.
                break;
            default:
                throw new InvalidDataException("Unknown dungeon command.");
        }
    }

    private void ComposeDungeon(Span<ushort> target)
    {
        (dungeonComposer ?? throw new InvalidOperationException("Dungeon composer absent.")).Compose(target, () => gate.LastFrameMilliseconds, ReadDungeonVisibilityAlias, dungeonHudY, dungeonHudEnabled, HandleDungeonCommand);
    }

    private int ReadDungeonVisibilityAlias(int index)
    {
        var current = dungeon ?? throw new InvalidOperationException("Dungeon absent.");
        var entry = dungeonEntry ?? throw new InvalidOperationException("Dungeon source absent.");
        return DungeonVisibilityAliases.Read(
            index,
            current,
            entry.ReturnX,
            entry.ReturnY,
            dungeonComposer!.Raster.TorchAnimationCounter,
            dungeonTreasureKind,
            dungeonTreasureAmount,
            dungeonTreasureItem,
            dungeonPadding,
            current.ActionTileIsSwitch ? 1 : 0,
            current.AdjustmentCounter,
            dungeonEndingTextPrefix
        );
    }

    private void RecordDungeonHudContext(GameState value, bool modal, int townAnchor)
    {
        int? origin = WorldHudRenderer.OriginY(value.Mode, value.WorldY, modal, townAnchor, dungeonHudEnabled: DungeonHudVisible);
        dungeonHudY = origin ?? 420;
        dungeonHudEnabled = origin.HasValue;
    }

    private void CopyDungeonStaticBackdrop(Span<ushort> target) => (dungeonComposer ?? throw new InvalidOperationException("Dungeon composer absent.")).Raster.CopyStaticBackdrop(target);
    private bool TryHandleDungeonAction(ActionMenuCommand command)
    {
        if (command.Kind != ActionMenuEffect.InvokeChoice || command.MenuType != 9)
        {
            return false;
        }

        var current = dungeon ?? throw new InvalidOperationException("Dungeon absent.");
        if (command.Value == 0)
        {
            actions.CloseAll();
            state.Mode = GameMode.Dungeon;
            current.OpenTorch();
        }
        else if (command.Value == 1)
        {
            modes.Push(GameMode.WorldOverlay);
            actions.SetRenderEnabled(false);
            modes.Push(GameMode.Status);
            EnterMode(GameMode.Status);
        }
        else if (command.Value == 2)
        {
            actions.CloseAll();
            actions.SetRenderEnabled(false);
            current.OpenSwitch();
        }
        else
        {
            throw new InvalidDataException("Native dungeon action has no handler for row.");
        }

        return true;
    }

    private void LeaveDungeon()
    {
        var entry = dungeonEntry ?? throw new InvalidOperationException("Dungeon return source absent.");
        Sound(2);
        state.WorldX = entry.ReturnX;
        state.WorldY = entry.ReturnY;
        saveDisabled = false;
        modes.ResetDepth();
        state.ModeStackDepth = -1;
        if (entry.Source == DungeonEntrySource.TownScript)
        {
            state.DungeonEntryPending = 0;
            townFile = dungeonReturnTown.ToArray();
            scriptMember = dungeonReturnTownScript.ToArray();
            modes.Push(GameMode.Town);
            EnterMode(GameMode.Town);
        }
        else
        {
            LoadExplorationReturnMap(dungeonReturnMap);
            modes.Push(GameMode.World);
            EnterMode(GameMode.World);
            exploration!.RefreshPending = true;
        }
    }
}
