using System.Buffers.Binary;
using Atria.Game.Dungeon;
using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Resources;
using Atria.Render.Codecs;

namespace Atria.Tests.Dungeon;

public static class DungeonTests
{
    public static string Run(string root)
    {
        using var archive=BinderArchive.Open(Path.Combine(root,"AT2AMF.000"));
        byte[] raw=archive.ReadMember(archive.FindResource("ground1.amf"u8));raw.AsSpan(313,32800).Clear();
        void Put(int index,int field,int value)=>BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(313+index*328+field,4),value);
        var visibility=new DungeonVisibility();visibility.MarkLocal(4,4,10);
        int[] expected=[24,33,34,35,42,43,44,45,46,53,54,55,64,23,25,32,36,52,56,63,65];
        Check(Enumerable.Range(0,100).Where(i=>visibility.Values[i]!=0).SequenceEqual(expected.Order()),"exact21 native offsets");
        visibility=new(19);visibility.Place(0,0,1);Check(visibility.NextTorch==0,"retained ring wraps slot19");
        var retainedBank=visibility.GetTorchBank();var reentered=new DungeonVisibility(0,default,retainedBank);
        Check(!reentered.Torches[19].Active&&reentered.Torches[19].Cost==1&&reentered.Torches[19].Remaining==4,"reentry clears only ACTIVE preserving aliased torch payload");
        visibility.Arrival(9,9,10);Check(visibility.Values[0]==2&&visibility.Torches[19].Remaining==3,"placed torch overrides distant local mask");
        for(int i=0;i<3;i++) visibility.Arrival(9,9,10);
        Check(visibility.Values[0]==0&&!visibility.Torches[19].Active,"torch expires before mask update at fourth arrival");
        Put(44,88,4);Put(44,92,20);Put(44,96,1);Put(20,92,1);
        var map=new DungeonMap(WorldMapContext.Parse(raw));Check(!map.CanEnter(4,4),"closed linked gate blocks regardless terrain");
        map.Write(20,92,0);Check(map.CanEnter(4,4),"changed linked switch opens gate");
        map.Write(44,88,0);map.Write(44,4,16);Check(!map.CanEnter(4,4),"terrain16 blocks dungeon");map.Write(44,4,1);
        var state=new GameState{WorldX=4,WorldY=4,ResourceCounterFc9a=1};var rng=new LegacyRandom(1);
        var arrival=new DungeonArrival(state,map,new());
        Check(arrival.Complete().Count==0&&state.ResourceCounterFc9a==1,"first arrival retains fire");
        Check(arrival.Complete().Count==0&&state.ResourceCounterFc9a==0,"second arrival consumes fire without immediate exit");
        arrival.Complete();var effects=arrival.Complete();
        Check(effects.Count==2&&effects[0].Member.Span.SequenceEqual("dungeon_nofire"u8)&&effects[1].Kind==DungeonArrivalEffectKind.ExitPending,"negative fire queues script before exit");
        map.Write(44,88,8);map.Write(44,92,123);map.Write(44,96,unchecked((int)0xffff0001));
        effects=arrival.Complete();Check(state.PendingBattle==123&&state.BattleVariant==1&&state.EndingResetFlag==1&&map.Read(44,88)==0&&effects[0].Kind==DungeonArrivalEffectKind.BattlePending,"battle WORD variant and one-shot tile mutation");
        map.Write(44,88,5);state.ResourceCounterFc9a=100;state.SetMemberStatus(0,1);state.SetPartyInt(0,160,80);rng=new(1);arrival=new(state,map,new());
        effects=arrival.Complete();Check(rng.Calls==0&&effects.Count==0&&state.ResourceCounterFc9a==100,"retired tile5 has no modal, resource penalty or RNG draw");
        map.Write(44,88,6);map.Write(44,104,1);map.Write(44,92,2);map.Write(44,96,7);map.Write(44,100,3);
        effects=arrival.Complete();Check(state.OwnedItems[7]==3&&state.AvailableItems[7]==3&&map.Read(44,104)==0,"treasure updates both native item banks then clears availability");
        map.Write(44,104,1);map.Write(44,92,99);var beforeItems=state.OwnedItems[7];
        effects=arrival.Complete();Check(effects[0].Kind==DungeonArrivalEffectKind.TreasureRetained&&map.Read(44,104)==0&&state.OwnedItems[7]==beforeItems,"unknown treasure subtype follows proven stale display path without fabricated reward");
        var sequences=new AnimationSequence[8];
        for(int i=0;i<8;i++) {var frames=new AnimationFrame[i<4?1:10];for(int f=0;f<frames.Length;f++) {var words=new ushort[28];if(i==5&&f==1) words[1]=64;frames[f]=new(words,0,0,0);}sequences[i]=new(0,frames);}
        state=new(){WorldX=4,WorldY=4,Day=14};map.Write(45,4,1);var motion=new DungeonMotion(state,map,new([],sequences));
        Check(motion.Begin(WorldDirection.Right),"dungeon valid target begins travel");
        var retainedMotion=motion.CaptureRuntime();motion.ClearMovementAfterBattle();
        Check(motion.CaptureRuntime()==(retainedMotion with {Moving=false}),"Battle return clears only46027c while retaining dungeon frame/pixel/deadline state");
        motion.RestoreRuntime(retainedMotion);
        Check(!motion.Advance(()=>0).Arrived,"movement timer strict comparison atzero");
        var step=motion.Advance(()=>1);Check(step.Arrived&&step.Pose.Frame==0&&state.WorldX==5&&state.Day==14,"64-pixel ANI endpoint completes while returned pose retains predraw frame; no world calendar");
        //Actual ED00 AMF+SOD/ground/object/CHIPCHAR/torch/UI resources, independent of synthetic rule fields above.
        byte[] dungeonRaw=archive.ReadMember(archive.FindResource("ed00.amf"u8));var original=AmfMap.Parse(dungeonRaw);
        map=new(WorldMapContext.Parse(dungeonRaw));var entrance=map.FindEntrance()??throw new InvalidDataException("ED00 fixture lacks native entrance.");
        state=new(){Mode=330,WorldX=entrance.X,WorldY=entrance.Y,DungeonFog=1,ResourceCounterFc9a=20};
        var assets=DungeonSceneAssets.Load(root,map.Context,dungeonRaw);var nativeRandom=new LegacyRandom(1);
        var runtime=new DungeonController(state,map,assets.Base.CharacterAnimation,nativeRandom);
        var oldSnapshot=runtime.CaptureRuntime() with {Modal=5};
        runtime.RestoreRuntime(oldSnapshot);
        Check(runtime.Modal==0,"pre0.3.5 removed modal resumes as normal exploration");
        var messages=LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root,"TEXTmsg.dat")));
        var font=BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root,"ENG1.FNT")),true);
        var catalog=ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root,"ATITEM.DAT")));
        var composer=new DungeonComposer(state,runtime,map,assets,font,messages,Enumerable.Range(0,ItemCatalog.Count).Select(i=>catalog.GetItem(i).Name).ToArray());
        var first=runtime.Input(new(203,205,200,208,46,1,15),_=>false,_=>false);
        Check(first.Count==1&&first[0].Kind==DungeonCommandKind.RebuildObjects,"actual entry first logical input builds full native cache");
        composer.ApplyRasterCommand(first[0]);ushort[] canvas=new ushort[640*480];
        composer.Compose(canvas,()=>21,i=>DungeonVisibilityAliases.Read(i,runtime,4,4,composer.Raster.TorchAnimationCounter,0,0,0,0,0,0,new byte[12]),421,false);
        Check(canvas.Any(p=>p!=0)&&state.DungeonFogDirty==0&&nativeRandom.Calls==0,"actual dungeon raster composes and clears refresh without gameplay RNG");
        var next=runtime.Input(new(203,205,200,208,46,1,15),_=>false,_=>false);
        Check(next.Count==1&&next[0].Kind==DungeonCommandKind.RunPendingScript&&runtime.PendingFunction.Span.SequenceEqual("enter"u8)&&state.ScriptActive==1,"actual entry queues enter only after full refresh frame");
        state.ScriptActive=0;runtime.OpenTorch();
        var binding=new DungeonBindings(203,205,200,208,46,1,15);
        runtime.Input(binding,_=>false,scan=>scan==205);Check(runtime.TorchCost==1,"torch modal does not use horizontal held keys");
        runtime.Input(binding,_=>false,scan=>scan==200);runtime.Input(binding,_=>false,scan=>scan==200);
        Check(runtime.TorchCost==2,"torch modal uses native UP held everysecond eligible call");
        var aliasedTorches=new DungeonTorch[20];aliasedTorches[17]=new(false,2,3,4,5);
        var border=new DungeonController(state,map,assets.Base.CharacterAnimation,new LegacyRandom(1),retainedTorches:aliasedTorches);
        Check(DungeonVisibilityAliases.Read(-20,border,4,4,0,0,0,0,0,0,0,new byte[12])==2,"off-grid darkness aliases inactive slot17X, not clamped zero");
        Check(DungeonVisibilityAliases.Read(112,border,4,4,0,0,0,2,0,0,0,new byte[12])==2,"lower border darkness aliases retained item id");
        var plan=new DungeonEntryPlan(DungeonEntrySource.WorldTile,"ed00.amf"u8.ToArray(),default,4,4,0);
        Check(DungeonEntry.ResolveScript(plan,original).Span.SequenceEqual(original.EmbeddedScene!.ScriptReference.Span),"ordinary dungeon entry resolves loaded SOD script identity");
        return "Dungeon contracts: visibility/torch/gates/fire/encounter/retired-tile5/treasure passed.";
    }
    private static void Check(bool ok,string message) {if(!ok) throw new InvalidOperationException("Dungeon: "+message);}
}
