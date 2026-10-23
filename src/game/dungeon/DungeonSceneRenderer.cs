using System.Buffers.Binary;
using Atria.Game.Exploration;
using Atria.Render.Codecs;
using Atria.Render.Scenes;

namespace Atria.Game.Dungeon;

/// <summary>Cached dungeon ground/SOD/black cells41e790/41e950 and dynamic object markers41fa20.</summary>
public sealed partial class DungeonSceneRenderer
{
    private readonly WorldSceneAssets assets;
    private readonly SpriteFile objects;
    private readonly int[] depth;
    private readonly ushort[] cached=new ushort[640*480],unmasked=new ushort[640*480];
    private int markerFrame,markerCalls;
    private bool ready;
    public DungeonSceneRenderer(WorldSceneAssets assets,SpriteFile dungeonObjects)
    {
        this.assets=assets;objects=dungeonObjects;
        depth=assets.Objects is {Count:>0} sod?SodScenePresentation.BuildDepthOrder(sod,assets.ObjectSprites??throw new InvalidDataException("Dungeon SOD sprites missing.")):[];
        if(assets.Ground565.Length!=cached.Length || objects.Frames.Length<=21) throw new InvalidDataException("Dungeon raster assets incomplete.");
    }
    public void RebuildObjects(DungeonMap map,DungeonVisibility visibility,bool fog)
    {
        assets.Ground565.CopyTo(unmasked,0);
        foreach(int index in depth)
        {
            var item=assets.Objects!.GetObject(index);
            //41ac70 loads integer0 at4b4048;41e790 reads4b4058/5c/60 = fields4/5/6.
            //World exploration uses a different condition tuple (fields8/9/10).
            if(item.GetInteger(4)==1)
            {
                int condition=item.GetInteger(6);
                if(condition is 0 or 1) {if(map.Read(item.GetInteger(5),92)!=condition) continue;}
                //Nonmatching or unknown conditional integer6 skips the object.
                else continue;
            }
            SpriteRaster.Draw565(SodScenePresentation.GetFrame(item,assets.ObjectSprites!),unmasked,640,480,640,item.GetInteger(0),item.GetInteger(1),SpriteBlend.Copy);
        }
        RefreshVisibility(map,visibility,fog);ready=true;
    }
    public void RefreshVisibility(DungeonMap map,DungeonVisibility visibility,bool fog)
    {
        unmasked.CopyTo(cached,0);
        if(!fog) return;
        var header=map.Context.GetHeader();int tileWidth=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(21,4)),tileHeight=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(25,4));
        for(int y=0;y<map.Context.Height;y++) for(int x=0;x<map.Context.Width;x++)
        {
            if(visibility.Values[y*map.Context.Width+x]!=0) continue;
            int x0=x*tileWidth,y0=y*tileHeight;
            for(int yy=Math.Max(0,y0);yy<=Math.Min(479,y0+47);yy++) cached.AsSpan(yy*640+Math.Max(0,x0),Math.Max(0,Math.Min(640,x0+64)-Math.Max(0,x0))).Clear();
        }
        cached.AsSpan(479*640,640).Clear();for(int y=0;y<480;y++) cached[y*640+639]=0;
    }
    public void CopyStaticBackdrop(Span<ushort> target)
    {if(!ready) throw new InvalidOperationException("Dungeon cached background not built.");if(target.Length!=cached.Length) throw new ArgumentException("Dungeon surface must be640x480.");cached.CopyTo(target);}
    public void DrawMarkers(DungeonMap map,DungeonVisibility visibility,bool fog,Span<ushort> target)
    {
        for(int i=0;i<100;i++)
        {
            if(fog && visibility.Values[i]<1) continue;
            int frame,x=i%10*64,y=i/10*48;
            switch(map.Read(i,88))
            {
                case 1:frame=markerFrame+4;x+=22;y+=14;break;
                case 2:frame=markerFrame+8;x+=22;y+=14;break;
                case 3:bool open=map.Read(i,92)!=0;frame=open?3:2;x+=19;y+=open?13:12;break;
                case 6:frame=map.Read(i,104)==0?1:0;x+=20;y+=10;break;
                case 9:frame=markerFrame+18;x+=25;y+=10;break;
                default:continue;
            }
            SpriteRaster.Draw565(objects.Frames[frame],target,640,480,640,x,y);
        }
        if(++markerCalls>4) {markerCalls=0;if(++markerFrame>3) markerFrame=0;}
    }
    private int torchFrame,torchCalls;
    public int TorchAnimationCounter=>torchCalls;
    public void DrawTorches(DungeonVisibility visibility,AnimationFile animation,SpriteFile sprites,int hudY,bool hudEnabled,Span<ushort> target)
    {
        foreach(var torch in visibility.Torches)
        {
            if(!torch.Active || (torch.Y==9&&hudY==420&&hudEnabled) || (torch.Y==0&&hudY==0&&hudEnabled)) continue;
            var frame=animation.Sequences[0].Frames[torchFrame];var sprite=sprites.Frames[frame.SpriteFrameIndex];
            int x=torch.X*64+48,y=torch.Y*48-10;
            WorldSceneRenderer.DrawShadow(sprite,target,x+frame.OffsetX,y+frame.OffsetY);
            AnimationRaster.Draw565(animation,sprites,0,torchFrame,target,640,480,640,x,y,SpriteBlend.Copy);
        }
        if(++torchCalls>2) {torchCalls=0;if(++torchFrame>5) torchFrame=0;}
    }
    public void DrawCharacter(ExplorationPose pose,Span<ushort> target)
    {
        var frame=assets.CharacterAnimation.Sequences[pose.Sequence].Frames[pose.Frame];
        WorldSceneRenderer.DrawShadow(assets.CharacterSprites.Frames[frame.SpriteFrameIndex],target,pose.X+frame.OffsetX,pose.Y+frame.OffsetY);
        AnimationRaster.Draw565(assets.CharacterAnimation,assets.CharacterSprites,pose.Sequence,pose.Frame,target,640,480,640,pose.X,pose.Y,SpriteBlend.HalfType3);
    }
    //207d0 probes flattened visibility beyond the100-cell bank. Caller supplies exact adjacent globals/torch aliases.
    public void DrawDarkness(int x,int y,Func<int,int> readNativeVisibility,Span<ushort> target)
    {
        ReadOnlySpan<(int Offset,int X,int Y)> pieces=[(-21,-64,-96),(-20,0,-96),(-19,64,-96),(-12,-128,-48),(-8,129,-48),(-2,-128,0),(2,164,0),(8,-128,48),(12,129,48),(19,-64,96),(20,0,123),(21,64,97)];
        int current=x+y*10;
        for(int i=0;i<pieces.Length;i++)
        {
            var p=pieces[i];if(readNativeVisibility(current+p.Offset)==2) continue;
            SpriteRaster.Draw565(assets.Etc.Frames[26+i],target,640,480,640,x*64+p.X,y*48+p.Y);
        }
    }
}
