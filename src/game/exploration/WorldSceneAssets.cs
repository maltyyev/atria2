using Atria.Game.World;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Exploration;

public sealed record WorldSceneAssets(ushort[] Ground565,SodV2? Objects,SpriteFile? ObjectSprites,
    SpriteFile Etc,AnimationFile CharacterAnimation,SpriteFile CharacterSprites)
{
    public static WorldSceneAssets Load(string root,WorldMapContext map,ReadOnlyMemory<byte> originalAmf)
    {
        var decoded=AmfMap.Parse(originalAmf);
        using var bmp=BinderArchive.Open(Path.Combine(root,"AT2BMP.000"));
        var image=BmpImage.Decode(Read(bmp,map.BackgroundReference.Span));
        if(image.Width!=640 || image.Height!=480) throw new InvalidDataException("World background must be640x480.");
        ushort[] pixels=new ushort[640*480];
        for(int i=0;i<pixels.Length;i++) { uint rgb=image.Pixels[i]; pixels[i]=(ushort)(((rgb>>8)&0xf800)|((rgb>>5)&0x7e0)|((rgb>>3)&31)); }
        using var spr=BinderArchive.Open(Path.Combine(root,"AT2SPR.000"));
        SpriteFile? objects=decoded.EmbeddedScene is {Count:>0} sod?SpriteFile.Decode(Read(spr,sod.SpriteReference.Span)):null;
        var etc=SpriteFile.Decode(Read(spr,"ETC"u8));
        using var ani=BinderArchive.Open(Path.Combine(root,"ANI.000"));
        var character=AnimationFile.Decode(Read(ani,"CHIPCHAR"u8));
        var sprites=SpriteFile.Decode(Read(spr,character.SpriteReference));
        return new(pixels,decoded.EmbeddedScene,objects,etc,character,sprites);
    }
    private static byte[] Read(BinderArchive archive,ReadOnlySpan<byte> name)
    { int index=archive.FindResource(name); if(index<0) throw new InvalidDataException("Required world render resource missing."); return archive.ReadMember(index); }
}
