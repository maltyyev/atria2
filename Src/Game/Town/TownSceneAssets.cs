using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game.Town;

public sealed record TownSceneAssets(SodV2 Scene, SpriteFile? Objects, ushort[] Background565)
{
    public string ResourceId { get; init; } = "";

    // Native425dc0 strips extension, performs Binder lookup, then reads120-byte header +370-byte records.
    public static TownSceneAssets Load(string originalRoot, ReadOnlySpan<byte> sceneReference, bool loadObjects = true)
    {
        using var scenes = BinderArchive.Open(Path.Combine(originalRoot, "AT2SOD.000"));
        int sceneIndex = Find(scenes, sceneReference);
        var scene = SodV2.Parse(TownSceneRepairs.Normalize(scenes.Entries[sceneIndex].Name, scenes.ReadMember(sceneIndex)));
        SpriteFile? objects = null;
        if (loadObjects && scene.Count > 0)
        {
            using var sprites = BinderArchive.Open(Path.Combine(originalRoot, "AT2SPR.000"));
            objects = SpriteFile.Decode(sprites.ReadMember(Find(sprites, scene.SpriteReference.Span)));
        }

        using var backgrounds = BinderArchive.Open(Path.Combine(originalRoot, "AT2BMP.000"));
        var background = BmpImage.Decode(backgrounds.ReadMember(Find(backgrounds, scene.BackgroundReference.Span)));
        if (background.Width != 640 || background.Height != 480)
        {
            throw new InvalidDataException("Town background is not native640x480.");
        }

        ushort[] pixels = background.ToRgb565();
        return new(scene, objects, pixels) { ResourceId = scenes.Entries[sceneIndex].Name };
    }

    private static int Find(BinderArchive archive, ReadOnlySpan<byte> name)
    {
        int index = archive.FindResource(name);
        if (index < 0)
        {
            throw new InvalidDataException("Town resource reference is absent from its archive.");
        }

        return index;
    }
}
