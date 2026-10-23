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
    private readonly SaveLoadMenuController persistenceMenu=new();
    private SaveLoadMenuRenderer? persistenceRenderer;
    private NativeSaveStore? persistenceStore;
    private SaveSlotInfo[] persistenceSlots=[];
    private ushort[]? persistenceBackground;
    private int persistenceSelection;
    private LegacySaveImage? persistenceLegacyTemplate;
    private NativeSaveStore PersistenceStore=>persistenceStore??=new(Path.Combine(sessionOutputDirectory,"saves"),root);

    private void EnterPersistence(int mode)
    {
        if(mode is not (220 or 230))throw new ArgumentOutOfRangeException(nameof(mode));
        if(persistenceBackground is null)
        {
            var bitmap=BmpImage.Decode(Read("AT2BMP.000","saveload.bmp"u8));
            if(bitmap.Width!=640||bitmap.Height!=480)throw new InvalidDataException("Save/load background must be640x480.");
            persistenceBackground=new ushort[640*480];
            for(int i=0;i<persistenceBackground.Length;i++){uint rgb=bitmap.Pixels[i];persistenceBackground[i]=(ushort)(((rgb>>8)&0xf800)|((rgb>>5)&0x7e0)|((rgb>>3)&31));}
        }
        persistenceRenderer??=new(etc,smallFont,BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root,"ENG.FNT")),false),messages);
        persistenceMenu.Enter((SaveLoadMenuKind)mode,persistenceSelection,HandlePersistence);
    }
    private void HandlePersistenceInput()
    {
        if(persistenceMenu.Failure is not null)
        {if(Query(bindings.Confirm)||Query(bindings.Cancel)){Sound(4);persistenceMenu.AcknowledgeFailure();}return;}
        if(persistenceMenu.PendingOperation)
        {
            bool loading=persistenceMenu.Kind==SaveLoadMenuKind.Load;
            //42b060 saves+refreshes BEFORE polling this opportunity's keys. Loading422660 instead returns after activation.
            persistenceMenu.Input(default,persistenceSlots.Select(s=>s.Metadata).ToArray(),HandlePersistence);
            if(loading || persistenceMenu.Failure is not null){persistenceSelection=persistenceMenu.Selection;return;}
        }
        bool down=false,up=false,left=false,right=false,accept=false,cancel=false;
        if(persistenceMenu.Countdown==0)
        {
            if(persistenceMenu.Confirmation){left=Query(bindings.Previous);if(!left)right=Query(bindings.Next);}
            else {down=Query(bindings.Down);if(!down)up=Query(bindings.Up);}
            if(!down&&!up&&!left&&!right){accept=Query(bindings.Confirm);if(!accept)cancel=Query(bindings.Cancel);}
        }
        int wheel=controls.TakeWheel();if(wheel!=0&&!persistenceMenu.Confirmation){down=wheel<0;up=wheel>0;accept=false;}
        if(accept && persistenceSlots.Length>0 && !persistenceMenu.Confirmation && !persistenceMenu.EmptyNotice && persistenceMenu.Kind==SaveLoadMenuKind.Load &&
            persistenceSlots[persistenceMenu.Selection].Status is SaveSlotStatus.Invalid or SaveSlotStatus.Unreadable)
        {
            Sound(3);persistenceMenu.ReportFailure(persistenceSlots[persistenceMenu.Selection].Failure??"Selected save cannot be read.");return;
        }
        persistenceMenu.Input(new(down,up,accept,cancel,left,right),persistenceSlots.Select(s=>s.Metadata).ToArray(),HandlePersistence);
        persistenceSelection=persistenceMenu.Selection;
    }
    private void ComposePersistence(Span<ushort> target)
    {
        (persistenceBackground??throw new InvalidOperationException("Save/load UI not entered.")).CopyTo(target);
        // The original has no reliable disk-error contract. This explicit native-engine notice is acknowledged before retry.
        ReadOnlyMemory<byte> failure=persistenceMenu.Failure is null?default:Encoding.ASCII.GetBytes("Save/load unavailable: "+persistenceMenu.Failure);
        persistenceRenderer!.Draw(persistenceRenderer.BuildPlan(persistenceMenu.AdvanceLogicalFrame(),persistenceSlots,
            statusAlternateJournalHeading,failure),target);
    }
    private void HandlePersistence(SaveLoadMenuCommand command)
    {
        try
        {
            switch(command.Kind)
            {
                case SaveLoadMenuEffect.Sound:Sound(command.Value);break;
                case SaveLoadMenuEffect.RefreshSlots:persistenceSlots=PersistenceStore.InspectLibrary(persistenceMenu.Kind==SaveLoadMenuKind.Save);persistenceMenu.SetCount(persistenceSlots.Length);break;
                case SaveLoadMenuEffect.PopMode:
                    if(!modes.TryPop(out var transition))throw new InvalidDataException("Save/load return mode is absent.");
                    EnterMode(transition.Mode);break;
                case SaveLoadMenuEffect.RestoreBackground:hasFrame=false;break;
                case SaveLoadMenuEffect.SaveSlot:
                    var row=persistenceSlots[command.Value];
                    SaveSessionSlot(row.IsNew||row.Automatic?PersistenceStore.NextManualSlot():row.Index);break;
                case SaveLoadMenuEffect.LoadSlot:LoadSessionSlot(persistenceSlots[command.Value].Index);break;
                default:throw new InvalidDataException("Unknown save/load command.");
            }
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException or OverflowException or Atria.Script.ScriptException)
        {persistenceMenu.ReportFailure(error.Message);Console.WriteLine("Persistence operation rejected: "+error.Message);}
    }
    private static SessionMapSnapshot CaptureSessionMap(WorldMapContext value,ReadOnlySpan<byte> member)=>new(member.ToArray(),value.GetHeader(),value.GetTiles());
    private SessionRuntimeSnapshot CaptureSessionRuntime(int savedMode,bool exact=false)
    {
        if(dialogueActive || (!exact&&(state.ScriptActive!=0 || state.EndingResetFlag!=0)) || moviePrepared)
            throw new NotSupportedException("An active script, battle transition, or movie cannot be snapshotted yet.");
        if((savedMode==300&&exploration?.Moving==true) || (savedMode==330&&dungeon?.Motion.Moving==true))
            throw new NotSupportedException("Moving-controller absolute clock rebasing is not recovered for snapshots.");
        var current=map??throw new InvalidDataException("Current AMF context is missing.");
        int currentKind=ReferenceEquals(current,dungeon?.RuntimeMap.Context)?2:ReferenceEquals(current,explorationEntry?.Map)?1:0;
        byte[] currentMember=currentKind==2?dungeonEntry!.MapMember.ToArray():currentKind==1?explorationEntry!.MapMember.ToArray():Encoding.ASCII.GetBytes($"ground{current.AreaIndex+1}.amf");
        SessionExplorationSnapshot? world=null;
        if(explorationEntry is {} entry)
            world=new(CaptureSessionMap(entry.Map,entry.MapMember.Span),entry.PrimaryName.ToArray(),entry.SecondaryName.ToArray(),entry.ScriptMember.ToArray(),
                exploration?.CaptureRuntime(),explorationRenderer?.CaptureRuntime(),explorationMapReload,explorationTravelSteps,explorationRevealX,explorationRevealY);
        SessionDungeonSnapshot? retainedDungeon=null;
        if(dungeon is {} controller)
        {
            var plan=dungeonEntry??throw new InvalidDataException("Retained dungeon resource identity is missing.");
            var composer=dungeonComposer??throw new InvalidDataException("Retained dungeon composer is missing.");
            retainedDungeon=new(CaptureSessionMap(controller.RuntimeMap.Context,plan.MapMember.Span),(int)plan.Source,plan.ScriptMember.ToArray(),plan.ReturnX,plan.ReturnY,plan.CaptureArea,
                controller.CaptureRuntime(),composer.Raster.CaptureRuntime(),composer.DynamicText.ToArray(),dungeonReturnMap.ToArray(),dungeonReturnTown.ToArray(),dungeonReturnTownScript.ToArray(),
                dungeonPadding,dungeonHudY,dungeonTreasureKind,dungeonTreasureAmount,dungeonTreasureItem,dungeonHudEnabled);
        }
        var selected=location is {} loc?new SessionLocationSnapshot(loc.SceneIndex,loc.SceneMember.ToArray(),loc.ScriptMember.ToArray(),loc.AreaIndex):null;
        return new(savedMode,modes.CaptureRuntime(),random.CaptureRuntime(),CaptureSessionMap(current,currentMember),currentKind,dialogue.CaptureRuntime(),music.CaptureRuntime(),musicTransport.CapturePlayingTrack(),selected,town.CaptureRuntime(),world,retainedDungeon,
            scriptMember.ToArray(),pendingHandler.ToArray(),townName.ToArray(),returnMap.ToArray(),townFile.ToArray(),previousTown,musicId,labelDelay,objectActionField,entryTextMode,
            selectedEquipmentActor,awardMusicTrack,saveDisabled,statusAlternateJournalHeading,settings.Encode(),gate.ThresholdMilliseconds,dungeonEndingTextPrefix.ToArray(),menu.CaptureRetained(),credits.CaptureRetained(),defeat.CaptureRetained(),retainedBattleCounter,retainedBattleVisualDetail,retainedBattleRenderPeriod)
            {ExactResume=exact,Script=CaptureScriptCheckpoint(),Interior=savedMode is 410 or 420 or 430?interior.CaptureRuntime():null,
                NextBattleSeed=nextBattleSeed,ScriptReturnLocation=scriptReturnLocation is {} prior?new(prior.SceneIndex,prior.SceneMember.ToArray(),prior.ScriptMember.ToArray(),prior.AreaIndex):null};
    }
    private void SaveSessionSlot(int slot)
    {
        int savedMode=modes.LegacySaveMode();
        if(saveDisabled)throw new NotSupportedException("Original save gate currently prevents saving.");
        PersistenceStore.WriteNative(slot,CaptureSaveImage(savedMode,true,""));
    }
    private NativeSaveImage CaptureSaveImage(int savedMode,bool exact,string reason)
    {
        var context=CaptureSessionRuntime(savedMode,exact) with {SaveReason=reason};
        // Native saves capture active world storage before writing, except town400. Prepare a detached area store.
        var capturedAreas=new WorldAreaStore();for(int i=0;i<8;i++)capturedAreas.Import(i,areas.GetInline(i),areas.GetPayload(i));
        if(savedMode==300)capturedAreas.Capture(map!.AreaIndex,map);
        int area=savedMode is 400 or 410 or 420 or 430?(location?.AreaIndex??throw new InvalidDataException("Town area is missing.")):
            savedMode==330?(dungeonEntry?.CaptureArea??location?.AreaIndex??map!.AreaIndex):map!.AreaIndex;
        byte[] primary=townName,secondary=capturedAreas.GetInline(area).AsSpan(50,50).ToArray();
        var metadata=LegacySlotMetadata.Empty([]).WithCurrent(primary,secondary,savedMode,area,town.TownId,state.GetPartyInt(0,200),messages[309].Span);
        var stocks=shopStocks??throw new InvalidDataException("Full30-shop state has not been initialized.");
        var image=NativeSaveImage.Capture(state,savedMode,capturedAreas,stocks,metadata,SessionRuntimeCodec.Encode(context),persistenceLegacyTemplate);
        return image;
    }
    private sealed class PreparedPersistence
    {
        public required SessionRuntimeSnapshot Snapshot;
        public required WorldMapContext Map;
        public TownSceneAssets? TownAssets;
        public required TownSnapshot Town;
        public List<TownCommand> TownEntryCommands=[];
        public WorldMapEntry? WorldEntry;
        public WorldSceneAssets? WorldAssets;
        public WorldSceneRenderer? WorldRenderer;
        public ExplorationController? World;
        public byte[] WorldRaw=[];
        public DungeonEntryPlan? DungeonEntry;
        public DungeonController? Dungeon;
        public DungeonComposer? DungeonComposer;
        public TownSceneAssets? InteriorAssets;
    }
    private (WorldMapContext Map,byte[] Raw) PrepareSessionMap(SessionMapSnapshot saved,bool world=true)
    {
        byte[] raw=Read("AT2AMF.000",saved.Member);
        // Parse the immutable source before replacing only recovered mutable header/tile banks.
        _=WorldMapContext.Parse(raw);
        saved.Header.CopyTo(raw,0);saved.Tiles.CopyTo(raw,AmfMap.HeaderSize);
        var map=WorldMapContext.Parse(raw);
        _=map.BackgroundReference;
        if(world)
        {
            _=WorldMapContext.CString(map.HeaderBytes[..13]);
            for(int tile=0;tile<100;tile++)for(int member=0;member<10;member++)
                _=WorldMapContext.CString(map.TileBytes.Slice(tile*328+128+member*20,20));
        }
        else
        {
            var dungeonMap=new DungeonMap(map);
            for(int tile=0;tile<100;tile++)
                if(dungeonMap.Read(tile,88) is 7 or 9 || (dungeonMap.Read(tile,88)==6&&dungeonMap.Read(tile,92)==4))_=dungeonMap.Member(tile);
        }
        return(map,raw);
    }
    private PreparedPersistence PrepareSessionRuntime(NativeRuntimeContext context,GameState savedState)
    {
        var saved=SessionRuntimeCodec.Decode(context);
        if((uint)savedState.WorldX>=10 || (uint)savedState.WorldY>=10)throw new InvalidDataException("Saved world coordinates exceed the recovered10x10 map.");
        if(saved.SavedMode!=savedState.Mode || (!saved.ExactResume&&(savedState.ScriptActive!=0 || savedState.EndingResetFlag!=0)))
            throw new InvalidDataException("Snapshot mode/script transition contradicts the supported activation context.");
        if(saved.PlayingMusicTrack>0)musicTransport.ValidateTrack(saved.PlayingMusicTrack);
        if(saved.MusicId>0)musicTransport.ValidateTrack(saved.MusicId);
        var current=PrepareSessionMap(saved.CurrentMap,saved.CurrentMapKind!=2);
        int entryTrack=saved.SavedMode==400?TownCatalog.Music[saved.Location!.SceneIndex]:saved.SavedMode==300?checked(current.Map.AreaIndex+1):saved.MusicId;
        if(entryTrack>0)musicTransport.ValidateTrack(entryTrack);
        var result=new PreparedPersistence {Snapshot=saved,Map=current.Map,Town=saved.Town};
        var validatedStack=new ModeStack();validatedStack.RestoreRuntime(saved.Modes);
        if(!saved.ExactResume&&validatedStack.LegacySaveMode()!=saved.SavedMode)throw new InvalidDataException("Underlying save mode disagrees with retained mode slots.");
        var retainedTown=new TownController();
        TownSceneAssets? retainedTownAssets=saved.Town.SceneName.Length==0?null:TownSceneAssets.Load(root,saved.Town.SceneName);
        retainedTown.RestoreRuntime(saved.Town,retainedTownAssets?.Scene,retainedTownAssets?.Objects);
        result.TownAssets=retainedTownAssets;
        if(saved.Exploration is {} world)
        {
            var loaded=PrepareSessionMap(world.Map);result.WorldRaw=loaded.Raw;
            if((uint)loaded.Map.AreaIndex>=8)throw new InvalidDataException("Retained world area is outside eight saved records.");
            result.WorldEntry=new(loaded.Map,loaded.Map.AreaIndex,world.PrimaryName,world.SecondaryName,world.ScriptMember,world.Map.Member);
            result.WorldAssets=WorldSceneAssets.Load(root,loaded.Map,loaded.Raw);
            result.WorldRenderer=new(result.WorldAssets);
            if(world.Renderer is {} renderer)result.WorldRenderer.RestoreRuntime(renderer);
            if(world.Controller is {} controller)
            {
                if(controller.Moving&&saved.SavedMode==300)throw new NotSupportedException("Moving world snapshot clock rebasing is not recovered.");
                result.World=new(state,loaded.Map,result.WorldAssets.CharacterAnimation,random,result.WorldRenderer.IsSceneReferenceBlocked,
                    controller.Direction,controller.TravelSteps);
                result.World.RestoreRuntime(controller);
            }
            if(saved.CurrentMapKind==1)
            {
                if(!saved.CurrentMap.Member.AsSpan().SequenceEqual(world.Map.Member))throw new InvalidDataException("Current world alias resource identity differs.");
                result.Map=loaded.Map;
            }
            result.WorldRenderer.RebuildBase(loaded.Map,savedState);
        }
        if(saved.Dungeon is {} dungeonSaved)
        {
            var loaded=PrepareSessionMap(dungeonSaved.Map,false);var dungeonMap=new DungeonMap(loaded.Map);
            var assets=DungeonSceneAssets.Load(root,loaded.Map,loaded.Raw);
            result.DungeonEntry=new((DungeonEntrySource)dungeonSaved.Source,dungeonSaved.Map.Member,dungeonSaved.ScriptMember,dungeonSaved.ReturnX,dungeonSaved.ReturnY,dungeonSaved.CaptureArea);
            result.Dungeon=new(state,dungeonMap,assets.Base.CharacterAnimation,random,initializeState:false);
            if(dungeonSaved.Controller.Motion.Moving&&saved.SavedMode==330)throw new NotSupportedException("Moving dungeon snapshot clock rebasing is not recovered.");
            result.Dungeon.RestoreRuntime(dungeonSaved.Controller);
            result.DungeonComposer=new(state,result.Dungeon,dungeonMap,assets,smallFont,messages,
                Enumerable.Range(0,ItemCatalog.Count).Select(i=>items.GetItem(i).Name).ToArray(),dungeonSaved.DynamicText);
            result.DungeonComposer.Raster.RestoreRuntime(dungeonSaved.Raster);
            result.DungeonComposer.Raster.RebuildObjects(dungeonMap,result.Dungeon.Visibility,savedState.DungeonFog!=0);
            if(saved.CurrentMapKind==2)
            {
                if(!saved.CurrentMap.Member.AsSpan().SequenceEqual(dungeonSaved.Map.Member))throw new InvalidDataException("Current dungeon alias resource identity differs.");
                result.Map=loaded.Map;
            }
        }
        if(saved.CurrentMapKind is <0 or >2 || (saved.CurrentMapKind==1&&result.WorldEntry is null) || (saved.CurrentMapKind==2&&result.Dungeon is null))
            throw new InvalidDataException("Current map alias references absent retained context.");
        if(!result.Map.GetHeader().AsSpan().SequenceEqual(saved.CurrentMap.Header)||!result.Map.GetTiles().AsSpan().SequenceEqual(saved.CurrentMap.Tiles))
            throw new InvalidDataException("Current AMF alias contradicts the retained map banks.");
        if(saved.SavedMode==400&&!saved.ExactResume)
        {
            var location=saved.Location!;
            var assets=TownSceneAssets.Load(root,location.SceneMember);
            // Dry-run native entry on a detached controller, collecting host commands without executing them.
            retainedTown.Enter(assets.Scene,assets.Objects,location.SceneIndex,0,location.SceneMember,
                TownCatalog.Name(messages,location.SceneIndex),TownCatalog.Music[location.SceneIndex],result.TownEntryCommands.Add);
            result.TownAssets=assets;result.Town=retainedTown.CaptureRuntime();
        }
        else if(saved.SavedMode==300)
        {
            if(result.World is null||result.WorldRenderer is null||result.WorldEntry is null)throw new InvalidDataException("Active world controller was not saved.");
            if(!ReferenceEquals(result.Map,result.WorldEntry.Map))throw new InvalidDataException("Active world map alias is invalid.");
            if(!saved.ExactResume){result.World.InputDelay=2;ExplorationRules.MarkArrival(result.Map,savedState.WorldX,savedState.WorldY);}
            result.WorldRenderer.RebuildBase(result.Map,savedState);
        }
        else if(saved.SavedMode==330 && !ReferenceEquals(result.Map,result.Dungeon!.RuntimeMap.Context))throw new InvalidDataException("Active dungeon map alias is invalid.");
        if(saved.Interior is {} inside)
        {
            var outer=retainedTown.Scene.GetObject(retainedTown.Selection);
            result.InteriorAssets=TownSceneAssets.Load(root,outer.GetString(0).Span,loadObjects:inside.Mode!=420);
            new InteriorController().RestoreRuntime(inside,result.InteriorAssets.Scene);
        }
        if(saved.Script is {} replay)
        {
            byte[] source=Read("SCRIPT.000",Encoding.ASCII.GetBytes(replay.Member));
            if(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source))!=replay.SourceHash)throw new InvalidDataException("Saved scenario source changed.");
            var program=Atria.Script.ScriptProgram.Parse(replay.Member,source);
            if(!program.Functions.ContainsKey(replay.Function))throw new InvalidDataException("Saved scenario function absent.");
            Atria.Script.CheckpointScriptHost.Validate(program,replay);
        }
        // Validate actual script identity/entry handler before any shared state is replaced.
        ReadOnlySpan<byte> requiredScript=saved.SavedMode==400?result.TownAssets!.Scene.ScriptReference.Span:saved.ScriptMember;
        if(!requiredScript.IsEmpty)
        {
            var program=Atria.Script.ScriptProgram.Parse(Encoding.ASCII.GetString(requiredScript),Read("SCRIPT.000",requiredScript));
            if(saved.SavedMode==400&&!program.Functions.ContainsKey("enter"))throw new InvalidDataException("Town load entry script lacks enter handler.");
        }
        return result;
    }
    private void LoadSessionSlot(int slot)
    {
        loadingSnapshot=true;
        try
        {
        var content=PersistenceStore.ReadSlot(slot)??throw new InvalidDataException("Selected slot is empty.");
        if(content.Legacy is not null)throw new NotSupportedException("Legacy import activation requires authentic fixture verification and the remaining native context fields.");
        var image=content.Native!;PreparedPersistence? prepared=null;
        // Do not initialize live shops as a side effect of a rejected load.
        var targetShops=shopStocks??new ShopStock[30];
        image.CommitStorage(state,areas,targetShops,(context,savedState)=>
        {
            prepared=PrepareSessionRuntime(context,savedState);
            InventoryMaintenance.ReconcileBattleSupplies(savedState);
        });
        var next=prepared!;var saved=next.Snapshot;shopStocks=targetShops;persistenceLegacyTemplate=image.LegacyTemplate;
        map=next.Map;location=saved.Location is {} loc?new(loc.SceneIndex,loc.SceneMember,loc.ScriptMember,loc.AreaIndex):null;
        scriptMember=saved.ScriptMember;pendingHandler=saved.PendingHandler;townName=saved.TownName;returnMap=saved.ReturnMap;townFile=saved.TownFile;
        previousTown=saved.ExactResume?saved.PreviousTown:0;musicId=saved.MusicId;labelDelay=saved.LabelDelay;objectActionField=saved.ObjectActionField;entryTextMode=saved.EntryTextMode;
        selectedEquipmentActor=saved.SelectedEquipmentActor;awardMusicTrack=saved.AwardMusicTrack;saveDisabled=saved.ExactResume?saved.SaveDisabled:false;statusAlternateJournalHeading=saved.StatusAlternateJournalHeading;
        gate.SetThresholdMilliseconds(saved.FrameThreshold);random.RestoreRuntime(saved.Random);modes.RestoreRuntime(saved.Modes);modes.ResetDepth();modes.Push(saved.SavedMode);state.ModeStackDepth=0;state.Mode=saved.SavedMode;
        actions.CloseAll();dialogue.RestoreRuntime(saved.Dialogue);townAssets=next.TownAssets;town.RestoreRuntime(next.Town,townAssets?.Scene,townAssets?.Objects);
        interiorAssets=next.InteriorAssets;trade=null;interiorActions=null;
        if(saved.Interior is {} inside){interior.RestoreRuntime(inside,interiorAssets!.Scene);if(inside.Mode==430)itemSprites=SpriteFile.Decode(Read("AT2SPR.000","item.spr"u8));}
        explorationEntry=next.WorldEntry;explorationAssets=next.WorldAssets;explorationRenderer=next.WorldRenderer;exploration=next.World;explorationOriginalAmf=next.WorldRaw;
        explorationMapReload=saved.Exploration?.MapReload??false;explorationTravelSteps=saved.Exploration?.TravelSteps??0;
        explorationRevealX=saved.Exploration?.RevealX??0;explorationRevealY=saved.Exploration?.RevealY??0;
        dungeonEntry=next.DungeonEntry;dungeon=next.Dungeon;dungeonComposer=next.DungeonComposer;
        if(saved.Dungeon is {} retained)
        {
            dungeonReturnMap=retained.ReturnMap;dungeonReturnTown=retained.ReturnTown;dungeonReturnTownScript=retained.ReturnTownScript;
            dungeonPadding=retained.Padding;dungeonHudY=retained.HudY;
            dungeonTreasureKind=retained.TreasureKind;dungeonTreasureAmount=retained.TreasureAmount;
            dungeonTreasureItem=retained.TreasureItem;dungeonHudEnabled=retained.HudEnabled;
        }
        else {dungeonReturnMap=[];dungeonReturnTown=[];dungeonReturnTownScript=[];dungeonPadding=dungeonHudY=dungeonTreasureKind=dungeonTreasureAmount=dungeonTreasureItem=0;dungeonHudEnabled=false;}
        saved.EndingTextPrefix.CopyTo(dungeonEndingTextPrefix,0);
        menu.RestoreRetained(saved.MainMenu);credits.RestoreRetained(saved.Credits);defeat.RestoreRetained(saved.Defeat);
        retainedBattleCounter=saved.RetainedBattleCounter;retainedBattleVisualDetail=saved.RetainedBattleVisualDetail;retainedBattleRenderPeriod=saved.RetainedBattleRenderPeriod;
        // Asset preparation and validation are complete. Audio/UI resources are transient and restart from the recovered track identity.
        StopExclusiveScenarioSound();StopAudio();RestoreSettingsSnapshot(saved.Settings);music.RestoreRuntime(saved.Music);
        if(saved.PlayingMusicTrack>0)musicTransport.Play(saved.PlayingMusicTrack);
        foreach(var command in next.TownEntryCommands)HandleTown(command);
        if(saved.SavedMode==300&&!saved.ExactResume){town.SetSavedSelection(0);townName=next.WorldEntry!.PrimaryName.ToArray();musicId=NativeModeEntry.MusicId(300,musicId,map.AreaIndex);music.Update(musicId);}
        hasFrame=false;
        resumeScript=saved.Script;nextBattleSeed=saved.NextBattleSeed;scriptReturnLocation=saved.ScriptReturnLocation is {} prior?new(prior.SceneIndex,prior.SceneMember,prior.ScriptMember,prior.AreaIndex):null;autosavePending="";controls.Clear();
        if(saved.Interior is not null&&resumeScript is null&&state.ScriptActive==0)InteriorActions.OpenAction();
        }
        finally {loadingSnapshot=false;}
    }
}
