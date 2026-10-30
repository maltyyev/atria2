using Atria.Game.Navigation;
using System.Diagnostics;
using System.Text;
using Atria.Audio;
using Atria.Game.ActionMenu;
using Atria.Game.Controls;
using Atria.Game.Dialogue;
using Atria.Game.Exploration;
using Atria.Game.Menu;
using Atria.Game.Persistence;
using Atria.Game.Progression;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Game.World;
using Atria.Platform.Win32;
using Atria.Platform;
using Atria.Render.Codecs;
using Atria.Render.Scenes;
using Atria.Render;
using Atria.Resources;
using Atria.Script;
using Atria.Video;

namespace Atria.Game;

/// <summary>Shared native session. Unimplemented modes/effects fail explicitly at their actual boundary.</summary>
public sealed partial class GameSession : IScriptEffects
{
    private readonly string root;
    private readonly string sessionOutputDirectory;
    private readonly NativeWindow window;
    private readonly D3D11Presenter presenter;
    private readonly AudioDevice audio;
    private readonly FileMusicTransport musicTransport;
    private readonly MusicController music;
    private readonly SoundEffectBank[] effects;
    private readonly ControlBindings bindings;
    private readonly ModalFrameGate gate = new(new MonotonicModalClock(), 0);
    private readonly NativeDialogueFrameHost host;
    private readonly MenuNavigation menuNavigation;
    private readonly DialogueRuntime dialogue;
    private readonly ItemCatalog items;
    private readonly IReadOnlyList<ReadOnlyMemory<byte>> messages;
    private readonly IReadOnlyList<ReadOnlyMemory<byte>> displayMessages;
    private readonly SpriteFile etc, effectSprites;
    private readonly AnimationFile effectAnimation;
    private readonly BitmapFont smallFont;
    private readonly WorldHudRenderer hud;
    private readonly ushort[] menuBackground;
    private readonly MainMenuModalRenderer menuModal;
    private readonly TownLeaveModalRenderer leaveModal;
    private readonly ActionMenuRenderer actionRenderer;
    private readonly MainMenuController menu = new();
    private readonly TownController town = new();
    private readonly ActionMenuController actions = new();
    private readonly GameState state = new()
    {
        Mode = GameMode.MainMenu,
        ModeStackDepth = 0
    };

    private readonly WorldAreaStore areas = new();
    private readonly Dictionary<(string Member, string SourceHash), ScriptProgram> programs = new();
    private readonly Stopwatch lifetime = Stopwatch.StartNew();
    private readonly double maximumSeconds;
    private readonly bool muted;
    private TownSceneAssets? townAssets;
    private WorldMapContext? map;
    private WorldSceneReference? location;
    private byte[] scriptMember = [], pendingHandler = [], townName = [], returnMap = [], townFile = [];
    private int musicId = 1, labelDelay, objectActionField, entryTextMode;
    private int selectedEquipmentActor = 0; //45ce68 BSS zero; status500 owns later selections.
    private bool saveDisabled = true, dialogueActive, menuSpriteActive = true, moviePrepared, hasFrame;
    private long logicalFrames, presents, dialoguesCompleted;
    private GameSession(
        string root,
        NativeWindow window,
        D3D11Presenter presenter,
        AudioDevice audio,
        FileMusicTransport musicTransport,
        FaceSpriteSource faces,
        double maximumSeconds,
        bool muted,
        string? controlsFile,
        string outputDirectory
    )
    {
        this.root = root;
        this.window = window;
        this.presenter = presenter;
        this.audio = audio;
        sessionOutputDirectory = Path.GetFullPath(outputDirectory);
        this.musicTransport = musicTransport;
        this.maximumSeconds = maximumSeconds;
        this.muted = muted;
        music = new(musicTransport);
        items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        byte[] text = File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat"));
        messages = LegacyTextTables.ParseMessages(text);
        displayMessages = GameText.Messages(messages);
        var originalBindings = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG")));
        defaultBindings = originalBindings;
        playerProfiles = new(sessionOutputDirectory, DefaultProfileName);
        var configuredBindings = InitializeSettings(Path.Combine(ActiveProfileDirectory, "config"), originalBindings);
        bindings = controlsFile is null ? configuredBindings.Clone() : ControlBindings.Parse(File.ReadAllBytes(controlsFile));
        etc = SpriteFile.Decode(Read("AT2SPR.000", "ETC"u8));
        effectSprites = SpriteFile.Decode(Read("AT2SPR.000", "CON_EFF"u8));
        effectAnimation = AnimationFile.Decode(Read("ANI.000", "CON_EFF"u8));
        smallFont = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG1.FNT")), true);
        hud = WorldHudRenderer.Load(root, smallFont);
        var largeFont = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG.FNT")), false);
        menuModal = new(etc, largeFont, text);
        leaveModal = new(etc, smallFont, text);
        actionRenderer = new(etc, smallFont);
        menuBackground = MainMenuArtwork.Load(Path.Combine(AppContext.BaseDirectory, "assets"));
        effects = SoundEffectBank.UiMemberNames.Select(name => audio.LoadEffect(Read("AT2WAV.000", Encoding.ASCII.GetBytes(name)))).ToArray();
        host = new(
            window,
            presenter,
            bindings,
            new ScanCodeEdges(),
            gate,
            ComposeScene,
            UpdateMusic,
            BeforeModalInput,
            effects,
            () => CurrentEffectsVolume,
            PollSessionInput
        );
        InitializeModernControls(originalBindings);
        host.Controls = controls;
        host.PresentCustomFrame = PresentMainMenu;
        menuNavigation = new(bindings, host.QueryScan);
        dialogue = new(DialogueCatalog.Load(Path.Combine(root, "roland.idx"), Path.Combine(root, "roland.dat")), new DialogueModalDriver(host, etc, smallFont, faces, DrawModalHud));
        menu.Enter();
        gate.Resume();
    }

    public static int Run(string root, string musicRoot, double maximumSeconds, bool muted, string? controlsFile = null, string? outputDirectory = null)
    {
        using var window = new NativeWindow("Atria II - native session", 2560, 1440);
        using var presenter = new D3D11Presenter(window.Handle, 2560, 1440);
        using var audio = new AudioDevice();
        using var transport = new FileMusicTransport(audio, musicRoot, muted ? 0 : 1);
        using var faces = new FaceSpriteSource(Path.Combine(root, "face.SPR"));
        var session = new GameSession(
            root,
            window,
            presenter,
            audio,
            transport,
            faces,
            maximumSeconds,
            muted,
            controlsFile,
            outputDirectory ?? Path.Combine(AppContext.BaseDirectory, "userdata")
        );
        try
        {
            session.Loop();
        }
        catch (OperationCanceledException) when (window.Closed || session.lifetime.Elapsed.TotalSeconds >= maximumSeconds)
        {
        }
        finally
        {
            session.DisposeProgression();
            session.controls.Dispose();
            Console.WriteLine($"Session: mode={session.state.Mode}, logicalFrames={session.logicalFrames}, presents={session.presents}, dialoguesCompleted={session.dialoguesCompleted}, introFlag={session.state.Globals[501]}, scriptActive={session.state.ScriptActive}, seconds={session.lifetime.Elapsed.TotalSeconds:F3}, dynamicCode={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}.");
        }

        return 0;
    }

    private void CheckLifetime()
    {
        if (window.Closed || lifetime.Elapsed.TotalSeconds >= maximumSeconds)
        {
            throw new OperationCanceledException("Session verification ended.");
        }
    }

    private void Loop()
    {
        while (true)
        {
            if (resumeScript is not null)
            {
                ResumeSavedScenario();
            }

            host.Pump();
            if (!host.Focused)
            {
                gate.Resume();
                Thread.Sleep(10);
                continue;
            }

            int batch = 0;
            while (gate.IsDue && batch++ < 8)
            {
                host.Pump();
                if (!host.Focused)
                {
                    break;
                }

                gate.CommitScheduled();
                host.BeginLogicalFrame(gate.LastFrameMilliseconds);
                UpdateMusic(state);
                gate.AdjustSpeed(Query(0x4e), Query(0x4a));
                if (BeforeModalInput(state))
                {
                    HandleModeInput();
                }

                if (navigation.ScreenDepth == 0 && resumeScript is not null)
                {
                    ResumeSavedScenario();
                }

                ComposeScene(state, host.FrameBuffer);
                logicalFrames++;
                hasFrame = true;
                if (navigation.ScreenDepth == 0 && state.ScriptActive != 0)
                {
                    //404170 dispatches queued scenario after the scene has been presented.
                    host.Present();
                    presents++;
                    RunScript(scriptMember, pendingHandler);
                }

                if (PendingWorldNarrative() is { } authored)
                {
                    RunScript(Encoding.ASCII.GetBytes(authored.Id), "run"u8);
                }

                FlushAutosave();
            }

            if (gate.IsDue)
            {
                continue; // Bound each batch, retain all remaining logical debt.
            }

            if (hasFrame && host.Focused)
            {
                host.Present();
                presents++;
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    private bool Query(byte scan) => host.QueryScan(scan);

    private NarrativeScenes.Scene? PendingWorldNarrative() =>
        state.Mode == GameMode.World && exploration is { Moving: false } && state.ScriptActive == 0
        && !battleActive && state.EndingResetFlag == 0 && actions.Depth < 0
            ? NarrativeScenes.After(state, "WORLD", "arrival", map?.AreaIndex) : null;
    private void UpdateMusic(GameState value)
    {
        if (value.Mode != 1)
        {
            music.Update(musicId);
        }
    }

    private bool BeforeModalInput(GameState value)
    {
        UpdatePointerFocus();
        if (controls.FocusPauseRequested)
        {
            ShowPause(false);
            return false;
        }

        if (CanChangeProfile && controls.ProfileMenuPressed())
        {
            ShowPlayerProfiles();
            return false;
        }

        if (!battleActive && !dialogueActive && value.Mode is GameMode.World or GameMode.WorldEntry or GameMode.Dungeon or GameMode.Town
&& controls.Press(0, GameAction.Status))
        {
            OpenScreen(UiScreen.Status);
            return false;
        }

        if (!battleActive && !dialogueActive && controls.Press(0, GameAction.Reference))
        {
            ShowMoveReference(0);
            return false;
        }

        if (navigation.ScreenDepth != 0)
            return true;

        if (NewCharacterRuntime.ProcessPending(value, items, selectedEquipmentActor))
        {
            return false;
        }

        if (ProcessPendingExperience(value))
        {
            return false;
        }

        if (value.EndingResetFlag != 0)
        {
            RunPendingBattle();
            return false;
        }

        if (value.VisualMode == 2)
        {
            StopAudio();
            PlayMovie("medal.jad");
            value.VisualMode = 0;
        }

        if (actions.Depth >= 0 && actions.RenderEnabled)
        {
            bool previous = menuNavigation.Previous(), next = !previous && menuNavigation.Next();
            bool cancel = !previous && !next && Query(bindings.Cancel);
            bool accept = !previous && !next && !cancel && Query(bindings.Confirm);
            actions.Input(new(previous, next, cancel, accept), HandleAction);
            return false;
        }

        return true;
    }

    private void HandleModeInput()
    {
        if (state.Mode == GameMode.MainMenu)
        {
            bool previous = false, next = false, accept = false, cancel = false;
            if (menu.Confirmation)
            {
                previous = Query(bindings.Previous);
                if (!previous)
                {
                    next = Query(bindings.Next);
                }
            }
            else
            {
                next = menuNavigation.Next();
                if (!next)
                {
                    previous = menuNavigation.Previous();
                }
            }

            if (!next && !previous)
            {
                accept = Query(bindings.Confirm);
                if (!accept)
                {
                    cancel = Query(bindings.Cancel);
                }
            }

            menu.Input(new(next, previous, accept, cancel), saveDisabled, HandleMenu);
            return;
        }

        if (state.Mode == GameMode.Town)
        {
            if (town.InputDeferred || (state.DungeonEntryPending != 0 && !town.LeaveConfirmation))
            {
                town.Input(default, state.DungeonEntryPending != 0, HandleTown);
                return;
            }

            bool next = false, previous = false, yes = false, no = false, accept = false, cancel = false;
            if (town.LeaveConfirmation)
            {
                yes = Query(bindings.Previous);
                if (!yes)
                {
                    no = Query(bindings.Next);
                }
            }
            else if (town.Scene.Count > 0)
            {
                next = Query(bindings.Next);
                previous = Query(bindings.Previous);
            }

            if (!yes && !no)
            {
                accept = Query(bindings.Confirm);
                if (!accept)
                {
                    cancel = Query(bindings.Cancel);
                }
            }

            town.Input(new(next, previous, accept, cancel, yes, no), state.DungeonEntryPending != 0, HandleTown);
            return;
        }

        if (state.Mode is GameMode.Interior or GameMode.Inn or GameMode.Shop)
        {
            HandleInteriorInput();
            return;
        }

        if (state.Mode is GameMode.World or GameMode.WorldEntry)
        {
            HandleExplorationInput();
            return;
        }

        if (state.Mode == GameMode.Status)
        {
            HandleStatusInput();
            return;
        }

        if (state.Mode == GameMode.Dungeon)
        {
            HandleDungeonInput();
            return;
        }

        if (state.Mode == GameMode.Settings)
        {
            HandleSettingsInput();
            return;
        }

        if (state.Mode == GameMode.Credits)
        {
            HandleCreditsInput();
            return;
        }

        if (state.Mode is GameMode.Save or GameMode.Load)
        {
            HandlePersistenceInput();
            return;
        }

        if (state.Mode == GameMode.QuitConfirmation)
        {
            HandleQuitInput();
            return;
        }

        if (state.Mode == GameMode.Defeat)
        {
            HandleDefeatInput();
            return;
        }

        if (state.Mode == GameMode.Ending)
        {
            HandleEndingInput();
            return;
        }

        throw new NotSupportedException($"Mode {state.Mode} input is not integrated yet.");
    }

    private void ComposeScene(GameState value, Span<ushort> target)
    {
        if (value.Mode == GameMode.MainMenu)
        {
            if (!menuSpriteActive)
            {
                throw new InvalidOperationException("Menu sprite was released before mode entry.");
            }

            MainMenuComposer.Draw(menu.AdvanceLogicalFrame(), menuBackground, target, menuModal.Draw);
        }
        else if (value.Mode == GameMode.Town)
        {
            var frame = town.AdvanceLogicalFrame(new(actions.Depth, dialogueActive ? 1 : 0, labelDelay));
            labelDelay = frame.LabelDelayAfterDraw;
            TownComposer.Draw(
                townAssets ?? throw new InvalidOperationException("Town not loaded."),
                frame,
                town.TownId,
                etc,
                smallFont,
                effectAnimation,
                effectSprites,
                target,
                leaveModal.Draw
            );
        }
        else if (value.Mode is GameMode.Interior or GameMode.Inn or GameMode.Shop)
        {
            ComposeInterior(target);
        }
        else if (value.Mode is GameMode.World or GameMode.WorldEntry)
        {
            ComposeExploration(target);
        }
        else if (value.Mode == GameMode.Status)
        {
            ComposeStatus(target);
        }
        else if (value.Mode == GameMode.Dungeon)
        {
            ComposeDungeon(target);
        }
        else if (value.Mode == GameMode.Settings)
        {
            ComposeSettings(target);
        }
        else if (value.Mode == GameMode.Credits)
        {
            ComposeCredits(target);
        }
        else if (value.Mode is GameMode.Save or GameMode.Load)
        {
            ComposePersistence(target);
        }
        else if (value.Mode == GameMode.QuitConfirmation)
        {
            ComposeQuit(target);
        }
        else if (value.Mode == GameMode.Defeat)
        {
            ComposeDefeat(target);
        }
        else if (value.Mode == GameMode.Ending)
        {
            ComposeEnding(target);
        }
        else
        {
            throw new NotSupportedException($"Mode {value.Mode} rendering is not integrated yet.");
        }

        if (actions.AdvanceLogicalFrame() is { } actionFrame)
        {
            actionRenderer.Draw(actionFrame, target);
        }

        if (!dialogueActive)
        {
            DrawHud(value, target, false);
        }

        DrawAutosaveFailure(target);
    }

    private void DrawModalHud(GameState value, Span<ushort> target) => DrawHud(value, target, true);
    private void DrawHud(GameState value, Span<ushort> target, bool modal)
    {
        int anchor = 400;
        if (value.Mode == GameMode.Town && townAssets?.Objects is { } sprites && town.Scene.Count > 0)
        {
            anchor = SodScenePresentation.GetSelectionAnchor(town.Scene, sprites, town.Selection).Y;
        }

        string title = value.Mode switch
        {
            GameMode.World or GameMode.WorldEntry => LocationTitle.World(map!.AreaIndex).Text,
            GameMode.Dungeon => dungeonTitle.Text,
            GameMode.Town or GameMode.Interior or GameMode.Inn or GameMode.Shop => LocationTitle.Town(town.TownId).Text,
            _ => ""
        };
        hud.Draw(value, title, target, modal, anchor, dungeonHudEnabled: DungeonHudVisible);
        if (!modal && value.Mode == GameMode.World && questMarker is { } marker)
        {
            int? hudY = WorldHudRenderer.OriginY(value.WorldY, false);
            var sprite = etc.Frames[marker.SpriteFrame];
            if (hudY is { } y && marker.Y < y + 60 && marker.Y + sprite.Height > y)
            {
                SpriteRaster.Draw565(sprite, target, 640, 480, 640, marker.X, marker.Y);
            }
        }
        RecordDungeonHudContext(value, modal, anchor);
    }

    private void Sound(int id)
    {
        if ((uint)id >= (uint)effects.Length)
        {
            throw new InvalidDataException("Unknown UI effect.");
        }

        effects[id].SetCurrentVolume(CurrentEffectsVolume);
        effects[id].Play();
    }

    private void StopAudio()
    {
        musicTransport.Stop();
        foreach (var effect in effects)
        {
            effect.StopAll();
        }
    }

    private void PlayMovie(string name)
    {
        var result = MoviePlayer.Play(Path.Combine(root, name), window, presenter, audio, () =>
        {
            controls.Poll(window);
            return controls.Menu(GameAction.Confirm) || controls.Menu(GameAction.Back);
        }, muted ? 0 : 1, Math.Max(0, maximumSeconds - lifetime.Elapsed.TotalSeconds));
        Console.WriteLine($"Session movie {name}: {result}.");
        controls.Clear();
        gate.Resume();
        CheckLifetime();
    }

    private void HandleMenu(MenuCommand command)
    {
        if (command.Effect != MenuEffect.Sound)
        {
            Console.WriteLine($"Menu operation: {command.Effect}, value={command.Value}.");
        }

        switch (command.Effect)
        {
            case MenuEffect.Sound:
                Sound(command.Value);
                break;
            case MenuEffect.InitializeQuitDialog:
                InitializeQuitDialog();
                break;
            case MenuEffect.ReleaseMenuSprite:
                menuSpriteActive = false;
                break; // Bounded immutable cache remains reusable.
            case MenuEffect.StopAudio:
                StopAudio();
                break;
            case MenuEffect.PrepareMovieDisplay:
                moviePrepared = true;
                break;
            case MenuEffect.PlayMovie:
                if (!moviePrepared)
                {
                    throw new InvalidOperationException("Movie display was not prepared.");
                }

                PlayMovie(command.Resource ?? throw new InvalidDataException("Movie name missing."));
                break;
            case MenuEffect.RestoreMovieDisplay:
                presenter.SetFrameSize(640, 480);
                moviePrepared = false;
                break;
            case MenuEffect.ResetGame:
                autosavePending = "";
                autosaveFailure = "";
                resumeScript = null;
                nextBattleSeed = null;
                admissionTarget = null;
                questCompletionBeforeCall = state.Journal.CompletionRevision;
                InitialGameState.ResetRecoveredCore(state, items, messages);
                NewGameSceneReset.Apply(state, town, actions);
                areas.ResetNames(messages);
                ResetShopStocks();
                ResetExplorationSession();
                break;
            case MenuEffect.SetNewGameGlobals:
                hasStartedGame = true;
                navigation.Reset();
                state.ModeStackDepth = -1;
                saveDisabled = false;
                labelDelay = 0;
                break;
            case MenuEffect.LoadGroundMap:
                map = WorldMapContext.Parse(Read("AT2AMF.000", Encoding.ASCII.GetBytes(command.Resource!)));
                break;
            case MenuEffect.InitializeGroundMap:
                areas.Capture(command.Value, map ?? throw new InvalidOperationException("Map missing."));
                break;
            case MenuEffect.CopyInitialGroundLocation:
                location = map!.SelectScene(state.WorldX, state.WorldY);
                break;
            case MenuEffect.ChangeMode:
                if (command.Value == GameMode.Town)
                    CommitLocation(LocationKind.Town);
                else
                    OpenScreen((UiScreen)command.Value);
                break;
            case MenuEffect.TryReturnToPreviousMode:
                MainMenuController.CompleteReturnToPreviousMode(CloseScreen(), HandleMenu);
                break;
            default:
                throw new NotSupportedException($"Main-menu operation {command.Effect} is not integrated yet.");
        }
    }

    private void HandleTown(TownCommand command)
    {
        switch (command.Kind)
        {
            case TownEffectKind.Sound:
                Sound(command.Value);
                break;
            case TownEffectKind.SetEntryGlobals:
                entryTextMode = command.Value;
                objectActionField = 0;
                townFile = command.Bytes.ToArray();
                break;
            case TownEffectKind.SetScriptContext:
                scriptMember = command.Bytes.ToArray();
                state.ScriptActive = 0;
                pendingHandler = [];
                break;
            case TownEffectKind.QueueEnterScript:
                pendingHandler = command.Bytes.ToArray();
                state.ScriptActive = 1;
                break;
            case TownEffectKind.SetTownNames:
                townName = command.Bytes.ToArray();
                break;
            case TownEffectKind.SetMusic:
                musicId = command.Value;
                break;
            case TownEffectKind.PlayMusic:
                music.Update(command.Value);
                break;
            case TownEffectKind.LoadBackground:
                if (townAssets is null || !townAssets.Scene.BackgroundReference.Span.SequenceEqual(command.Bytes.Span))
                {
                    throw new InvalidDataException("Loaded town background does not match the requested reference.");
                }

                break; // TownSceneAssets loaded and owns these exact pixels before committing entry.
            case TownEffectKind.SetObjectActionField:
                objectActionField = command.Value;
                break;
            case TownEffectKind.OpenActionMenu:
                actions.Open(0, 0, 5, command.Value, command.Value2, displayMessages, state.Mode);
                break;
            case TownEffectKind.CloseActionMenus:
                actions.CloseAll();
                break;
            case TownEffectKind.SetUiDirty:
                actions.SetRenderEnabled(command.Value != 0);
                break;
            case TownEffectKind.RunPreEnter:
                RunScript(command.Bytes.Span, "pre_enter"u8, ScenarioContinuation.EnterBuilding);
                town.CompletePreEnter(state.EnterPermission != 0, state.Mode, HandleTown);
                break;
            case TownEffectKind.RunPreOut:
                state.EnterPermission = 0;

                RunScript(command.Bytes.Span, "pre_out"u8, ScenarioContinuation.LeaveTown);
                town.CompletePreOut(state.EnterPermission != 0, HandleTown, state.Mode);
                break;
            case TownEffectKind.ReleaseTownSprite:
                town.ReleaseObjectSprites();
                if (townAssets is not null)
                {
                    townAssets = townAssets with
                    {
                        Objects = null
                    };
                }

                break;
            case TownEffectKind.EnableSave:
                saveDisabled = false;
                break;
            case TownEffectKind.CopyReturnMapReferences:
                returnMap = command.Bytes.ToArray();
                break;
            case TownEffectKind.LoadReturnMap:
                LoadExplorationReturnMap(command.Bytes);
                break;
            case TownEffectKind.CopyTownToBattleReturn:
                townFile = command.Bytes.ToArray();
                break;
            case TownEffectKind.InitializeEncounter:
                StartDungeonFromTown();
                break;
            case TownEffectKind.ChangeMode:
                if (GameMode.CanSnapshot(command.Value))
                    CommitLocation((LocationKind)command.Value);
                else
                    OpenScreen((UiScreen)command.Value);
                break;
            default:
                throw new NotSupportedException($"Town operation {command.Kind} is not integrated yet.");
        }
    }

    private void HandleAction(ActionMenuCommand command)
    {
        if (TryHandleDungeonAction(command))
        {
            return;
        }

        if (TryHandleExplorationAction(command))
        {
            return;
        }

        if (command.Kind == ActionMenuEffect.Sound)
        {
            Sound(command.Value);
        }
        else if (command.Kind == ActionMenuEffect.InvokeTownChoice)
        {
            town.ChooseAction(command.Value, HandleTown);
        }
        else if (state.Mode is GameMode.Interior or GameMode.Inn or GameMode.Shop)
        {
            InteriorActions.HandleCommand(command);
        }
        else
        {
            throw new NotSupportedException($"Action-menu operation {command.Kind} is not integrated in mode {state.Mode}.");
        }
    }

    private void RunScript(ReadOnlySpan<byte> member, ReadOnlySpan<byte> handler, ScenarioContinuation continuation = ScenarioContinuation.None, int continuationMode = 0, ScriptCheckpoint? replay = null)
    {
        byte[] source = GameResources.Read(root, "SCRIPT.000", member, replay?.SourceHash);
        string name = Path.GetFileNameWithoutExtension(Encoding.ASCII.GetString(member)).ToUpperInvariant();
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source));
        if (!programs.TryGetValue((name, hash), out var program))
        {
            programs.Add((name, hash), program = ScriptProgram.Parse(name, source));
        }

        string function = Encoding.ASCII.GetString(handler);
        Console.WriteLine($"Scenario started: {name}/{function}.");
        if (replay is not null && replay.SourceHash != hash)
        {
            throw new InvalidDataException("Scenario source differs from the saved continuation.");
        }

        if (activeScriptHost is not null)
        {
            throw new InvalidOperationException("External scenario reentry cannot overwrite a continuation.");
        }

        activeScriptIdentity = new(name, function, hash, continuation, continuationMode, []);
        var recording = new CheckpointScriptHost(new ScriptStateHost(state, this), replay?.Calls)
        {
            BeforeCall = BeforeScenarioCall,
            AfterCall = AfterScenarioCall
        };
        activeScriptHost = recording;
        try
        {
            new ScriptMachine(program, recording).Invoke(function);
            recording.VerifyReplayComplete();
        }
        finally
        {
            activeScriptHost = null;
            activeScriptIdentity = null;
        }

        if (state.Mode is not (GameMode.Defeat or GameMode.Ending) && NarrativeScenes.All.Any(scene => scene.Id == name))
        {
            state.NarrativeHistory.Add(name);
        }

        Console.WriteLine($"Scenario returned: {name}/{function}; active={state.ScriptActive}, introFlag={state.Globals[501]}.");
        if (state.Mode is not (GameMode.Defeat or GameMode.Ending) && NarrativeScenes.After(state, name, function) is { } scene)
        {
            RunScript(Encoding.ASCII.GetBytes(scene.Id), "run"u8, continuation, continuationMode);
        }
    }

    public void Execute(ScriptEffect effect, GameState value)
    {
        if (effect.Kind is ScriptEffectKind.Battle or ScriptEffectKind.BattleWithPostAction or ScriptEffectKind.ForcedBattleWithPostAction)
        {
            if (!ReferenceEquals(value, state))
            {
                throw new InvalidOperationException("Battle belongs to the owning session state.");
            }
            //battle_start queues only; the other two callbacks wait for the real battle and caller restoration.
            if (effect.Kind != ScriptEffectKind.Battle)
            {
                RunPendingBattle();
            }

            return;
        }

        if (effect.Kind == ScriptEffectKind.ModeChange)
        {
            if (!ReferenceEquals(value, state) || value.Mode is not (GameMode.Ending or GameMode.Defeat))
            {
                throw new InvalidOperationException("Scenario termination requires the owning session state.");
            }
            //42ddf0 resets depth and calls429260; keep the actual stack in sync with its packed fields.
            ShowTermination(value.Mode);
            return;
        }

        if (effect.Kind is not (ScriptEffectKind.DialogueText or ScriptEffectKind.DialogueKey or ScriptEffectKind.QuestionText or ScriptEffectKind.QuestionKey))
        {
            throw new NotSupportedException($"Scenario effect {effect.Kind} is not integrated yet.");
        }

        bool previous = dialogueActive;
        dialogueActive = true;
        try
        {
            dialogue.Execute(effect, value);
            dialoguesCompleted++;
            Console.WriteLine($"Dialogue completed: count={dialoguesCompleted}.");
        }
        finally
        {
            dialogueActive = previous;
        }
    }

    private byte[] Read(string archiveName, ReadOnlySpan<byte> member) => GameResources.Read(root, archiveName, member);
}
