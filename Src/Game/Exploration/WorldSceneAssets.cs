using Atria.Game.World;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Exploration;

public sealed record WorldSceneAssets(
    ushort[] Ground565,
    SodV2? Objects,
    SpriteFile? ObjectSprites,
    SpriteFile Etc,
    AnimationFile CharacterAnimation,
    SpriteFile CharacterSprites
)
{
    public static WorldSceneAssets Load(string root, WorldMapContext map, ReadOnlyMemory<byte> originalAmf)
    {
        var decoded = AmfMap.Parse(originalAmf);
        using var bmp = BinderArchive.Open(Path.Combine(root, "AT2BMP.000"));
        var image = BmpImage.Decode(Read(bmp, map.BackgroundReference.Span));
        if (image.Width != 640 || image.Height != 480)
        {
            throw new InvalidDataException("World background must be640x480.");
        }

        ushort[] pixels = image.ToRgb565();
        using var spr = BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
        SpriteFile? objects = decoded.EmbeddedScene is { Count: > 0 } sod ? SpriteFile.Decode(Read(spr, sod.SpriteReference.Span)) : null;
        var etc = SpriteFile.Decode(Read(spr, "ETC"u8));
        using var ani = BinderArchive.Open(Path.Combine(root, "ANI.000"));
        var character = AnimationFile.Decode(Read(ani, "CHIPCHAR"u8));
        var sprites = SpriteFile.Decode(Read(spr, character.SpriteReference));
        return new(pixels, decoded.EmbeddedScene, objects, etc, character, sprites);
    }

    private static byte[] Read(BinderArchive archive, ReadOnlySpan<byte> name)
    {
        int index = archive.FindResource(name);
        if (index < 0)
        {
            throw new InvalidDataException("Required world render resource missing.");
        }

        return archive.ReadMember(index);
    }
}
