using System.Diagnostics;
using System.Text;
using Atria.Audio;
using Atria.Game.ActionMenu;
using Atria.Game.Dialogue;
using Atria.Game.Menu;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Game.World;
using Atria.Game.Exploration;
using Atria.Game.Progression;
using Atria.Render.Scenes;
using Atria.Platform;
using Atria.Render;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Script;
using Atria.Video;
using Atria.Game.Controls;

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
    private readonly ModalFrameGate gate=new(new MonotonicModalClock(),0);
    private readonly NativeDialogueFrameHost host;
    private readonly MenuNavigation menuNavigation;
    private readonly DialogueRuntime dialogue;
    private readonly ItemCatalog items;
    private readonly IReadOnlyList<ReadOnlyMemory<byte>> messages;
    private readonly SpriteFile etc,menuSprites,fireSprites,effectSprites;
    private readonly AnimationFile fireAnimation,effectAnimation;
    private readonly BitmapFont smallFont;
    private readonly WorldHudRenderer hud;
    private readonly ushort[] menuBackground;
    private readonly MainMenuModalRenderer menuModal;
    private readonly TownLeaveModalRenderer leaveModal;
    private readonly ActionMenuRenderer actionRenderer;
    private readonly MainMenuController menu=new();
    private readonly TownController town=new();
    private readonly ActionMenuController actions=new();
    private readonly GameState state=new() { Mode=200,ModeStackDepth=0 };
    private readonly ModeStack modes=new();
    private readonly WorldAreaStore areas=new();
    private readonly Dictionary<string,ScriptProgram> programs=new(StringComparer.Ordinal);
    private readonly Stopwatch lifetime=Stopwatch.StartNew();
    private readonly double maximumSeconds;
    private readonly bool muted;
    private TownSceneAssets? townAssets;
    private WorldMapContext? map;
    private WorldSceneReference? location;
    private byte[] scriptMember=[],pendingHandler=[],townName=[],returnMap=[],townFile=[];
    private int previousTown,musicId=1,labelDelay,objectActionField,entryTextMode;
    private int selectedEquipmentActor=0; //45ce68 BSS zero; status500 owns later selections.
    private bool saveDisabled=true,dialogueActive,menuSpriteActive=true,moviePrepared,hasFrame;
    private long logicalFrames,presents,dialoguesCompleted;

    private GameSession(string root,NativeWindow window,D3D11Presenter presenter,AudioDevice audio,
        FileMusicTransport musicTransport,FaceSpriteSource faces,double maximumSeconds,bool muted,string? controlsFile,string outputDirectory)
    {
        this.root=root; this.window=window; this.presenter=presenter; this.audio=audio;
        sessionOutputDirectory=Path.GetFullPath(outputDirectory);
        this.musicTransport=musicTransport; this.maximumSeconds=maximumSeconds; this.muted=muted;
        music=new(musicTransport);
        items=ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root,"ATITEM.DAT")));
        byte[] text=File.ReadAllBytes(Path.Combine(root,"TEXTmsg.dat")); messages=LegacyTextTables.ParseMessages(text);
        var originalBindings=ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root,"BATTLE.CFG")));
        var configuredBindings=InitializeSettings(Path.Combine(sessionOutputDirectory,"config"),originalBindings);
        bindings=controlsFile is null?configuredBindings:ControlBindings.Parse(File.ReadAllBytes(controlsFile));
        etc=SpriteFile.Decode(Read("AT2SPR.000","ETC"u8));
        menuSprites=SpriteFile.Decode(Read("AT2SPR.000","MENU"u8));
        fireSprites=SpriteFile.Decode(Read("AT2SPR.000","MENUFIRE"u8));
        fireAnimation=AnimationFile.Decode(Read("ANI.000","MENUFIRE"u8));
        effectSprites=SpriteFile.Decode(Read("AT2SPR.000","CON_EFF"u8));
        effectAnimation=AnimationFile.Decode(Read("ANI.000","CON_EFF"u8));
        smallFont=BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root,"ENG1.FNT")),true);
        hud=WorldHudRenderer.Load(root,smallFont);
        var largeFont=BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root,"ENG.FNT")),false);
        menuModal=new(etc,largeFont,text); leaveModal=new(etc,smallFont,text); actionRenderer=new(etc,smallFont);
        var bitmap=BmpImage.Decode(Read("AT2BMP.000","MENUBG"u8));
        if(bitmap.Width!=640 || bitmap.Height!=480) throw new InvalidDataException("Invalid main-menu background.");
        menuBackground=new ushort[bitmap.Pixels.Length];
        for(int i=0;i<menuBackground.Length;i++)
        { uint rgb=bitmap.Pixels[i]; menuBackground[i]=(ushort)(((rgb>>8)&0xf800)|((rgb>>5)&0x7e0)|((rgb>>3)&31)); }
        effects=SoundEffectBank.UiMemberNames.Select(name=>audio.LoadEffect(Read("AT2WAV.000",Encoding.ASCII.GetBytes(name)))).ToArray();
        host=new(window,presenter,bindings,new ScanCodeEdges(),gate,ComposeScene,UpdateMusic,BeforeModalInput,
            effects,()=>CurrentEffectsVolume,PollSessionInput);
        InitializeModernControls(originalBindings);
        host.Controls=controls;
        menuNavigation=new(bindings,host.QueryScan);
        dialogue=new(DialogueCatalog.Load(Path.Combine(root,"roland.idx"),Path.Combine(root,"roland.dat")),new DialogueModalDriver(host,etc,smallFont,faces,DrawModalHud));
        menu.Enter(); gate.Resume();
    }

    public static int Run(string root,string musicRoot,double maximumSeconds,bool muted,string? controlsFile=null,string? outputDirectory=null)
    {
        using var window=new NativeWindow("Atria II - native session",2560,1440);
        using var presenter=new D3D11Presenter(window.Handle,2560,1440);
        using var audio=new AudioDevice();
        using var transport=new FileMusicTransport(audio,musicRoot,muted?0:1);
        using var faces=new FaceSpriteSource(Path.Combine(root,"face.SPR"));
        var session=new GameSession(root,window,presenter,audio,transport,faces,maximumSeconds,muted,controlsFile,
            outputDirectory??Path.Combine(AppContext.BaseDirectory,"userdata"));
        try { session.Loop(); }
        catch(OperationCanceledException) when(window.Closed || session.lifetime.Elapsed.TotalSeconds>=maximumSeconds) { }
        finally
        {
            session.DisposeProgression();
            session.controls.Dispose();
            Console.WriteLine($"Session: mode={session.state.Mode}, logicalFrames={session.logicalFrames}, presents={session.presents}, dialoguesCompleted={session.dialoguesCompleted}, introFlag={session.state.Globals[501]}, scriptActive={session.state.ScriptActive}, seconds={session.lifetime.Elapsed.TotalSeconds:F3}, dynamicCode={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}.");
        }
        return 0;
    }

    private void CheckLifetime()
    { if(window.Closed || lifetime.Elapsed.TotalSeconds>=maximumSeconds) throw new OperationCanceledException("Session verification ended."); }
    private void Loop()
    {
        while(true)
        {
            if(resumeScript is not null)ResumeSavedScenario();
            host.Pump();
            if(!host.Focused) { gate.Resume(); Thread.Sleep(10); continue; }
            int batch=0;
            while(gate.IsDue && batch++<8)
            {
                host.Pump(); if(!host.Focused) break;
                gate.CommitScheduled(); host.BeginLogicalFrame(gate.LastFrameMilliseconds);
                UpdateMusic(state);
                gate.AdjustSpeed(Query(0x4e),Query(0x4a));
                if(BeforeModalInput(state)) HandleModeInput();
                if(resumeScript is not null)ResumeSavedScenario();
                ComposeScene(state,host.FrameBuffer); logicalFrames++; hasFrame=true;
                if(state.ScriptActive!=0)
                {
                    //404170 dispatches queued scenario after the scene has been presented.
                    host.Present(); presents++;
                    RunScript(scriptMember,pendingHandler);
                }
                FlushAutosave();
            }
            if(gate.IsDue) continue; // Bound each batch, retain all remaining logical debt.
            if(hasFrame && host.Focused) { host.Present(); presents++; }
            else Thread.Sleep(1);
        }
    }
    private bool Query(byte scan)=>host.QueryScan(scan);
    private void UpdateMusic(GameState value) { if(value.Mode!=1) music.Update(musicId); }
    private bool BeforeModalInput(GameState value)
    {
        UpdatePointerFocus();
        if(controls.FocusPauseRequested){ShowPause(false);return false;}
        if(!battleActive&&!dialogueActive&&value.Mode is 300 or 310 or 330 or 400&&controls.Press(0,GameAction.Status))
        {if(value.Mode is 300 or 310)modes.Push(300);modes.Push(500);EnterMode(500);return false;}
        if(!battleActive&&!dialogueActive&&controls.Press(0,GameAction.Reference)){ShowMoveReference(0);return false;}
        if(NewCharacterRuntime.ProcessPending(value,items,selectedEquipmentActor)) return false;
        if(ProcessPendingExperience(value)) return false;
        if(value.EndingResetFlag!=0) {RunPendingBattle();return false;}
        if(value.VisualMode==2)
        { StopAudio(); PlayMovie("medal.jad"); value.VisualMode=0; }
        if(actions.Depth>=0 && actions.RenderEnabled)
        {
            bool previous=menuNavigation.Previous(),next=!previous&&menuNavigation.Next();
            bool cancel=!previous&&!next&&Query(bindings.Cancel);
            bool accept=!previous&&!next&&!cancel&&Query(bindings.Confirm);
            actions.Input(new(previous,next,cancel,accept),HandleAction); return false;
        }
        return true;
    }
    private void HandleModeInput()
    {
        if(state.Mode==200)
        {
            bool previous=false,next=false,accept=false,cancel=false;
            if(menu.Confirmation) { previous=Query(bindings.Previous); if(!previous) next=Query(bindings.Next); }
            else { next=menuNavigation.Next(); if(!next) previous=menuNavigation.Previous(); }
            if(!next&&!previous) { accept=Query(bindings.Confirm); if(!accept) cancel=Query(bindings.Cancel); }
            menu.Input(new(next,previous,accept,cancel),saveDisabled,HandleMenu); return;
        }
        if(state.Mode==400)
        {
            if(town.InputDeferred || (state.DungeonEntryPending!=0 && !town.LeaveConfirmation))
            { town.Input(default,state.DungeonEntryPending!=0,HandleTown); return; }
            bool next=false,previous=false,yes=false,no=false,accept=false,cancel=false;
            if(town.LeaveConfirmation) { yes=Query(bindings.Previous); if(!yes) no=Query(bindings.Next); }
            else if(town.Scene.Count>0) { next=Query(bindings.Next); previous=Query(bindings.Previous); }
            if(!yes&&!no) { accept=Query(bindings.Confirm); if(!accept) cancel=Query(bindings.Cancel); }
            town.Input(new(next,previous,accept,cancel,yes,no),state.DungeonEntryPending!=0,HandleTown); return;
        }
        if(state.Mode is 410 or 420 or 430) { HandleInteriorInput(); return; }
        if(state.Mode is 300 or 310) { HandleExplorationInput(); return; }
        if(state.Mode==500) { HandleStatusInput(); return; }
        if(state.Mode==330) { HandleDungeonInput(); return; }
        if(state.Mode==250) { HandleSettingsInput(); return; }
        if(state.Mode==240) { HandleCreditsInput(); return; }
        if(state.Mode is 220 or 230) { HandlePersistenceInput(); return; }
        if(state.Mode==9999) { HandleQuitInput(); return; }
        if(state.Mode==1100) { HandleDefeatInput(); return; }
        if(state.Mode==1000) { HandleEndingInput(); return; }
        throw new NotSupportedException($"Mode {state.Mode} input is not integrated yet.");
    }
    private void ComposeScene(GameState value,Span<ushort> target)
    {
        if(value.Mode==200)
        {
            if(!menuSpriteActive) throw new InvalidOperationException("Menu sprite was released before mode entry.");
            MainMenuComposer.Draw(menu.AdvanceLogicalFrame(),menuBackground,menuSprites,fireAnimation,fireSprites,target,menuModal.Draw);
        }
        else if(value.Mode==400)
        {
            var frame=town.AdvanceLogicalFrame(new(actions.Depth,dialogueActive?1:0,labelDelay)); labelDelay=frame.LabelDelayAfterDraw;
            TownComposer.Draw(townAssets??throw new InvalidOperationException("Town not loaded."),frame,town.TownId,etc,smallFont,effectAnimation,effectSprites,target,leaveModal.Draw);
        }
        else if(value.Mode is 410 or 420 or 430) ComposeInterior(target);
        else if(value.Mode is 300 or 310) ComposeExploration(target);
        else if(value.Mode==500) ComposeStatus(target);
        else if(value.Mode==330) ComposeDungeon(target);
        else if(value.Mode==250) ComposeSettings(target);
        else if(value.Mode==240) ComposeCredits(target);
        else if(value.Mode is 220 or 230) ComposePersistence(target);
        else if(value.Mode==9999) ComposeQuit(target);
        else if(value.Mode==1100) ComposeDefeat(target);
        else if(value.Mode==1000) ComposeEnding(target);
        else throw new NotSupportedException($"Mode {value.Mode} rendering is not integrated yet.");
        if(actions.AdvanceLogicalFrame() is {} actionFrame) actionRenderer.Draw(actionFrame,target);
        if(!dialogueActive) DrawHud(value,target,false);
        DrawAutosaveFailure(target);
    }
    private void DrawModalHud(GameState value,Span<ushort> target)=>DrawHud(value,target,true);
    private void DrawHud(GameState value,Span<ushort> target,bool modal)
    {
        int anchor=400;
        if(value.Mode==400 && townAssets?.Objects is {} sprites && town.Scene.Count>0)
            anchor=SodScenePresentation.GetSelectionAnchor(town.Scene,sprites,town.Selection).Y;
        hud.Draw(value,townName,target,modal,anchor,dungeonHudEnabled:DungeonHudVisible);
        RecordDungeonHudContext(value,modal,anchor);
    }
    private void Sound(int id)
    { if((uint)id>=(uint)effects.Length) throw new InvalidDataException("Unknown UI effect."); effects[id].SetCurrentVolume(CurrentEffectsVolume); effects[id].Play(); }
    private void StopAudio() { musicTransport.Stop(); foreach(var effect in effects) effect.StopAll(); }
    private void PlayMovie(string name)
    {
        var result=MoviePlayer.Play(Path.Combine(root,name),window,presenter,audio,
            ()=>{controls.Poll(window);return controls.Menu(GameAction.Confirm)||controls.Menu(GameAction.Back);},muted?0:1,
            Math.Max(0,maximumSeconds-lifetime.Elapsed.TotalSeconds));
        Console.WriteLine($"Session movie {name}: {result}."); controls.Clear();gate.Resume(); CheckLifetime();
    }
    private void HandleMenu(MenuCommand command)
    {
        if(command.Effect!=MenuEffect.Sound) Console.WriteLine($"Menu operation: {command.Effect}, value={command.Value}.");
        switch(command.Effect)
        {
            case MenuEffect.Sound: Sound(command.Value); break;
            case MenuEffect.InitializeQuitDialog: InitializeQuitDialog(); break;
            case MenuEffect.ReleaseMenuSprite: menuSpriteActive=false; break; // Bounded immutable cache remains reusable.
            case MenuEffect.StopAudio: StopAudio(); break;
            case MenuEffect.PrepareMovieDisplay: moviePrepared=true; break;
            case MenuEffect.PlayMovie:
                if(!moviePrepared) throw new InvalidOperationException("Movie display was not prepared.");
                PlayMovie(command.Resource??throw new InvalidDataException("Movie name missing.")); break;
            case MenuEffect.RestoreMovieDisplay: presenter.SetFrameSize(640,480); moviePrepared=false; break;
            case MenuEffect.ResetGame:
                autosavePending="";autosaveFailure="";resumeScript=null;nextBattleSeed=null;scriptReturnLocation=null;questCompletionChanged=false;
                InitialGameState.ResetRecoveredCore(state,items,messages);
                NewGameSceneReset.Apply(state,town,actions,ref previousTown);
                areas.ResetNames(messages); ResetShopStocks(); ResetExplorationSession(); break;
            case MenuEffect.SetNewGameGlobals: modes.ResetDepth(); state.ModeStackDepth=-1; saveDisabled=false; labelDelay=0; break;
            case MenuEffect.LoadGroundMap: map=WorldMapContext.Parse(Read("AT2AMF.000",Encoding.ASCII.GetBytes(command.Resource!))); break;
            case MenuEffect.InitializeGroundMap: areas.Capture(command.Value,map??throw new InvalidOperationException("Map missing.")); break;
            case MenuEffect.CopyInitialGroundLocation: location=map!.SelectScene(state.WorldX,state.WorldY); break;
            case MenuEffect.ChangeMode:
                if(command.Value==400) modes.StartNewGame(); else modes.Push(command.Value);
                EnterMode(command.Value); break;
            case MenuEffect.TryReturnToPreviousMode:
                bool success=modes.TryPop(out var transition);
                MainMenuController.CompleteReturnToPreviousMode(success,HandleMenu);
                if(success) { if(transition.RestoreFlag) actions.SetRenderEnabled(true); EnterMode(transition.Mode); }
                break;
            default: throw new NotSupportedException($"Main-menu operation {command.Effect} is not integrated yet.");
        }
    }
    private void EnterMode(int mode)
    {
        int oldMode=state.Mode;
        controls.Clear();
        state.ModeStackDepth=modes.Depth; state.Mode=mode;
        if(mode==200) { menuSpriteActive=true; menu.Enter(); }
        else if(mode==400)
        {
            var selected=location??throw new InvalidOperationException("No current town reference.");
            townAssets=TownSceneAssets.Load(root,selected.SceneMember.Span);
            town.Enter(townAssets.Scene,townAssets.Objects,selected.SceneIndex,previousTown,selected.SceneMember,
                TownCatalog.Name(messages,selected.SceneIndex),TownCatalog.Music[selected.SceneIndex],HandleTown);
        }
        else if(mode is 410 or 420 or 430) EnterInterior(mode);
        else if(mode is 300 or 310) EnterExploration(mode);
        else if(mode==500) EnterStatus();
        else if(mode==330) EnterDungeon();
        else if(mode==250) EnterSettings();
        else if(mode==240) EnterCredits();
        else if(mode is 220 or 230) EnterPersistence(mode);
        else if(mode==9999) EnterQuitConfirmation();
        else if(mode==1100) EnterDefeat();
        else if(mode==1000) EnterEnding();
        else throw new NotSupportedException($"Mode {mode} entry is not integrated yet.");
        Console.WriteLine($"Session entered mode={mode}, town={town.TownId}.");
        if(oldMode!=mode&&(oldMode is 300 or 310 or 330 or 400)&&(mode is 300 or 330 or 400))RequestAutosave("Переход локации");
    }
    private void HandleTown(TownCommand command)
    {
        switch(command.Kind)
        {
            case TownEffectKind.Sound: Sound(command.Value); break;
            case TownEffectKind.SetEntryGlobals: entryTextMode=command.Value; objectActionField=0; townFile=command.Bytes.ToArray(); break;
            case TownEffectKind.SetScriptContext: scriptMember=command.Bytes.ToArray(); state.ScriptActive=0; pendingHandler=[]; break;
            case TownEffectKind.QueueEnterScript: pendingHandler=command.Bytes.ToArray(); state.ScriptActive=1; break;
            case TownEffectKind.SetTownNames: townName=command.Bytes.ToArray(); break;
            case TownEffectKind.SetMusic: musicId=command.Value; break;
            case TownEffectKind.PlayMusic: music.Update(command.Value); break;
            case TownEffectKind.SetPreviousTown: previousTown=command.Value; break;
            case TownEffectKind.LoadBackground:
                if(townAssets is null || !townAssets.Scene.BackgroundReference.Span.SequenceEqual(command.Bytes.Span))
                    throw new InvalidDataException("Loaded town background does not match the requested reference.");
                break; // TownSceneAssets loaded and owns these exact pixels before committing entry.
            case TownEffectKind.SetObjectActionField: objectActionField=command.Value; break;
            case TownEffectKind.OpenActionMenu: actions.Open(0,0,5,command.Value,command.Value2,messages,state.Mode); break;
            case TownEffectKind.CloseActionMenus: actions.CloseAll(); break;
            case TownEffectKind.SetUiDirty: actions.SetRenderEnabled(command.Value!=0); break;
            case TownEffectKind.RunPreEnter:
                RunScript(command.Bytes.Span,"pre_enter"u8,1); town.CompletePreEnter(state.EnterPermission!=0,state.Mode,HandleTown); break;
            case TownEffectKind.RunPreOut:
                if(command.Value!=0) state.EnterPermission=0;
                RunScript(command.Bytes.Span,"pre_out"u8,2); town.CompletePreOut(state.EnterPermission!=0,HandleTown,state.Mode); break;
            case TownEffectKind.ReleaseTownSprite:
                town.ReleaseObjectSprites(); if(townAssets is not null) townAssets=townAssets with { Objects=null }; break;
            case TownEffectKind.PushMapReturnMarker: modes.Push(command.Value); break;
            case TownEffectKind.ResetModeStackAndEnableSave: modes.ResetDepth(); state.ModeStackDepth=-1; saveDisabled=false; break;
            case TownEffectKind.CopyReturnMapReferences: returnMap=command.Bytes.ToArray(); break;
            case TownEffectKind.LoadReturnMap: LoadExplorationReturnMap(command.Bytes); break;
            case TownEffectKind.CopyTownToBattleReturn: townFile=command.Bytes.ToArray(); break;
            case TownEffectKind.InitializeEncounter: StartDungeonFromTown(); break;
            case TownEffectKind.ChangeMode: modes.Push(command.Value); EnterMode(command.Value); break;
            default: throw new NotSupportedException($"Town operation {command.Kind} is not integrated yet.");
        }
    }
    private void HandleAction(ActionMenuCommand command)
    {
        if(TryHandleDungeonAction(command))return;
        if(TryHandleExplorationAction(command))return;
        if(command.Kind==ActionMenuEffect.Sound) Sound(command.Value);
        else if(command.Kind==ActionMenuEffect.InvokeTownChoice) town.ChooseAction(command.Value,HandleTown);
        else if(state.Mode is 410 or 420 or 430) InteriorActions.HandleCommand(command);
        else throw new NotSupportedException($"Action-menu operation {command.Kind} is not integrated in mode {state.Mode}.");
    }
    private void RunScript(ReadOnlySpan<byte> member,ReadOnlySpan<byte> handler,int continuation=0,int continuationMode=0,ScriptCheckpoint? replay=null)
    {
        using var archive=BinderArchive.Open(Path.Combine(root,"SCRIPT.000"));
        int index=archive.FindResource(member); if(index<0) throw new InvalidDataException($"Required scenario member is missing: {Encoding.ASCII.GetString(member)} / {Encoding.ASCII.GetString(handler)}; mode={state.Mode}, town={town.TownId}.");
        string name=archive.Entries[index].Name;
        if(!programs.TryGetValue(name,out var program)) programs.Add(name,program=ScriptProgram.Parse(name,archive.ReadMember(index)));
        string function=Encoding.ASCII.GetString(handler);
        Console.WriteLine($"Scenario started: {name}/{function}.");
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archive.ReadMember(index)));
        if(replay is not null&&replay.SourceHash!=hash)throw new InvalidDataException("Scenario source differs from the saved continuation.");
        if(activeScriptHost is not null)throw new InvalidOperationException("External scenario reentry cannot overwrite a continuation.");
        activeScriptIdentity=new(name,function,hash,continuation,continuationMode,[]);
        var recording=new CheckpointScriptHost(new ScriptStateHost(state,this),replay?.Calls){BeforeCall=BeforeScenarioCall,AfterCall=AfterScenarioCall};
        activeScriptHost=recording;
        try {new ScriptMachine(program,recording).Invoke(function);recording.VerifyReplayComplete();}
        finally {activeScriptHost=null;activeScriptIdentity=null;}
        Console.WriteLine($"Scenario returned: {name}/{function}; active={state.ScriptActive}, introFlag={state.Globals[501]}.");
    }
    public void Execute(ScriptEffect effect,GameState value)
    {
        if(effect.Kind is ScriptEffectKind.Battle or ScriptEffectKind.BattleWithPostAction or ScriptEffectKind.ForcedBattleWithPostAction)
        {
            if(!ReferenceEquals(value,state)) throw new InvalidOperationException("Battle belongs to the owning session state.");
            //battle_start queues only; the other two callbacks wait for the real battle and caller restoration.
            if(effect.Kind!=ScriptEffectKind.Battle) RunPendingBattle();
            return;
        }
        if(effect.Kind==ScriptEffectKind.ModeChange)
        {
            if(!ReferenceEquals(value,state) || value.Mode is not (1000 or 1100))
                throw new InvalidOperationException("Scenario termination requires the owning session state.");
            //42ddf0 resets depth and calls429260; keep the actual stack in sync with its packed fields.
            modes.ResetDepth(); modes.Push(value.Mode); EnterMode(value.Mode); return;
        }
        if(effect.Kind is not (ScriptEffectKind.DialogueText or ScriptEffectKind.DialogueKey or ScriptEffectKind.QuestionText or ScriptEffectKind.QuestionKey))
            throw new NotSupportedException($"Scenario effect {effect.Kind} is not integrated yet.");
        bool previous=dialogueActive; dialogueActive=true;
        try { dialogue.Execute(effect,value); dialoguesCompleted++; Console.WriteLine($"Dialogue completed: count={dialoguesCompleted}."); }
        finally { dialogueActive=previous; }
    }
    private byte[] Read(string archiveName,ReadOnlySpan<byte> member)
    {
        using var archive=BinderArchive.Open(Path.Combine(root,archiveName)); int index=archive.FindResource(member);
        if(index<0) throw new InvalidDataException($"Required resource is absent from {archiveName}.");
        return archive.ReadMember(index);
    }
}
