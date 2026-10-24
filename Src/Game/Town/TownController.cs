using Atria.Render.Codecs;
using Atria.Render.Scenes;
using Atria.Resources;

namespace Atria.Game.Town;

public enum TownEffectKind
{
    Sound,
    SetEntryGlobals,
    SetScriptContext,
    QueueEnterScript,
    SetTownNames,
    SetMusic,
    PlayMusic,
    SetPreviousTown,
    LoadBackground,
    OpenActionMenu,
    SetObjectActionField,
    ReleaseTownSprite,
    RunPreOut,
    RunPreEnter,
    CopyReturnMapReferences,
    LoadReturnMap,
    ResetModeStackAndEnableSave,
    ChangeMode,
    CopyTownToBattleReturn,
    InitializeEncounter,
    CloseActionMenus,
    PushMapReturnMarker,
    SetUiDirty
}

public readonly record struct TownCommand(TownEffectKind Kind, int Value = 0, int Value2 = 0, ReadOnlyMemory<byte> Bytes = default);
public readonly record struct TownInput(bool Next = false, bool Previous = false, bool Accept = false, bool Cancel = false, bool ChooseYes = false, bool ChooseNo = false);
public readonly record struct TownRenderGates(int ActionMenuDepth = -1, int DialogueActive = 0, int LabelDelay = 0)
{
    public TownRenderGates() : this(-1, 0, 0)
    {
    }
}

public sealed record TownFrame(
    int Selection,
    bool DrawObjects,
    bool DrawEffects,
    bool DrawLabel,
    int MotionX,
    int MotionY,
    int LabelDelayAfterDraw,
    bool LeaveConfirmation,
    bool LeaveYes,
    int LeaveFlash,
    int[] EffectFrames
);
public sealed partial class TownController
{
    private SodV2? scene;
    private SpriteFile? sprites;
    private ReadOnlyMemory<byte> loadedSceneName;
    private readonly int[] effectFrames = new int[5], effectCounters = new int[5];
    private int inputDelay, markerCounter, markerPhase, motionX, motionY, leaveFlash;
    private bool pendingPreOut, pendingPreEnter, actionPreOut;
    public bool InputDeferred => inputDelay != 0;
    public int TownId
    {
        get; private set;
    }
    public int Selection
    {
        get; private set;
    }
    public int SavedSelection
    {
        get; private set;
    }
    public bool LeaveConfirmation
    {
        get; private set;
    }
    public bool LeaveYes
    {
        get; private set;
    }

    public void PointerSelect(int index)
    {
        if (!InputDeferred && !LeaveConfirmation && (uint)index < (uint)SodScenePresentation.SelectableCount(Scene))
        {
            Selection = index;
            ResetMarker();
        }
    }

    public SodV2 Scene => scene ?? throw new InvalidOperationException("Town not loaded.");

    public void ReleaseObjectSprites() => sprites = null;
    // Native mode300/330 reset63bc0 before town reload;400 itself retains it.
    public void SetSavedSelection(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        SavedSelection = value;
    }

    public void Enter(
        SodV2 townScene,
        SpriteFile? townSprites,
        int townId,
        int previousTownId,
        ReadOnlyMemory<byte> sceneName,
        ReadOnlyMemory<byte> townName,
        int musicId,
        Action<TownCommand> emit
    )
    {
        if ((uint)townId >= 52)
        {
            throw new ArgumentOutOfRangeException(nameof(townId));
        }

        if (townScene.Count > 0 && townSprites is null)
        {
            throw new InvalidDataException("Town object SPR is required on entry.");
        }

        int selectable = SodScenePresentation.SelectableCount(townScene);
        if (townScene.Count > 0 && (selectable < 1 || SavedSelection >= selectable))
        {
            throw new InvalidDataException("Town selected index is outside selectable records.");
        }

        scene = townScene;
        sprites = townSprites;
        TownId = townId;
        LeaveConfirmation = LeaveYes = false;
        loadedSceneName = sceneName;
        pendingPreOut = pendingPreEnter = false;
        emit(new(TownEffectKind.SetEntryGlobals, 4, Bytes: sceneName));
        bool resetEffects = false;
        foreach (var effect in TownEffectCatalog.Records)
        {
            if (effect.Town == townId && effect.Slot == 0)
            {
                resetEffects = true;
            }
        }

        if (resetEffects)
        {
            Array.Clear(effectFrames);
            Array.Clear(effectCounters);
        }

        if (townScene.Count < 1)
        {
            SavedSelection = Selection = 0;
            ResetMarker();
        }
        else
        {
            _ = SodScenePresentation.BuildDepthOrder(townScene, townSprites!);
            Selection = SavedSelection;
            ResetMarker();
        }

        emit(new(TownEffectKind.SetScriptContext, Bytes: townScene.ScriptReference));
        if (previousTownId != townId)
        {
            emit(new(TownEffectKind.SetPreviousTown, townId));
            emit(new(TownEffectKind.QueueEnterScript, townId, Bytes: "enter"u8.ToArray()));
        }

        inputDelay = 1;
        emit(new(TownEffectKind.SetTownNames, Bytes: townName));
        emit(new(TownEffectKind.SetMusic, musicId));
        emit(new(TownEffectKind.LoadBackground, Bytes: townScene.BackgroundReference));
        emit(new(TownEffectKind.PlayMusic, musicId));
        // mode400 calls music after the load, not from41cfb0 itself.
    }

    public void Input(TownInput input, bool encounterPending, Action<TownCommand> emit)
    {
        _ = Scene;
        if (inputDelay != 0)
        {
            inputDelay--;
            return;
        }

        if (LeaveConfirmation)
        {
            LeaveInput(input, emit);
            return;
        }

        if (encounterPending)
        {
            emit(new(TownEffectKind.ReleaseTownSprite));
            emit(new(TownEffectKind.SetPreviousTown, 0));
            emit(new(TownEffectKind.CopyTownToBattleReturn, Bytes: loadedSceneName));
            emit(new(TownEffectKind.InitializeEncounter));
            emit(new(TownEffectKind.ChangeMode, 330));
            return;
        }

        if (Scene.Count > 0)
        {
            int selectable = SodScenePresentation.SelectableCount(Scene);
            if (input.Next)
            {
                Navigate(1, selectable, emit);
            }

            if (input.Previous)
            {
                Navigate(selectable - 1, selectable, emit);
            }
        }

        if (input.Accept)
        {
            emit(new(TownEffectKind.Sound, 3));
            if (Scene.Count < 1)
            {
                emit(new(TownEffectKind.OpenActionMenu, 3, 66));
            }
            else
            {
                emit(new(TownEffectKind.SetObjectActionField, Scene.GetObject(Selection).GetInteger(14)));
                emit(new(TownEffectKind.OpenActionMenu, 5, 0));
            }

            return;
        }

        if (input.Cancel)
        {
            emit(new(TownEffectKind.Sound, 3));
            LeaveConfirmation = LeaveYes = true;
        }
    }

    private void Navigate(int delta, int selectable, Action<TownCommand> emit)
    {
        emit(new(TownEffectKind.Sound, 1));
        Selection = (Selection + delta) % selectable;
        SavedSelection = Selection;
        ResetMarker();
    }

    private void ResetMarker()
    {
        markerCounter = markerPhase = motionX = motionY = 0;
    }

    private void LeaveInput(TownInput input, Action<TownCommand> emit)
    {
        if (input.ChooseYes)
        {
            if (!LeaveYes)
            {
                emit(new(TownEffectKind.Sound, 1));
                LeaveYes = true;
                leaveFlash = 2;
            }

            return;
        }

        if (input.ChooseNo)
        {
            if (LeaveYes)
            {
                emit(new(TownEffectKind.Sound, 1));
                LeaveYes = false;
                leaveFlash = 2;
            }

            return;
        }

        if (input.Accept)
        {
            emit(new(TownEffectKind.Sound, 3));
            LeaveConfirmation = false;
            if (LeaveYes)
            {
                pendingPreOut = true;
                actionPreOut = false;
                emit(new(TownEffectKind.RunPreOut, Bytes: Scene.ScriptReference));
            }
        }
        else if (input.Cancel)
        {
            emit(new(TownEffectKind.Sound, 4));
            LeaveConfirmation = false;
        }
    }

    public void CompletePreOut(bool allowed, Action<TownCommand> emit, int currentMode = 400)
    {
        if (!pendingPreOut)
        {
            throw new InvalidOperationException("No pending pre_out.");
        }

        pendingPreOut = false;
        if (!allowed)
        {
            return;
        }

        emit(new(TownEffectKind.ReleaseTownSprite));
        if (actionPreOut && currentMode == 1100)
        {
            return;
        }

        emit(new(TownEffectKind.CopyReturnMapReferences, Bytes: HeaderString(81)));
        emit(new(TownEffectKind.LoadReturnMap, Bytes: HeaderString(81)));
        emit(new(TownEffectKind.ResetModeStackAndEnableSave));
        emit(new(TownEffectKind.ChangeMode, 300));
        if (actionPreOut)
        {
            emit(new(TownEffectKind.SetPreviousTown, 0));
        }
    }

    //0x4283e0 action0: callback can change mode during pre_enter; never enter after game-over.
    public void ChooseAction(int action, Action<TownCommand> emit)
    {
        int count = Scene.Count > 0 ? 4 : 3;
        if ((uint)action >= (uint)count)
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        if (Scene.Count < 1)
        {
            action++; //66 handler428470 omits object-enter choice.
        }

        if (action == 0)
        {
            emit(new(TownEffectKind.CloseActionMenus));
            pendingPreEnter = true;
            emit(new(TownEffectKind.RunPreEnter, Bytes: Scene.GetObject(Selection).GetString(1)));
            return;
        }

        if (action == 1)
        {
            emit(new(TownEffectKind.CloseActionMenus));
            pendingPreOut = true;
            actionPreOut = true;
            emit(new(TownEffectKind.RunPreOut, 1, Bytes: Scene.ScriptReference));
            return;
        }

        emit(new(TownEffectKind.PushMapReturnMarker, 320));
        emit(new(TownEffectKind.SetUiDirty, 0));
        emit(new(TownEffectKind.ChangeMode, action == 2 ? 500 : 200));
    }

    public void CompletePreEnter(bool allowed, int currentMode, Action<TownCommand> emit)
    {
        if (!pendingPreEnter)
        {
            throw new InvalidOperationException("No pending pre_enter.");
        }

        pendingPreEnter = false;
        if (!allowed || currentMode == 1100)
        {
            return;
        }

        emit(new(TownEffectKind.ReleaseTownSprite));
        emit(new(TownEffectKind.ResetModeStackAndEnableSave));
        int kind = Scene.GetObject(Selection).GetInteger(7);
        if (kind is >= 0 and <= 2)
        {
            emit(new(TownEffectKind.ChangeMode, 410 + kind * 10));
        }
    }

    private ReadOnlyMemory<byte> HeaderString(int offset)
    {
        var data = Scene.Header.Slice(offset, 13);
        return data[..RawBytes.NullTerminated(data.Span).Length];
    }

    public TownFrame AdvanceLogicalFrame(TownRenderGates gates)
    {
        bool objects = Scene.Count > 0 && sprites is not null;
        bool effects = Scene.Count < 1 || objects;
        bool label = objects && gates.ActionMenuDepth < 0 && gates.DialogueActive == 0 && gates.LabelDelay < 1 && !LeaveConfirmation;
        var frame = new TownFrame(
            Selection,
            objects,
            effects,
            label,
            motionX,
            motionY,
            objects && !label ? unchecked(gates.LabelDelay - 1) : gates.LabelDelay,
            LeaveConfirmation,
            LeaveYes,
            leaveFlash,
            (int[])effectFrames.Clone()
        );
        if (effects)
        {
            TownEffectCatalog.Advance(TownId, effectFrames, effectCounters);
        }

        if (objects && ++markerCounter > 1)
        {
            markerCounter = 0;
            motionX++;
            motionY--;
            if (++markerPhase >= 9)
            {
                ResetMarker();
            }
        }

        if (LeaveConfirmation && leaveFlash != 0)
        {
            leaveFlash--;
        }

        return frame;
    }
}
