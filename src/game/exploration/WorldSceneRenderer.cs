using System.Buffers.Binary;
using Atria.Game.State;
using Atria.Game.World;
using Atria.Render.Codecs;
using Atria.Render.Scenes;

namespace Atria.Game.Exploration;

public readonly record struct WorldRevealPose(int X,int Y,int SpriteFrame);

public sealed partial class WorldSceneRenderer
{
    public const int NativeObjectCapacity=200; //41ab90 zeroes200 records, including slots beyond loaded SOD count.
    private readonly WorldSceneAssets assets;
    private readonly int[] depth;
    private readonly int[] conditional;
    private readonly ushort[] cached=new ushort[640*480];
    private bool ready;
    private int revealFrame,revealCounter;
    public WorldSceneRenderer(WorldSceneAssets assets)
    {
        this.assets=assets;
        if(assets.Ground565.Length!=cached.Length || assets.Etc.Frames.Length<=47) throw new InvalidDataException("World render assets incomplete.");
        int count=assets.Objects?.Count??0;
        if(count>NativeObjectCapacity) throw new InvalidDataException("World SOD exceeds native200-record bank.");
        conditional=new int[NativeObjectCapacity];
        for(int i=0;i<count;i++) conditional[i]=assets.Objects!.GetObject(i).GetInteger(8);
        depth=count>0?SodScenePresentation.BuildDepthOrder(assets.Objects!,assets.ObjectSprites??throw new InvalidDataException("World SOD sprites missing.")):[];
    }
    //41b980: rebuild only at native background/visibility refresh boundaries, not GPU refresh.
    public void RebuildBase(WorldMapContext map,GameState state)
    {
        assets.Ground565.CopyTo(cached,0);
        foreach(int index in depth)
        {
            var item=assets.Objects!.GetObject(index);
            if(conditional[index]==1)
            {
                int eventIndex=checked(item.GetInteger(10)+item.GetInteger(9)*10);
                if((uint)eventIndex>=(uint)state.Events.Length) throw new InvalidDataException("World SOD conditional event exceeds bank.");
                if(state.Events[eventIndex]!=1) continue;
                conditional[index]=0; //425d30..425d3a: event==1 clears raw integer8, then draws/unlocks the object.
            }
            SpriteRaster.Draw565(SodScenePresentation.GetFrame(item,assets.ObjectSprites!),cached,640,480,640,item.GetInteger(0),item.GetInteger(1));
        }
        var tiles=map.TileBytes; var header=map.HeaderBytes;
        int tileW=BinaryPrimitives.ReadInt32LittleEndian(header[21..]),tileH=BinaryPrimitives.ReadInt32LittleEndian(header[25..]);
        for(int y=0;y<map.Height;y++) for(int x=0;x<map.Width;x++)
        {
            if(tiles[(y*map.Width+x)*328+3]!=2) continue;
            for(int pass=0;pass<2;pass++) SpriteRaster.Draw565(assets.Etc.Frames[16],cached,640,480,640,
                checked(tileW*x),checked(tileH*y),SpriteBlend.HalfAll);
        }
        //41b5e0 explicitly clears bottom and right single-pixel edges after fog.
        cached.AsSpan(479*640,640).Clear(); for(int y=0;y<480;y++) cached[y*640+639]=0;
        ready=true;
    }
    //41b9d0's first test, intended callback for ExplorationArrival which already tests tile integer8.
    public bool IsSceneReferenceBlocked(int objectIndex)
    {
        if((uint)objectIndex>=(uint)conditional.Length) throw new InvalidDataException("World tile references absent SOD object.");
        return conditional[objectIndex]==0;
    }
    public void ResetRevealAnimation() { revealCounter=revealFrame=0; }
    public void CopyStaticBackdrop(Span<ushort> target)
    {
        if(!ready) throw new InvalidOperationException("World static background not built.");
        if(target.Length!=cached.Length) throw new ArgumentException("World backdrop requires640x480 surface.");
        cached.CopyTo(target);
    }
    public WorldRevealPose? AdvanceReveal(WorldMapContext map,int worldX,int worldY,int revealX,int revealY,bool visible=true)
    {
        if(!visible) return null;
        if((uint)revealX>=10 || (uint)revealY>=10 || (uint)worldX>=10 || (uint)worldY>=10) throw new ArgumentOutOfRangeException(nameof(revealX));
        int selected=revealY*map.Width+revealX,current=worldY*map.Width+worldX;
        var tile=map.TileBytes.Slice(selected*328,328);
        bool animated=tile[1]==1 && tile[0]==0 && (selected==current-1||selected==current+1||selected==current-map.Width||selected==current+map.Width);
        int frame=38+(animated?revealFrame:0);
        if(animated && ++revealCounter>2) { revealCounter=0; if(++revealFrame>9) revealFrame=0; }
        return new(checked(BinaryPrimitives.ReadInt32LittleEndian(map.HeaderBytes[21..])*revealX),
            checked(BinaryPrimitives.ReadInt32LittleEndian(map.HeaderBytes[25..])*revealY),frame);
    }
    public void Draw(ExplorationPose character,WorldRevealPose? reveal,Span<ushort> target)
    {
        if(!ready) throw new InvalidOperationException("Rebuild world base before drawing.");
        if(target.Length!=cached.Length) throw new ArgumentException("World requires native640x480 surface.");
        cached.CopyTo(target);
        //Mode310 draws reveal cursor BEFORE the mode300 character callback.
        if(reveal is {} cursor) SpriteRaster.Draw565(assets.Etc.Frames[cursor.SpriteFrame],target,640,480,640,cursor.X,cursor.Y);
        var frames=assets.CharacterAnimation.Sequences;
        if((uint)character.Sequence>=(uint)frames.Length || (uint)character.Frame>=(uint)frames[character.Sequence].Frames.Length)
            throw new InvalidDataException("World character ANI cursor invalid.");
        var aniFrame=frames[character.Sequence].Frames[character.Frame];
        if(aniFrame.SpriteFrameIndex>=assets.CharacterSprites.Frames.Length) throw new InvalidDataException("World character SPR cursor invalid.");
        DrawShadow(assets.CharacterSprites.Frames[aniFrame.SpriteFrameIndex],target,checked(character.X+aniFrame.OffsetX),checked(character.Y+aniFrame.OffsetY));
        AnimationRaster.Draw565(assets.CharacterAnimation,assets.CharacterSprites,character.Sequence,character.Frame,target,640,480,640,character.X,character.Y,SpriteBlend.HalfType3);
    }
    //439240->4388b0: black silhouette compressed to quarter-height and diagonally shifted, not a generic ellipse.
    public static void DrawShadow(SpriteFrame sprite,Span<ushort> target,int x,int y)
    {
        if(target.Length!=640*480) throw new ArgumentException("World shadow requires native640x480 surface.");
        int quarter=sprite.Height>>2;
        foreach(var run in sprite.Runs)
        {
            int advance=(run.Y+3)/4;
            long dy=(long)y+quarter*3+advance,dx=(long)x-quarter+advance+run.X;
            if(dy<0 || dy>=480) continue;
            for(int i=0;i<run.Colors.Length;i++) if(dx+i>=0 && dx+i<640) target[(int)dy*640+(int)(dx+i)]=0;
        }
    }
}
