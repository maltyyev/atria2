using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Town;

public sealed record TownSceneAssets(SodV2 Scene,SpriteFile? Objects,ushort[] Background565)
{
    // Native425dc0 strips extension, performs Binder lookup, then reads120-byte header +370-byte records.
    public static TownSceneAssets Load(string originalRoot,ReadOnlySpan<byte> sceneReference,bool loadObjects=true)
    {
        using var scenes=BinderArchive.Open(Path.Combine(originalRoot,"AT2SOD.000"));
        int sceneIndex=Find(scenes,sceneReference);
        var scene=SodV2.Parse(TownSceneRepairs.Normalize(scenes.Entries[sceneIndex].Name,scenes.ReadMember(sceneIndex)));
        SpriteFile? objects=null;
        if(loadObjects && scene.Count>0)
        {
            using var sprites=BinderArchive.Open(Path.Combine(originalRoot,"AT2SPR.000"));
            objects=SpriteFile.Decode(sprites.ReadMember(Find(sprites,scene.SpriteReference.Span)));
        }
        using var backgrounds=BinderArchive.Open(Path.Combine(originalRoot,"AT2BMP.000"));
        var background=BmpImage.Decode(backgrounds.ReadMember(Find(backgrounds,scene.BackgroundReference.Span)));
        if(background.Width!=640 || background.Height!=480) throw new InvalidDataException("Town background is not native640x480.");
        ushort[] pixels=new ushort[background.Pixels.Length];
        for(int i=0;i<pixels.Length;i++)
        {
            uint rgb=background.Pixels[i];
            pixels[i]=(ushort)(((rgb>>8)&0xf800)|((rgb>>5)&0x7e0)|((rgb>>3)&0x1f));
        }
        return new(scene,objects,pixels);
    }
    private static int Find(BinderArchive archive,ReadOnlySpan<byte> name)
    {
        int index=archive.FindResource(name);
        if(index<0) throw new InvalidDataException("Town resource reference is absent from its archive.");
        return index;
    }
}

public static class TownCatalog
{
    // Native44d4c0 DWORD table, used by41cfb0. Logical music ID, before supplied Track(n+1) mapping.
    public static ReadOnlySpan<int> Music => [1,10,9,9,10,14,12,11,12,18,9,10,16,17,12,12,13,10,14,17,12,14,17,16,13,17,17,10,16,14,18,14,13,14,15,13,12,16,13,9,17,10,13,14,14,16,17,18,14,9,19,1];
    public static ReadOnlyMemory<byte> Name(IReadOnlyList<ReadOnlyMemory<byte>> messages,int townId)
    {
        if((uint)townId>=52 || messages.Count<=119+townId) throw new ArgumentOutOfRangeException(nameof(townId));
        return messages[119+townId];
    }
}
