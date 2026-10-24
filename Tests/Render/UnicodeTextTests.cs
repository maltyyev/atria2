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
        CheckMenuBounds();
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

    private static void CheckMenuBounds()
    {
        // Independent visible plaque rectangles measured on the text-free menu artwork.
        (int Left, int Top, int Right, int Bottom)[] bounds =
        [
            (148, 220, 216, 244), (220, 286, 276, 345), (366, 286, 424, 345),
            (432, 220, 500, 244), (366, 147, 408, 189), (229, 147, 273, 191)
        ];
        void Verify()
        {
            ushort[] inactive = new ushort[640 * 480];
            MainMenuArtwork.DrawLabels(-1, inactive, 640, 480, 640, null);
            for (int item = 0; item < bounds.Length; item++)
            {
                ushort[] selected = new ushort[640 * 480];
                MainMenuArtwork.DrawLabels(item, selected, 640, 480, 640, null);
                var box = bounds[item];
                Check(selected.Where((pixel, i) => pixel != inactive[i]).Any(), "Selected plaque is visibly highlighted");
                for (int i = 0; i < selected.Length; i++)
                {
                    if (selected[i] == inactive[i]) continue;
                    Check(i % 640 >= box.Left && i % 640 < box.Right && i / 640 >= box.Top && i / 640 < box.Bottom, "Caption stays on its plaque");
                }
                Array.Clear(selected);
                MainMenuArtwork.DrawTitle(item, 7, selected, 640, 480, 640, null);
                Check(selected.Where((pixel, i) => i % 640 < 232 || i % 640 >= 408 || i / 640 < 190 || i / 640 >= 250).All(pixel => pixel == 0), "Central caption fits its panel");
            }
        }
        Verify();
        var previous = GameText.Current;
        string directory = Path.Combine(Path.GetTempPath(), "atria-menu-bounds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "ru-RU"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "ru-RU", "menu.json"), """
                {"ui.mainMenu.new":"Начать совершенно новое приключение",
                 "ui.mainMenu.load":"Продолжить сохранённое приключение",
                 "ui.mainMenu.save":"Записать текущее состояние приключения",
                 "ui.mainMenu.quit":"Завершить приложение и вернуться на рабочий стол",
                 "ui.mainMenu.credits":"製作者についての詳細な情報",
                 "ui.mainMenu.settings":"Дополнительные настройки управления и звука"}
                """);
            GameText.Use(new LocalizationCatalog(directory));
            Verify();
        }
        finally
        {
            GameText.Use(previous);
            Directory.Delete(directory, recursive: true);
        }
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
