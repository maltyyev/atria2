using System.Globalization;
using System.Text;
using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;

namespace Atria.Game.Interiors;

public enum ShopPrimitive
{
    Tint,
    Panel,
    ItemSprite,
    EtcSprite,
    Text
}

public enum ShopTextStyle
{
    Solid,
    Normal,
    Selected,
    Title,
    Quantity
}

public sealed record ShopDrawCommand(
    ShopPrimitive Primitive,
    int X,
    int Y,
    int A = 0,
    int B = 0,
    ushort Color = 0,
    bool Small = false,
    ShopTextStyle Style = ShopTextStyle.Solid,
    ReadOnlyMemory<byte> Text = default,
    SpriteBlend Blend = SpriteBlend.Copy
);
public readonly record struct ShopRenderFrame(int ArrowPhase, int TextPhase = 0);
public sealed class ShopRenderClock
{
    private int counter, phase;
    public ShopRenderFrame AdvanceLogicalFrame(bool hasRows)
    {
        var frame = new ShopRenderFrame(phase);
        if (hasRows && ++counter > 3)
        {
            counter = 0;
            phase = (phase + 1) & 3;
        }

        return frame;
    }

    public void Reset()
    {
        counter = phase = 0;
    }
}

/// <summary>430780..4349b0 exact recovered shop composition; draw onto a freshly recomposed interior background.</summary>
public sealed class ShopRenderer
{
    private readonly SpriteFile itemSprites, etc;
    private readonly BitmapFont small, large;
    private readonly ItemCatalog items;
    private readonly ReadOnlyMemory<byte> itemExtra;
    private readonly IReadOnlyList<ReadOnlyMemory<byte>> messages;
    private static ReadOnlySpan<ushort> QuantityPalette => [0xffff, 0xe7ff, 0xd7ff, 0xc7ff, 0xb7ff, 0xa7ff, 0x97ff, 0x97ff, 0x77ff, 0x67ff, 0x57ff, 0x47ff, 0x371c, 0x369a, 0x3618, 0x3596, 0x2514, 0x2492, 0x2410, 0x238e, 0x130c, 0x12cb, 0x128a, 0x1249, 0x0208, 0x01c7, 0x0186, 0x0145, 0x0104, 0x00c3, 0x0082, 0x0041];

    public ShopRenderer(
        SpriteFile itemSprites,
        SpriteFile etc,
        BitmapFont small,
        BitmapFont large,
        ItemCatalog items,
        ReadOnlyMemory<byte> itemExtra,
        IReadOnlyList<ReadOnlyMemory<byte>> messages
    )
    {
        if (small.Width != 8 || small.Height != 16 || large.Width != 16 || large.Height != 24)
        {
            throw new ArgumentException("Shop requires both original ENG font sizes.");
        }

        if (messages.Count < 372)
        {
            throw new InvalidDataException("Shop TEXTmsg labels are absent.");
        }

        this.itemSprites = itemSprites;
        this.etc = etc;
        this.small = small;
        this.large = large;
        this.items = items;
        this.itemExtra = itemExtra;
        this.messages = messages;
    }

    public IReadOnlyList<ShopDrawCommand> BuildPlan(ShopSession session, GameState state, ShopRenderFrame frame)
    {
        if ((uint)frame.ArrowPhase > 3 || (uint)frame.TextPhase > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(frame));
        }

        var ops = new List<ShopDrawCommand>();
        void Text(ReadOnlyMemory<byte> value, int x, int y, bool smallFont, ShopTextStyle style = ShopTextStyle.Solid, ushort color = 0xffff) => ops.Add(new(ShopPrimitive.Text, x, y, Color: color, Small: smallFont, Style: style, Text: value));
        void Pair(ReadOnlyMemory<byte> value, int x, int y, bool sm, ShopTextStyle style, ushort color = 0xffff, int shadowX = 1, int shadowY = 1)
        {
            Text(value, x + shadowX, y + shadowY, sm, color: 0);
            Text(value, x, y, sm, style, color);
        }

        void Tint(int x, int y, int w, int h, ushort color) => ops.Add(new(ShopPrimitive.Tint, x, y, w, h, color));
        void Panel(int x, int y, int w, int h) => ops.Add(new(ShopPrimitive.Panel, x, y, w, h));
        void Sprite(bool item, int index, int x, int y, SpriteBlend blend = SpriteBlend.Copy) => ops.Add(new(item ? ShopPrimitive.ItemSprite : ShopPrimitive.EtcSprite, x, y, index, Blend: blend));
        //42f3b0/42f6a0 immutable base panels.
        Tint(110, 46, 292, 36, 15);
        Panel(96, 32, 8, 0);
        Tint(110, 110, 292, 260, 0);
        Panel(96, 96, 8, 7);
        Tint(446, 110, 132, 164, 15);
        Panel(432, 96, 3, 4);
        Tint(446, 302, 132, 68, 15);
        Panel(432, 288, 3, 1);
        Pair(Msg(365), 470, 310, true, ShopTextStyle.Solid, shadowX: 2);
        int[] categoryFrames = [21, 35, 58];
        for (int c = 0; c < 3; c++)
        {
            Sprite(true, categoryFrames[c], 160 + c * 80, 48, SpriteBlend.HalfAll);
        }

        Sprite(true, categoryFrames[session.Category], 160 + session.Category * 80, 48);
        if (session.Count < 1)
        {
            var empty = Msg(353);
            Text(empty, 161, 229, false, color: 0);
            Text(empty, 161, 227, false, color: 0);
            Text(empty, 159, 229, false, color: 0);
            Text(empty, 159, 227, false, color: 0);
            Text(empty, 160, 228, false, ShopTextStyle.Title);
            return ops.AsReadOnly();
        }

        foreach (var row in session.VisibleRows())
        {
            Sprite(true, row.SpriteFrame, row.X, row.Y);
            Pair(items.GetItem(row.ItemId).Name, 180, row.Y + 8, true, ShopTextStyle.Selected);
            Pair(Number(row.Quantity), 356, row.Y + 5, false, ShopTextStyle.Selected);
        }

        int id = session.SelectedItemId;
        if ((uint)id >= 88)
        {
            throw new InvalidDataException("Shop selected item exceeds original88-item UI tables.");
        }

        var item = items.GetItem(id);
        int Int(int offset) => item.GetInteger((offset - 30) / 4);
        if (session.Buying)
        {
            for (int line = 0; line < 3; line++)
            {
                Pair(DatTables.GetItemExtraLine(itemExtra, id, line), 448, 120 + line * 16, true, ShopTextStyle.Normal);
            }

            int modifiers = Int(58);
            if ((uint)modifiers > 3)
            {
                throw new InvalidDataException("Item modifier count exceeds original three slots.");
            }

            int visibleModifiers = 0;
            for (int i = 0; i < modifiers; i++)
            {
                int code = Int(62 + i * 4), value = Int(74 + i * 4);
                if (code == 12)
                {
                    continue;
                }

                if ((uint)(45 + code) >= (uint)messages.Count)
                {
                    throw new InvalidDataException("Shop modifier label exceeds TEXTmsg.");
                }

                Pair(Msg(45 + code), 460, 180 + visibleModifiers * 18, true, ShopTextStyle.Solid, 0xffe0);
                Pair(
                    Join(value > 0 ? "+"u8.ToArray() : Array.Empty<byte>(), Number(value)),
                    542,
                    180 + visibleModifiers * 18,
                    true,
                    ShopTextStyle.Solid,
                    0xffe0
                );
                visibleModifiers++;
            }

            int lineY = 180 + visibleModifiers * 18;
            if (session.Category < 2 && Int(50) != 0)
            {
                Pair(Join(Msg(session.Category == 0 ? 366 : 370), Number(Int(50))), 460, lineY, true, ShopTextStyle.Solid);
                lineY += 18;
                Pair(
                    Join(Msg(session.Category == 0 ? 367 : 371), Number(Int(54)), session.Category == 1 ? "%"u8.ToArray() : Array.Empty<byte>()),
                    460,
                    lineY,
                    true,
                    ShopTextStyle.Solid
                );
            }

            if (session.Category == 2)
            {
                int[] labels = id switch
                {
                    55 => [318, 319],
                    56 => [320],
                    57 => [321],
                    >= 58 and <= 66 => [322 + (id - 58) * 2, 323 + (id - 58) * 2],
                    >= 84 and <= 87 => [340 + (id - 84) * 2, 341 + (id - 84) * 2],
                    _ => []
                };
                for (int i = 0; i < labels.Length; i++)
                {
                    Pair(Msg(labels[i]), 460, lineY + i * 18, true, ShopTextStyle.Solid);
                }
            }

            Pair(Msg(368), 460, 255, true, ShopTextStyle.Solid);
            Pair(Join(Number(Int(46)), Msg(369)), 505, 255, true, ShopTextStyle.Selected);
        }
        else
        {
            Pair(Join(Number(Int(46) / 2), Msg(369)), 464, 128, false, ShopTextStyle.Selected, shadowY: session.Category == 2 ? 1 : 2);
            if (Int(46) == 0)
            {
                Pair(Msg(349), 464, 160, false, ShopTextStyle.Title);
            }
        }

        if (session.Scroll > 0)
        {
            Sprite(false, 18, 152, 114);
        }

        if (session.Scroll + 5 < session.Count)
        {
            Sprite(false, 19, 152, 356);
        }

        Sprite(false, 22 + frame.ArrowPhase, 120, 133 + session.Row * 46);
        Pair(
            Join(Number(unchecked(state.OwnedItems[id] - state.AvailableItems[id])), " / "u8.ToArray(), Number(state.OwnedItems[id])),
            480,
            340,
            false,
            ShopTextStyle.Selected
        );
        if (session.QuantityActive)
        {
            int dy = session.Row * 46;
            Tint(142, 130 + dy, 228, 36, 0);
            Tint(142, 130 + dy, 228, 36, 0);
            Panel(128, 116 + dy, 6, 0);
            if (session.Buying && session.InsufficientFunds)
            {
                Pair(Msg(364), 164, 136 + dy, false, ShopTextStyle.Title);
            }
            else
            {
                Pair(Msg(session.Buying ? 348 : 350), 154, 140 + dy, false, ShopTextStyle.Solid);
                Pair(Number(session.Quantity), 228, 138 + dy, false, ShopTextStyle.Quantity);
                int total = unchecked((session.Buying ? Int(46) : Int(46) / 2) * session.Quantity);
                Pair(Join(Number(total), Msg(369)), 264, 138 + dy, false, ShopTextStyle.Selected);
            }
        }

        return ops.AsReadOnly();
    }

    public void Draw(ShopSession session, GameState state, ShopRenderFrame frame, Span<ushort> target)
    {
        if (target.Length != 640 * 480)
        {
            throw new ArgumentException("Shop target must be640x480.");
        }

        foreach (var op in BuildPlan(session, state, frame))
        {
            switch (op.Primitive)
            {
                case ShopPrimitive.Tint:
                    UiRaster.TintHalf(target, 640, 480, 640, op.X, op.Y, op.A, op.B, op.Color);
                    break;
                case ShopPrimitive.Panel:
                    UiRaster.DrawPanel(etc, op.X, op.Y, op.A, op.B, target);
                    break;
                case ShopPrimitive.ItemSprite:
                case ShopPrimitive.EtcSprite:
                    var sprites = op.Primitive == ShopPrimitive.ItemSprite ? itemSprites : etc;
                    if ((uint)op.A >= (uint)sprites.Frames.Length)
                    {
                        throw new InvalidDataException("Shop sprite frame is absent.");
                    }

                    SpriteRaster.Draw565(sprites.Frames[op.A], target, 640, 480, 640, op.X, op.Y, op.Blend);
                    break;
                case ShopPrimitive.Text:
                    var font = op.Small ? small : large;
                    if (op.Style == ShopTextStyle.Solid)
                    {
                        font.DrawText(op.Text.Span, target, 640, 480, 640, op.X, op.Y, op.Color);
                    }
                    else
                    {
                        UiRaster.DrawStyledText(font, op.Text.Span, Palette(op.Style), frame.TextPhase, target, 640, 480, 640, op.X, op.Y);
                    }

                    break;
            }
        }
    }

    private ReadOnlyMemory<byte> Msg(int index) => messages[index];
    private static byte[] Number(int value) => Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));
    private static byte[] Join(params ReadOnlyMemory<byte>[] parts)
    {
        int size = 0;
        foreach (var part in parts)
        {
            size = checked(size + part.Length);
        }

        var bytes = new byte[size];
        int p = 0;
        foreach (var part in parts)
        {
            part.Span.CopyTo(bytes.AsSpan(p));
            p += part.Length;
        }

        return bytes;
    }

    private static ReadOnlySpan<ushort> Palette(ShopTextStyle style) => style switch
    {
        ShopTextStyle.Normal => NativeUiPalette.Normal,
        ShopTextStyle.Selected => NativeUiPalette.Selected,
        ShopTextStyle.Title => NativeUiPalette.Title,
        ShopTextStyle.Quantity => QuantityPalette,
        _ => throw new ArgumentOutOfRangeException(nameof(style))
    };
}
