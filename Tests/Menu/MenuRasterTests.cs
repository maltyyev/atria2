using Atria.Game.Menu;
using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;

namespace Atria.Tests.Menu;

public static class MenuRasterTests
{
    public static string Run(string root)
    {
        byte[] bits = new byte[4096];
        for (int row = 0; row < 16; row++)
        {
            bits[65 * 16 + row] = 0x80;
        }

        var font = BitmapFont.DecodeEng(bits, true);
        ushort[] palette = Enumerable.Range(1, 32).Select(i => (ushort)i).ToArray();
        ushort[] target = new ushort[640 * 480];
        UiRaster.DrawStyledText(font, "A"u8, palette, 0, target, 640, 480, 640, 10, 20);
        Check(target[20 * 640 + 10] == 1 && target[22 * 640 + 10] == 1 && target[23 * 640 + 10] == 3 && target[35 * 640 + 10] == 27, "small font native row-gradient schedule");
        Check(target[20 * 640 + 11] == 0, "transparent glyph bits");
        UiRaster.DrawStyledText(font, "A"u8, palette, 31, target, 640, 480, 640, -1, -2);
        target[0] = 0x0002;
        UiRaster.TintHalf(target, 640, 480, 640, 0, 0, 1, 1, 0x0002);
        Check(target[0] == 2, "half tint sums before shifting");
        SpriteFrame[] tiles = Enumerable.Range(0, 12).Select(i => new SpriteFrame(0, 0, 0, [], [], 1, 1, [new SpriteRun(0, 0, 0, [(ushort)(i + 1)], [])])).ToArray();
        Array.Clear(target);
        UiRaster.DrawPanel(new(false, tiles), 10, 20, 2, 1, target, arrows: true);
        Check(target[24 * 640 + 12] == 1 && target[24 * 640 + 106] == 2 && target[84 * 640 + 12] == 3, "panel corners");
        Check(target[24 * 640 + 42] == 5 && target[102 * 640 + 42] == 6, "panel horizontal edges");
        Check(target[52 * 640 + 14] == 7 && target[52 * 640 + 124] == 8, "panel vertical edges");
        Check(target[22 * 640 + 74] == 9 && target[99 * 640 + 74] == 10 && target[52 * 640 + 12] == 11 && target[52 * 640 + 121] == 12, "panel arrows");
        using var archive = BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
        var etc = SpriteFile.Decode(Read(archive, "ETC"u8));
        var actualFont = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "eng1.fnt")), true);
        var modal = new MainMenuModalRenderer(etc, actualFont, File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat")));
        foreach (bool yes in new[]
        {
            true,
            false
        }
        )
        {
            foreach (int flash in new[]
            {
                0,
                1,
                2
            }
            )
            {
                Array.Fill(target, (ushort)0x7bef);
                modal.Draw(new(false, 0, -1, -1, 7, true, yes, flash, [0, 4, 2, 8, 3, 5]), target);
                Check(target.Any(p => p != 0x7bef), "resource modal composition");
            }
        }

        return "Shared UI raster checks passed (native gradient rows, half rounding, panel tile coordinates, six actual menu modal states).";
    }

    private static byte[] Read(BinderArchive archive, ReadOnlySpan<byte> name)
    {
        int index = archive.FindExact(name);
        if (index < 0)
        {
            throw new InvalidDataException("Missing UI sprite.");
        }

        return archive.ReadMember(index);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
