using Atria.Platform.Win32;
using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;

namespace Atria.Render.Scenes;

public static class MainMenuArtwork
{
    private const int Width = 640, Height = 480;
    private const int MaximumCachedLabels = 64;
    private const string FontFamily = "Georgia";
    private static readonly string[] keys = ["ui.mainMenu.new", "ui.mainMenu.load", "ui.mainMenu.save", "ui.mainMenu.quit", "ui.mainMenu.credits", "ui.mainMenu.settings"];
    private static readonly (int X, int Y, int Angle)[] anchors =
    [
        (181, 233, 0), (248, 315, -48), (394, 316, 48),
        (465, 233, 0), (384, 165, -48), (244, 165, 48)
    ];
    private static readonly Dictionary<(string Text, int Item), TextMask> labels = new();

    public static ushort[] Load(string assetRoot)
    {
        string path = Path.Combine(assetRoot, "interface", "ru-RU", "MainMenu.bmp");
        var image = BmpImage.Decode(File.ReadAllBytes(path));
        if ((long)image.Width * Height != (long)image.Height * Width)
        {
            throw new InvalidDataException("Main menu artwork must have a 4:3 aspect ratio.");
        }

        ushort[] source = image.ToRgb565(), result = new ushort[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                result[y * Width + x] = source[(y * image.Height / Height) * image.Width + x * image.Width / Width];
            }
        }
        return result;
    }

    public static void DrawLabels(int selected, Span<ushort> target, int width, int height, int stride, RasterClip? clip)
    {
        for (int item = 0; item < keys.Length; item++)
        {
            var mask = Label(item);
            var anchor = anchors[item];
            int x = anchor.X - mask.Width / 2 + DirectWriteRasterizer.MaskPadding;
            int y = anchor.Y - mask.Height / 2 + DirectWriteRasterizer.MaskPadding;
            UnicodeText.Draw(mask, target, width, height, stride, x + 1, y + 2, 0, clip);
            UnicodeText.Draw(mask, target, width, height, stride, x, y, item == selected ? (ushort)0xff76 : (ushort)0x9c09, clip);
        }
    }

    public static void DrawTitle(int item, int reveal, Span<ushort> target, int width, int height, int stride, RasterClip? clip)
    {
        const int maximumTitleWidth = 174, revealSteps = 8;
        string text = Caption(item);
        float size = 28;
        var mask = UnicodeText.Layout(text, size, FontFamily, bold: true);
        while (mask.Advance > maximumTitleWidth && size > 10)
        {
            mask = UnicodeText.Layout(text, --size, FontFamily, bold: true);
        }

        if (mask.Advance > maximumTitleWidth)
        {
            mask = Truncated(text, size, maximumTitleWidth);
        }

        RasterClip bounds = clip ?? new(0, 0, width, height);
        bounds = new(bounds.Left, bounds.Top, Math.Min(bounds.RightExclusive, 232 + 176 * (reveal + 1) / revealSteps), bounds.BottomExclusive);
        if (bounds.RightExclusive <= bounds.Left)
        {
            return;
        }
        int x = 320 - mask.Advance / 2;
        int y = 224 - (mask.Height - DirectWriteRasterizer.MaskPadding * 2) / 2;
        UnicodeText.Draw(mask, target, width, height, stride, x + 2, y + 2, 0, bounds);
        UnicodeText.Draw(mask, target, width, height, stride, x, y, 0xff76, bounds);
    }

    private static string Caption(int item) => GameText.Get(keys[item]);

    private static TextMask Label(int item)
    {
        string text = Caption(item);
        lock (labels)
        {
            if (labels.TryGetValue((text, item), out var cached))
            {
                return cached;
            }

            const int maximumLabelWidth = 66;
            float size = 15;
            var mask = UnicodeText.Layout(text, size, FontFamily, bold: true);
            while (mask.Advance > maximumLabelWidth && size > 8)
            {
                mask = UnicodeText.Layout(text, --size, FontFamily, bold: true);
            }
            if (mask.Advance > maximumLabelWidth)
            {
                mask = Truncated(text, size, maximumLabelWidth);
            }

            var result = Rotate(mask, anchors[item].Angle);
            if (labels.Count >= MaximumCachedLabels)
            {
                labels.Clear();
            }
            labels.Add((Caption(item), item), result);
            return result;
        }
    }

    private static TextMask Truncated(string text, float size, int maximumWidth)
    {
        string shortened = UnicodeText.Fit(text, maximumWidth, value => UnicodeText.Layout(value, size, FontFamily, bold: true).Advance);
        return UnicodeText.Layout(shortened, size, FontFamily, bold: true);
    }

    private static TextMask Rotate(TextMask source, int degrees)
    {
        if (degrees == 0)
        {
            return source;
        }

        double angle = degrees * Math.PI / 180, cosine = Math.Cos(angle), sine = Math.Sin(angle);
        int width = (int)Math.Ceiling(Math.Abs(source.Width * cosine) + Math.Abs(source.Height * sine));
        int height = (int)Math.Ceiling(Math.Abs(source.Width * sine) + Math.Abs(source.Height * cosine));
        byte[] coverage = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double dx = x - width / 2.0, dy = y - height / 2.0;
                int sx = (int)Math.Round(dx * cosine + dy * sine + source.Width / 2.0);
                int sy = (int)Math.Round(-dx * sine + dy * cosine + source.Height / 2.0);
                if ((uint)sx < source.Width && (uint)sy < source.Height)
                {
                    coverage[y * width + x] = source.Coverage[sy * source.Width + sx];
                }
            }
        }
        return new(width, height, source.Advance, coverage, source.MissingGlyphs, source.Lines);
    }
}
