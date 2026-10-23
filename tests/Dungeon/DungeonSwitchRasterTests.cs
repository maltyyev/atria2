using Atria.Game.Dungeon;
using Atria.Game.Exploration;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Resources;

namespace Atria.Tests.Dungeon;

public static class DungeonSwitchRasterTests
{
    public static string Run(string root)
    {
        using var archive=BinderArchive.Open(Path.Combine(root,"AT2AMF.000"));
        int maps=0,switches=0,objects=0,gates=0;
        foreach(var entry in archive.Entries)
        {
            byte[] raw=archive.ReadMember(entry.Index);
            var decoded=AmfMap.Parse(raw);var scene=decoded.EmbeddedScene;
            if(scene is null)continue;
            var conditional=Enumerable.Range(0,scene.Count).Select(scene.GetObject).Where(o=>o.GetInteger(4)==1).ToArray();
            if(conditional.Length==0)continue;
            Check(decoded.Width==10&&decoded.Height==10,entry.Name+" dungeon dimensions");
            var map=new DungeonMap(WorldMapContext.Parse(raw));var assets=DungeonSceneAssets.Load(root,map.Context,raw);
            var raster=new DungeonSceneRenderer(assets.Base,assets.ObjectMarkers);var visibility=new DungeonVisibility();
            // Expose every tile, retaining fog's bottom/right border behavior as well.
            for(int y=0;y<10;y++)for(int x=0;x<10;x++)visibility.MarkLocal(x,y,10);
            ushort[] closed=new ushort[640*480],opened=new ushort[640*480],restored=new ushort[640*480];
            foreach(int switchCell in conditional.Select(o=>o.GetInteger(5)).Distinct())
            {
                Check(map.Read(switchCell,88)==3,entry.Name+" object references a real switch");
                int initial=map.Read(switchCell,92);
                foreach(bool fog in new[]{false,true})
                {
                    map.Write(switchCell,92,0);raster.RebuildObjects(map,visibility,fog);raster.CopyStaticBackdrop(closed);
                    map.Write(switchCell,92,1);raster.RebuildObjects(map,visibility,fog);raster.CopyStaticBackdrop(opened);
                    Check(!closed.AsSpan().SequenceEqual(opened),entry.Name+" switch "+switchCell+" must change wall pixels, fog="+fog);
                    raster.RefreshVisibility(map,visibility,fog);raster.CopyStaticBackdrop(restored);
                    Check(opened.AsSpan().SequenceEqual(restored),"visibility refresh must not resurrect removed walls");
                    map.Write(switchCell,92,0);raster.RebuildObjects(map,visibility,fog);raster.CopyStaticBackdrop(restored);
                    Check(closed.AsSpan().SequenceEqual(restored),"closing switch restores identical backdrop");
                }
                foreach(int cell in Enumerable.Range(0,100).Where(i=>map.Read(i,88)==4&&map.Read(i,92)==switchCell))
                {
                    int blocked=map.Read(cell,96);Check(blocked is 0 or 1,"binary gate condition");
                    map.Write(switchCell,92,blocked);Check(!map.CanEnter(cell%10,cell/10),"closed gate collision");
                    map.Write(switchCell,92,1-blocked);Check(map.CanEnter(cell%10,cell/10),"open gate collision");gates++;
                }
                map.Write(switchCell,92,initial);switches++;
            }
            maps++;objects+=conditional.Length;
        }
        // Exercise the same modal-confirm -> RebuildObjects route as the live session.
        byte[] fixture=archive.ReadMember(archive.FindResource("ed01.amf"u8));
        var liveMap=new DungeonMap(WorldMapContext.Parse(fixture));var liveAssets=DungeonSceneAssets.Load(root,liveMap.Context,fixture);
        var state=new GameState{WorldX=4,WorldY=4,DungeonFog=0};
        var controller=new DungeonController(state,liveMap,liveAssets.Base.CharacterAnimation,new LegacyRandom(1));
        var liveRaster=new DungeonSceneRenderer(liveAssets.Base,liveAssets.ObjectMarkers);
        var bindings=new DungeonBindings(203,205,200,208,46,1,15);
        controller.Input(bindings,_=>false,_=>false);state.DungeonFogDirty=0;
        controller.Input(bindings,_=>false,_=>false);state.ScriptActive=0;
        liveMap.Write(44,92,0);liveRaster.RebuildObjects(liveMap,controller.Visibility,false);
        ushort[] before=new ushort[640*480],after=new ushort[640*480];liveRaster.CopyStaticBackdrop(before);
        controller.OpenSwitch();controller.Input(bindings,s=>s==bindings.Left,_=>false);
        var commands=controller.Input(bindings,s=>s==bindings.Confirm,_=>false);
        Check(liveMap.Read(44,92)==1&&controller.Modal==0,"switch confirmation commits state");
        Check(commands.Count(c=>c.Kind==DungeonCommandKind.RebuildObjects)==1,"switch confirmation requests one rebuild");
        foreach(var command in commands)if(command.Kind==DungeonCommandKind.RebuildObjects)liveRaster.RebuildObjects(liveMap,controller.Visibility,false);
        liveRaster.CopyStaticBackdrop(after);Check(!before.AsSpan().SequenceEqual(after),"confirmed switch removes actual ED01 wall pixels");
        Check(maps>1&&switches>1&&objects>1&&gates>1,"real resource coverage");
        return $"Dungeon switch raster: {maps} maps, {switches} switches, {objects} conditional objects, {gates} linked gates; both states/fog modes, restoration, visibility refresh and modal-confirm route passed.";
    }
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("Dungeon switch raster: "+message);}
}
