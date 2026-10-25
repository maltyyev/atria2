using Atria.Platform.Win32;
using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;

namespace Atria.Render.Scenes;

public static class MainMenuArtwork
{
    private const string LabelFont = "Palatino Linotype";
    private static readonly string[] keys = ["ui.mainMenu.new", "ui.mainMenu.load", "ui.mainMenu.save", "ui.mainMenu.quit", "ui.mainMenu.credits", "ui.mainMenu.settings"];
    private static readonly Dictionary<(string Text, int Width, int Height, float Size), TextMask> labels = new();
    private const ushort Gold = 0xe653, SelectedGold = 0xff79, SelectedBorder = 0xfe4b, DisabledBronze = 0x8bc9;

    public static ushort[] LoadNative(string assetRoot)
    {
        var image = BmpImage.Decode(File.ReadAllBytes(Path.Combine(assetRoot, "interface", "MainMenuTemple.bmp")));
        if (image.Width != MainMenuLayout.Width || image.Height != MainMenuLayout.Height)
            throw new InvalidDataException("Temple menu artwork must be 2560x1440.");
        return image.ToRgb565();
    }

    // Legacy scene snapshots retain their 640x480 contract, with the wide menu letterboxed.
    public static ushort[] Load(string assetRoot)
    {
        var source = LoadNative(assetRoot);
        ushort[] result = new ushort[640 * 480];
        for (int y = 0; y < 360; y++)
            for (int x = 0; x < 640; x++)
                result[(y + 60) * 640 + x] = source[y * 4 * MainMenuLayout.Width + x * 4];
        return result;
    }

    public static void DrawLabels(int selected, Span<ushort> target, int width, int height, int stride, RasterClip? clip, bool saveDisabled = false)
    {
        var viewport = Viewport.Fit(width, height, MainMenuLayout.Width, MainMenuLayout.Height);
        foreach (int item in MainMenuLayout.Order)
        {
            var original = MainMenuLayout.Button(item);
            var box = new RasterClip(
                viewport.X + original.Left * viewport.Width / MainMenuLayout.Width,
                viewport.Y + original.Top * viewport.Height / MainMenuLayout.Height,
                viewport.X + original.RightExclusive * viewport.Width / MainMenuLayout.Width,
                viewport.Y + original.BottomExclusive * viewport.Height / MainMenuLayout.Height);
            int inset = Math.Max(2, viewport.Width * 22 / MainMenuLayout.Width);
            var inside = new RasterClip(box.Left + inset, box.Top + inset, box.RightExclusive - inset, box.BottomExclusive - inset);
            if (inside.RightExclusive <= inside.Left || inside.BottomExclusive <= inside.Top)
                continue;
            bool disabled = saveDisabled && item == MainMenuLayout.SaveItem;
            if (item == selected && !disabled)
                Border(target, width, height, stride, box, SelectedBorder, Math.Max(1, viewport.Width / 640), clip);
            DrawCentered(GameText.Get(keys[item]), target, width, height, stride, inside,
                78f * viewport.Width / MainMenuLayout.Width, disabled ? DisabledBronze : item == selected ? SelectedGold : Gold, clip);
        }
    }

    public static void DrawCentered(string text, Span<ushort> target, int width, int height, int stride, RasterClip box, float size, ushort color, RasterClip? clip = null)
    {
        int maximumWidth = box.RightExclusive - box.Left, maximumHeight = box.BottomExclusive - box.Top;
        TextMask mask;
        lock (labels)
        {
            var key = (text, maximumWidth, maximumHeight, size);
            if (!labels.TryGetValue(key, out mask!))
            {
                mask = Tighten(UnicodeText.Layout(text, size, LabelFont));
                while ((mask.Advance > maximumWidth || mask.Height - DirectWriteRasterizer.MaskPadding * 2 > maximumHeight) && size > 8)
                    mask = Tighten(UnicodeText.Layout(text, --size, LabelFont));
                if (mask.Advance > maximumWidth)
                {
                    string fitted = UnicodeText.Fit(text, maximumWidth, value => Tighten(UnicodeText.Layout(value, size, LabelFont)).Advance);
                    mask = Tighten(UnicodeText.Layout(fitted, size, LabelFont));
                }
                if (labels.Count >= 64)
                    labels.Clear();
                labels[key] = mask;
            }
        }
        int x = box.Left + (maximumWidth - mask.Advance) / 2;
        int y = box.Top + (maximumHeight - mask.Height) / 2 + DirectWriteRasterizer.MaskPadding;
        if (clip is { } limit)
            box = new(Math.Max(box.Left, limit.Left), Math.Max(box.Top, limit.Top), Math.Min(box.RightExclusive, limit.RightExclusive), Math.Min(box.BottomExclusive, limit.BottomExclusive));
        if (box.RightExclusive <= box.Left || box.BottomExclusive <= box.Top)
            return;
        UnicodeText.Draw(mask, target, width, height, stride, x + 2, y + 2, 0, box);
        UnicodeText.Draw(mask, target, width, height, stride, x, y, color, box);
    }

    public static void Border(Span<ushort> target, int width, int height, int stride, RasterClip box, ushort color, int thickness, RasterClip? clip = null)
    {
        var limit = clip ?? new RasterClip(0, 0, width, height);
        for (int y = Math.Max(limit.Top, box.Top); y < Math.Min(limit.BottomExclusive, box.BottomExclusive); y++)
            for (int x = Math.Max(limit.Left, box.Left); x < Math.Min(limit.RightExclusive, box.RightExclusive); x++)
                if (x < box.Left + thickness || x >= box.RightExclusive - thickness || y < box.Top + thickness || y >= box.BottomExclusive - thickness)
                    target[y * stride + x] = color;
    }
    // Font line boxes include unused ascender/descender space; center the visible ink on each plaque.
    private static TextMask Tighten(TextMask source)
    {
        int left = source.Width, top = source.Height, right = -1, bottom = -1;
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                if (source.Coverage[y * source.Width + x] == 0)
                    continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }
        if (right < left)
            return source;
        int padding = DirectWriteRasterizer.MaskPadding;
        int inkWidth = right - left + 1, inkHeight = bottom - top + 1;
        int width = inkWidth + padding * 2, height = inkHeight + padding * 2;
        byte[] coverage = new byte[width * height];
        for (int y = 0; y < inkHeight; y++)
        {
            source.Coverage.AsSpan((top + y) * source.Width + left, inkWidth).CopyTo(coverage.AsSpan((y + padding) * width + padding));
        }
        return new(width, height, inkWidth, coverage, source.MissingGlyphs, source.Lines);
    }

}
