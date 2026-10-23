using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Combat;

public sealed record CombatBattleAssets(int BackgroundWidth,int BackgroundHeight,ushort[] Background,
    ushort[] Menu,SpriteFile Faces,SpriteFile Items,SpriteFile Arrows,SpriteFile MagicIcons,SpriteFile Numbers)
{
    public CombatBackgroundLayer? Secondary { get; init; }
    public CombatCameraBounds CreateCamera(CombatSceneRuntime runtime,Action<int,int> translateParticles)
        =>new(runtime,new CombatScrollLayer(BackgroundWidth,BackgroundHeight,Secondary is null?1:2,Secondary is null?1:2),
            translateParticles,Secondary is null?null:new CombatScrollLayer(Secondary.Width,Secondary.Height));
    public static CombatBattleAssets Load(string root,CombatSceneDefinition scene)
    {
        using var bitmaps=BinderArchive.Open(Path.Combine(root,"BMP.000"));
        BmpImage background=Bitmap(bitmaps,scene.Source.BackgroundReference.Span),menu=Bitmap(bitmaps,"BATMENU.bmp"u8);
        CombatBackgroundLayer? secondary=null;
        ReadOnlySpan<byte> secondKey=scene.Source.Header.Span.Slice(81,13);
        int terminator=secondKey.IndexOf((byte)0);if(terminator>=0) secondKey=secondKey[..terminator];
        if(!secondKey.IsEmpty)
        {
            BmpImage image=Bitmap(bitmaps,secondKey);
            if(image.Width<639 || image.Height<400) throw new InvalidDataException("Invalid second battle bitmap dimensions.");
            secondary=new(image.Width,image.Height,Convert(image));
        }
        if(background.Width<639 || background.Height<400 || menu.Width!=640 || menu.Height!=80) throw new InvalidDataException("Invalid original battle bitmap dimensions.");
        using var sprites=BinderArchive.Open(Path.Combine(root,"SPR.000"));
        return new(background.Width,background.Height,Convert(background),Convert(menu),
            Sprite(sprites,"menuFace.spr"u8),Sprite(sprites,"item.spr"u8),Sprite(sprites,"menuArr.spr"u8),
            Sprite(sprites,"magicico.spr"u8),Sprite(sprites,"Number.spr"u8)) { Secondary=secondary };
    }
    private static SpriteFile Sprite(BinderArchive archive,ReadOnlySpan<byte> key)=>SpriteFile.Decode(archive.ReadMember(Find(archive,key)));
    private static BmpImage Bitmap(BinderArchive archive,ReadOnlySpan<byte> key)=>BmpImage.Decode(archive.ReadMember(Find(archive,key)));
    private static int Find(BinderArchive archive,ReadOnlySpan<byte> key)
    {
        int index=archive.FindResource(key);if(index<0) throw new FileNotFoundException("Actual combat display resource is missing.");return index;
    }
    private static ushort[] Convert(BmpImage bitmap)
    {
        ushort[] result=new ushort[bitmap.Pixels.Length];
        for(int i=0;i<result.Length;i++) { uint rgb=bitmap.Pixels[i];result[i]=(ushort)(((rgb>>8)&0xf800)|((rgb>>5)&0x7e0)|((rgb>>3)&31)); }
        return result;
    }
}
