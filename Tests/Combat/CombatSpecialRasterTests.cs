using Atria.Game.Combat;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Combat;

public static class CombatSpecialRasterTests
{
    public static string Run(string root)
    {
        ushort[] pixels = new ushort[640 * 480];
        var clip = new RasterClip(0, 0, 639, 400);
        CombatSpecialRaster.DrawCrosshair(pixels, 640, clip);
        Check(pixels.Count(p => p == 0xf98c) == 5 && pixels[240 * 640 + 319] == 0xf98c && pixels[239 * 640 + 320] == 0xf98c
&& pixels[241 * 640 + 320] == 0xf98c, "Exact fixed five-pixel cross.");
        CombatSpecialRaster.DrawCrosshair(pixels, 640, new(0, 0, 1, 1));
        Array.Fill(pixels, (ushort)0x001f);
        var sprite = new SpriteFrame(0, 0, 0, [], [], 2, 1, [new(0, 0, 0, [0xf800, 0x07e0], [])]);
        CombatSpecialRaster.DrawHalfAll(sprite, pixels, 640, 10, 20, false, clip);
        Check(pixels[20 * 640 + 10] == SpriteRaster.Half565(0xf800, 0x001f) && pixels[20 * 640 + 11] == SpriteRaster.Half565(0x07e0, 0x001f), "Mode2 halves even opcode0.");
        Array.Fill(pixels, (ushort)0x001f);
        CombatSpecialRaster.DrawHalfAll(sprite, pixels, 640, 10, 20, true, clip);
        Check(pixels[20 * 640 + 12] == SpriteRaster.Half565(0xf800, 0x001f) && pixels[20 * 640 + 11] == SpriteRaster.Half565(0x07e0, 0x001f)
&& pixels[20 * 640 + 10] == 0x001f, "438eb0 mirrored origin is x+width.");
        Array.Fill(pixels, (ushort)0x001f);
        CombatSpecialRaster.DrawHalfAll(sprite, pixels, 640, -1, 20, false, new(0, 0, 0, 400));
        Check(pixels.All(p => p == 0x001f), "Bounded clipping never writes outside destination clip.");
        ushort[] backdrop = Enumerable.Repeat((ushort)0x1234, 640 * 480).ToArray();
        var layer = new CombatBackgroundLayer(640, 480, new ushort[640 * 480]);
        layer.Draw(backdrop, new CombatScrollLayer(640, 480));
        Check(backdrop[0] == 0 && backdrop[398 * 640 + 638] == 0 && backdrop[639] == 0x1234 && backdrop[400 * 640] == 0x1234, "Second-layer black is opaque; exclusive639x400 source leaves final column/menu untouched.");
        var slow = new CombatScrollLayer(700, 480, 2, 2);
        var fast = new CombatScrollLayer(700, 480);
        slow.MoveX(2);
        fast.MoveX(2);
        Check(slow.X == 0 && fast.X == 2, "Two-layer first logical camera move.");
        slow.MoveX(2);
        fast.MoveX(2);
        Check(slow.X == 2 && fast.X == 4, "Two-layer divisor counters are independent.");
        using var archive = BinderArchive.Open(Path.Combine(root, "SOD.000"));
        byte[] sceneBytes = archive.ReadMember(archive.FindResource("SBATTL02"u8));
        var actual = new CombatSceneDefinition(new(FixedEncounterKind.Scripted, 2), SodV2.Parse(sceneBytes));
        var assets = CombatBattleAssets.Load(root, actual);
        Check(assets.Secondary is null && assets.BackgroundWidth == 830, "Actual encounter has one background.");
        //Constructed header fixture uses a real original bitmap; it is not claimed to be an original two-layer scene.
        sceneBytes.AsSpan(34, 13).CopyTo(sceneBytes.AsSpan(81, 13));
        var constructed = new CombatSceneDefinition(new(FixedEncounterKind.Scripted, 2), SodV2.Parse(sceneBytes));
        var two = CombatBattleAssets.Load(root, constructed);
        Check(two.Secondary is not null && two.Secondary.Width == 830 && two.Secondary.Pixels.SequenceEqual(two.Background), "Constructed two-layer header resolves the original resource through the same raw-key lookup.");
        return "Combat special raster: exact fixed cross, half-all opcode0 and mirrored endpoint passed.";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
