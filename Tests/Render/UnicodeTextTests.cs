using System.Globalization;
using Atria.Game.Controls;
using Atria.Localization;
using Atria.Platform.Win32;
using Atria.Render.Codecs;
using Atria.Render.Scenes;
using Atria.Render.UI;

namespace Atria.Tests.Render;

public static class UnicodeTextTests
{
    public static string Run(string resources)
    {
        string output = Path.Combine(resources, "..", "audit", "unicode");
        Directory.CreateDirectory(output);
        using var raster = new DirectWriteRasterizer();
        string[] samples = ["Unicode: café — Ελληνικά", "中文 日本語 한국어", "مرحبا بالعالم 123", "שָׁלוֹם", "e\u0301 — 👩‍💻"];
        ushort[] samplePixels = new ushort[640 * 480];
        for (int i = 0; i < samples.Length; i++)
        {
            var mask = raster.Render(samples[i], 24);
            Check(mask.MissingGlyphs == 0 && mask.Coverage.Any(a => a > 0), "Installed fonts cover sample " + samples[i]);
            UnicodeText.Draw(mask, samplePixels, 640, 480, 640, 30, 30 + i * 60, 0xffff);
        }
        SaveBitmap(Path.Combine(output, "unicode-samples.bmp"), samplePixels);

        var letter = raster.Render("F", 40, "Arial");
        int[] rows = Enumerable.Range(0, letter.Height).Select(y => letter.Coverage.Skip(y * letter.Width).Take(letter.Width).Count(a => a > 32)).Where(n => n > 0).ToArray();
        Check(rows.Take(3).Sum() > rows.TakeLast(3).Sum() * 2, "Top-down DIB orientation: F top arm is wider than its stem");
        Check(raster.Render("word word word word", 18, wrapWidth: 60).Lines > 1, "Measured wrapping");
        Check(ReferenceEquals(UnicodeText.Layout(samples[0], 20), UnicodeText.Layout(samples[0], 20)), "Repeated draws reuse mask");
        Check(UnicodeText.Fit("👩‍💻e\u0301XY", 30, text => new StringInfo(text).LengthInTextElements * 10) == "👩‍💻e\u0301…", "Fitting preserves grapheme clusters");
        Check(UnicodeText.Fit("long", 1, text => text.Length * 8) == "", "Too narrow for ellipsis");

        var clipped = Enumerable.Repeat((ushort)0x1234, 640 * 480).ToArray();
        UnicodeText.Draw(letter, clipped, 640, 480, 640, 4, 4, 0xffff, new(10, 10, 20, 20));
        Check(clipped.Where((_, i) => i % 640 < 10 || i % 640 >= 20 || i / 640 < 10 || i / 640 >= 20).All(p => p == 0x1234), "Clipping cannot write outside bounds");

        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(resources, "ENG1.FNT")), true);
        ushort[] original = new ushort[640 * 480], modern = new ushort[640 * 480];
        const string russian = "Роланд: 123";
        font.DrawText(GameText.Encoding.GetBytes(russian), original, 640, 480, 640, 20, 20, 0xffff);
        font.DrawText(russian, modern, 640, 480, 640, 20, 20, 0xffff);
        Check(original.SequenceEqual(modern), "Existing Russian glyph pixels remain exact");
        ControlPanel.Text(font, modern, samples[1], 20, 50, 0xffff);
        Check(!original.SequenceEqual(modern), "Unicode reaches actual control panel renderer");

        ushort[] background = MainMenuArtwork.Load(Path.Combine(AppContext.BaseDirectory, "assets"));
        ulong previous = 0;
        for (int item = 0; item < 6; item++)
        {
            for (int reveal = 0; reveal < 8; reveal++)
            {
                var pixels = background.ToArray();
                MainMenuPresentation.DrawSelection(item, pixels, 640, 480, 640);
                MainMenuPresentation.DrawPanel(item, reveal, pixels, 640, 480, 640);
                if (reveal == 7)
                {
                    ulong checksum = pixels.Aggregate(0UL, (sum, pixel) => unchecked(sum * 31 + pixel));
                    Check(checksum != previous, "Menu selection changes visible labels");
                    previous = checksum;
                    SaveBitmap(Path.Combine(output, $"menu-{item}.bmp"), pixels);
                }
            }
        }
        return "Unicode: native shaping/fallback, top-down masks, grapheme fitting, wrapping, clipping, cache, Russian pixel parity and 48 menu frames passed.";
    }

    private static void SaveBitmap(string path, ushort[] pixels)
    {
        const int width = 640, height = 480, headerBytes = 54, bytesPerPixel = 3;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4d42);
        writer.Write(headerBytes + width * height * bytesPerPixel);
        writer.Write(0);
        writer.Write(headerBytes);
        writer.Write(40);
        writer.Write(width);
        writer.Write(-height);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(new byte[24]);
        foreach (ushort pixel in pixels)
        {
            writer.Write((byte)((pixel & 31) * 255 / 31));
            writer.Write((byte)(((pixel >> 5) & 63) * 255 / 63));
            writer.Write((byte)((pixel >> 11) * 255 / 31));
        }
    }

    private static void Check(bool condition, string reason)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Unicode: " + reason);
        }
    }
}
