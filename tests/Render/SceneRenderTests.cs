using Atria.Render.Codecs;
using Atria.Render.Scenes;
using Atria.Resources;

namespace Atria.Tests.Render;

public static class SceneRenderTests
{
    public static void Run(string originalRoot)
    {
        using var sodArchive = BinderArchive.Open(Path.Combine(originalRoot, "AT2SOD.000"));
        using var menuSprites = BinderArchive.Open(Path.Combine(originalRoot, "AT2SPR.000"));
        using var animations = BinderArchive.Open(Path.Combine(originalRoot, "ANI.000"));
        var scene = SodV2.Parse(Read(sodArchive, "01MERCEN"u8));
        var townSprite = SpriteFile.Decode(Read(menuSprites, scene.SpriteReference.Span));
        ushort[] target = new ushort[640 * 480];
        int selectable = SodScenePresentation.SelectableCount(scene);
        Require(selectable > 0 && selectable <= scene.Count, "Town selectable count invalid.");
        for (int i = 0; i < selectable; i++)
        {
            SodScenePresentation.DrawSelected(scene, townSprite, i, target, 640, 480, 640);
            var anchor = SodScenePresentation.GetSelectionAnchor(scene, townSprite, i);
            Require(anchor.X >= 0 && anchor.X < 640 && anchor.Y >= 0 && anchor.Y < 480, "Town anchor outside native viewport.");
        }
        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(originalRoot, "eng1.fnt")), true);
        var etc = SpriteFile.Decode(Read(menuSprites, "ETC"u8));
        SodScenePresentation.DrawSelectionLabel(scene, townSprite, etc, font, 0, 0, 0, target, 640, 480, 640);
        int[] order = SodScenePresentation.BuildDepthOrder(scene, townSprite);
        Require(order.Distinct().Count() == scene.Count, "Depth order lost objects.");
        // Native adjacent-swap tolerance preserves depth10 before9, but moves7 before both.
        byte[] syntheticSod = new byte[SodV2.HeaderSize + 3 * SodV2.RecordSize];
        "Object Tool ver2.0"u8.CopyTo(syntheticSod);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(syntheticSod.AsSpan(30), 3);
        int[] bottoms = [10, 9, 7];
        for (int i = 0; i < 3; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                syntheticSod.AsSpan(SodV2.HeaderSize + i * SodV2.RecordSize + 4), bottoms[i] - 1);
        var syntheticSprite = new SpriteFile(false, [new SpriteFrame(0, 0, 0, [], [], 1, 1, [])]);
        Require(SodScenePresentation.BuildDepthOrder(SodV2.Parse(syntheticSod), syntheticSprite).SequenceEqual([2, 0, 1]),
            "Native one-pixel depth tolerance changed.");

        var menu = SpriteFile.Decode(Read(menuSprites, "MENU"u8));
        for (int item = 0; item < 6; item++)
        {
            MainMenuPresentation.DrawSelection(menu, item, target, 640, 480, 640);
            for (int reveal = 0; reveal < 8; reveal++)
                MainMenuPresentation.DrawPanel(menu, item, reveal, target, 640, 480, 640);
        }
        var flames = AnimationFile.Decode(Read(animations, "MENUFIRE"u8));
        var flameSprites = SpriteFile.Decode(Read(menuSprites, flames.SpriteReference));
        MainMenuPresentation.DrawFlames(flames, flameSprites, MainMenuPresentation.InitialFlameFrames, target, 640, 480, 640);
        var effects = AnimationFile.Decode(Read(animations, "CON_EFF"u8));
        var effectSprites = SpriteFile.Decode(Read(menuSprites, effects.SpriteReference));
        Require(TownEffectCatalog.Records.Length == 23, "Town effect literal table changed.");
        int[] effectFrames = new int[5], counters = new int[5];
        for (int town = 0; town < 52; town++)
        {
            Array.Clear(effectFrames); Array.Clear(counters);
            for (int tick = 0; tick < 160; tick++)
            {
                TownEffectCatalog.Draw565(town, effects, effectSprites, effectFrames, target, 640, 480, 640);
                TownEffectCatalog.Advance(town, effectFrames, counters);
            }
        }
        Console.WriteLine("Scene rendering: town selection/label, six menu selections, 48 panels, flame cursors, 23 town effect definitions passed.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
    private static byte[] Read(BinderArchive archive, ReadOnlySpan<byte> reference)
    {
        int index = archive.FindResource(reference);
        if (index < 0) throw new InvalidDataException("Scene test resource is absent: " + System.Text.Encoding.ASCII.GetString(reference));
        return archive.ReadMember(index);
    }
}

