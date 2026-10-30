using System.Buffers.Text;
using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;

namespace Atria.Game.Exploration;

public sealed class WorldHudRenderer
{
    private readonly ushort[] panel;
    private readonly SpriteFile icons;
    private readonly BitmapFont font;
    private static ReadOnlySpan<ushort> NumberPalette => [0xffff, 0xe7ff, 0xd7ff, 0xc7ff, 0xb7ff, 0xa7ff, 0x97ff, 0x97ff, 0x77ff, 0x67ff, 0x57ff, 0x47ff, 0x371c, 0x369a, 0x3618, 0x3596, 0x2514, 0x2492, 0x2410, 0x238e, 0x130c, 0x12cb, 0x128a, 0x1249, 0x0208, 0x01c7, 0x0186, 0x0145, 0x0104, 0x00c3, 0x0082, 0x0041];

    public WorldHudRenderer(ushort[] panel565, SpriteFile panelSprites, BitmapFont activeFont)
    {
        if (panel565.Length < 640 * 60 || panelSprites.Frames.Length < 5)
        {
            throw new InvalidDataException("Native world HUD assets incomplete.");
        }

        panel = panel565;
        icons = panelSprites;
        font = activeFont;
    }

    public static WorldHudRenderer Load(string root, BitmapFont activeFont)
    {
        using var bmp = BinderArchive.Open(Path.Combine(root, "AT2BMP.000"));
        var image = BmpImage.Decode(bmp.ReadMember(bmp.FindResource("panel.bmp"u8)));
        if (image.Width != 640 || image.Height < 60)
        {
            throw new InvalidDataException("PANEL bitmap lacks640x60 source region.");
        }

        ushort[] pixels = image.ToRgb565();
        using var spr = BinderArchive.Open(Path.Combine(root, "AT2SPR.000"));
        return new(pixels, SpriteFile.Decode(spr.ReadMember(spr.FindResource("panel.spr"u8))), activeFont);
    }

    public static int? OriginY(int worldY, bool modal) => worldY > 4 ? 0 : modal ? null : 421;
    public static int? OriginY(int mode, int worldY, bool modal, int townAnchorY = 400, bool dungeonHudEnabled = false)
    {
        if (mode is not (300 or 310 or 330 or 400 or 410 or 420 or 430))
        {
            return null;
        }

        if (mode == 330 && !dungeonHudEnabled)
        {
            return null;
        }

        bool top = mode switch
        {
            300 or 310 or 330 => worldY > 4,
            400 => townAnchorY >= 80,
            _ => false
        };
        return top ? 0 : modal ? null : 421;
    }

    //429420 normal/4295e0 modal placement;4297c0 composition. Call after scene/dialogue/action overlays.
    public void Draw(
        GameState state,
        string areaName,
        Span<ushort> target,
        bool modal = false,
        int townAnchorY = 400,
        bool dungeonHudEnabled = false
    )
    {
        if (target.Length != 640 * 480)
        {
            throw new ArgumentException("HUD requires640x480 surface.");
        }

        int? origin = OriginY(state.Mode, state.WorldY, modal, townAnchorY, dungeonHudEnabled);
        if (origin is not { } y)
        {
            return;
        }

        for (int row = 0; row < 60 && y + row < 480; row++)
        {
            panel.AsSpan(row * 640, 640).CopyTo(target.Slice((y + row) * 640, 640));
        }

        string name = areaName.Trim().ToUpperInvariant();
        TextBoxRaster.Draw(font, name, target, 640, 480, 640, new(182, y + 8, 462, y + 24), 0, centered: true);
        TextBoxRaster.Draw(font, name, target, 640, 480, 640, new(180, y + 7, 460, y + 23), 0xffff, NativeUiPalette.Normal, centered: true);

        Icon(1, 40, y + 8, target);
        Number(state.ResourceCounterDc88, 90, y + 9, 92, y + 10, target);
        Icon(2, 39, y + 35, target);
        Number(state.Money, 90, y + 34, 92, y + 35, target);
        Icon(4, 500, y + 9, target);
        Number(state.Score, 550, y + 10, 552, y + 11, target);
        Icon(3, 500, y + 32, target);
        Number(state.ResourceCounterFc9a, 550, y + 33, 552, y + 34, target);
        Icon(0, 240, y + 37, target);
        Span<byte> date = stackalloc byte[40];
        int count = 0;
        Utf8Formatter.TryFormat(state.Year, date, out int length);
        count += length;
        date[count++] = (byte)'.';
        Utf8Formatter.TryFormat(state.Month, date[count..], out length);
        count += length;
        date[count++] = (byte)'.';
        Utf8Formatter.TryFormat(state.Day, date[count..], out length);
        count += length;
        Styled(date[..count], 290, y + 38, 292, y + 39, NativeUiPalette.Normal, target);
        if (!modal && state.Mode is 300 or 310) DrawActiveQuest(state, target);
    }

    public static void DrawActiveQuest(GameState state, Span<ushort> target)
    {
        if (state.Journal.ActiveQuest(state) is not { } id) return;
        const int left = 20, top = 64, availableWidth = 600;
        const ushort parchment = 0xce35;
        string title = GameText.Get($"quests.{id:D3}.heading.0");
        string text = GameText.Format("ui.quests.activeHud", ("title", title));
        text = UnicodeText.Fit(text, availableWidth, value => UnicodeText.Layout(value, 12, bold: true).Width);
        var mask = UnicodeText.Layout(text, 12, bold: true);
        UnicodeText.Draw(mask, target, 640, 480, 640, left - 1, top, 0);
        UnicodeText.Draw(mask, target, 640, 480, 640, left + 1, top, 0);
        UnicodeText.Draw(mask, target, 640, 480, 640, left, top - 1, 0);
        UnicodeText.Draw(mask, target, 640, 480, 640, left, top + 1, 0);
        UnicodeText.Draw(mask, target, 640, 480, 640, left, top, parchment);
    }

    private void Number(int value, int x, int y, int shadowX, int shadowY, Span<ushort> target)
    {
        Span<byte> bytes = stackalloc byte[12];
        Utf8Formatter.TryFormat(value, bytes, out int count);
        Styled(bytes[..count], x, y, shadowX, shadowY, NumberPalette, target);
    }

    private void Styled(ReadOnlySpan<byte> text, int x, int y, int shadowX, int shadowY, ReadOnlySpan<ushort> palette, Span<ushort> target)
    {
        font.DrawText(text, target, 640, 480, 640, shadowX, shadowY, 0);
        UiRaster.DrawStyledText(font, text, palette, 0, target, 640, 480, 640, x, y);
    }

    private void Icon(int frame, int x, int y, Span<ushort> target) => SpriteRaster.Draw565(icons.Frames[frame], target, 640, 480, 640, x, y);
}
