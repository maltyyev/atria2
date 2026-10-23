using System.Buffers.Binary;
using Atria.Game.Combat;
using Atria.Game.State;
using Atria.Resources;
namespace Atria.Tests.Combat;

public static class CombatSlowDurationTests
{
    public static string Run(string root)
    {
        static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("On-hit slow: "+message);}
        static void W(byte[] b,int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at,4),value);
        static int I(byte[] b,int at)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at,4));
        var scene=CombatSceneDefinition.Load(root,new(FixedEncounterKind.Scripted,2));
        var runtime=new CombatSceneRuntime(root,scene,0);runtime.BindDeclaredAnimations(1000);
        var state=InitialGameState.Create(ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root,"ATITEM.DAT"))),
            LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root,"TEXTmsg.dat"))));
        var selection=new CombatPartySelection(state,1);
        selection.ApplyInput((_,key)=>key is CombatSelectionInput.Toggle or CombatSelectionInput.Finish,_=>{});
        runtime.PopulateSelectedParty(selection);runtime.BindAllAttributes(0,0,1000,()=>1);runtime.BindPlayerRoles(selection);
        var enemy=runtime.Actors[0];int playerIndex=scene.PartyStartIndex;var player=runtime.Actors[playerIndex];
        var hits=new CombatHitState(runtime.Actors.Count);
        var hud=new CombatHudRaster(runtime,CombatBattleAssets.Load(root,scene),new byte[240]);
        // Controlled type/level fixtures over real bound actor/animation assets exercise the production hit path.
        foreach(int type in new[]{14,23,25,9})foreach(int level in new[]{1,5,13})
        {
            W(enemy.Binding!.MutableFields,0xafc,type);W(enemy.Fields,0x1ec,level);
            player.Fields.AsSpan(0x244,72).Clear();W(player.Fields,0x2ac,0);W(player.Fields,0x2b4,0);
            W(player.Fields,0x170,100000);W(player.Fields,0xad,0);W(player.Fields,0xb1,6);
            enemy.Animation!.BeginAttack(0,1000);
            CombatHitResult Hit(uint now)=>CombatHitRuntime.Apply(runtime,hits,0,playerIndex,state,now,false,()=>9,_=>{},_=>{},()=>{},
                _=>throw new InvalidOperationException("High-HP duration fixture must survive."));
            var result=Hit(1000);
            if(type is 14 or 23)
            {
                int duration=(level+5)*2000;
                Check(result==CombatHitResult.DamageApplied&&I(player.Fields,0x2ac)==1,"damaging hit still applies slow");
                Check(I(player.Fields,0x244)==1000+duration&&I(player.Fields,0x248)==15,"deadline is half original duration for both enemy types");
                Hit(1500);Check(I(player.Fields,0x244)==1500+duration,"repeat hit refreshes using shortened duration");
                hud.AdvanceVisualState((uint)(1500+duration-1),false);
                Check(I(player.Fields,0x2ac)==1,"slow remains active immediately before deadline");
                hud.AdvanceVisualState((uint)(1500+duration),false);
                Check(I(player.Fields,0x2ac)==0&&I(player.Fields,0x244)==0,"normal status expiry clears slow at shortened deadline");
            }
            else if(type==25)
                Check(I(player.Fields,0x2ac)==0&&I(player.Fields,0x2b4)==1&&I(player.Fields,0x244)==1000+(level+5)*4000,"poison duration unchanged");
            else Check(I(player.Fields,0x2ac)==0&&I(player.Fields,0x244)==0,"ordinary enemy does not acquire slow");
        }
        return "On-hit slow: both types, three levels, damage path, refresh and actual HUD expiry passed; poison and ordinary hits unchanged.";
    }
}
