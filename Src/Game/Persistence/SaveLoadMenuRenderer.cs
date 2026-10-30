using System.Globalization;
using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;

namespace Atria.Game.Persistence;

public enum SaveMenuPrimitive
{
    Tint,
    Fill,
    Panel,
    Text
}

public enum SaveMenuTextStyle
{
    Solid,
    Normal,
    Selected,
    Title,
    Purple
}

public sealed record SaveMenuDrawCommand(
    SaveMenuPrimitive Primitive,
    int X,
    int Y,
    int A = 0,
    int B = 0,
    ushort Color = 0,
    bool Small = false,
    SaveMenuTextStyle Style = SaveMenuTextStyle.Solid,
    string Text = ""
);
/// <summary>421d20/4226a0/423050 and42ad20/42b520/42bfd0. Compose over fresh saveload.bmp.</summary>
public sealed class SaveLoadMenuRenderer
{
    private readonly SpriteFile etc;
    private readonly BitmapFont small, large;
    private readonly IReadOnlyList<ReadOnlyMemory<byte>> messages;
    public SaveLoadMenuRenderer(SpriteFile etc, BitmapFont small, BitmapFont large, IReadOnlyList<ReadOnlyMemory<byte>> messages)
    {
        if (small.Width != 8 || small.Height != 16 || large.Width != 16 || large.Height != 24)
        {
            throw new ArgumentException("Save UI requires original two bitmap fonts.");
        }

        if (messages.Count < 364)
        {
            throw new InvalidDataException("Save UI TEXTmsg labels are absent.");
        }

        this.etc = etc;
        this.small = small;
        this.large = large;
        this.messages = messages;
    }

    public IReadOnlyList<SaveMenuDrawCommand> BuildPlan(
        SaveLoadMenuFrame frame,
        IReadOnlyList<SaveSlotInfo> slots,
        bool alternateEmptyName = false,
        string safetyFailureLabel = ""
    )
    {
        if (frame.Selection < 0 || (slots.Count > 0 && frame.Selection >= slots.Count) || (uint)frame.ConfirmationChoice > 1
|| (uint)frame.Countdown > 20)
        {
            throw new InvalidDataException("Save UI frame exceeds slot/modal bounds.");
        }

        var ops = new List<SaveMenuDrawCommand>();
        void Box(SaveMenuPrimitive kind, int x, int y, int a, int b, ushort color = 0) => ops.Add(new(kind, x, y, a, b, color));
        void Text(string text, int x, int y, SaveMenuTextStyle style = SaveMenuTextStyle.Solid, bool sm = false, ushort color = 0, int w = 0, int h = 24) => ops.Add(new(SaveMenuPrimitive.Text, x, y, w, h, Color: color, Small: sm, Style: style, Text: text));
        void Pair(string text, int x, int y, SaveMenuTextStyle style, bool sm = false, bool flash = false, int shadowX = 1, int shadowY = 1, int w = 0)
        {
            Text(text, x + shadowX, y + shadowY, sm: sm, w: w);
            Text(text, x, y, flash ? SaveMenuTextStyle.Solid : style, sm, 0xffff, w: w);
        }

        void Outline(string text, int x, int y, SaveMenuTextStyle style, bool flash = false)
        {
            text = text.Trim().ToUpperInvariant();
            foreach (var d in new[]
            {
                (1, 1),
                (1, -1),
                (-1, 1),
                (-1, -1)
            }
            )
            {
                Text(text, x + d.Item1, y + d.Item2, sm: true, w: 280);
            }

            Text(text, x, y, flash ? SaveMenuTextStyle.Solid : style, sm: true, color: 0xffff, w: 280);
        }

        Box(SaveMenuPrimitive.Tint, 110, 78, 420, 260);
        Box(SaveMenuPrimitive.Panel, 96, 64, 12, 7);
        Box(SaveMenuPrimitive.Tint, 142, 382, 356, 36, 15);
        Box(SaveMenuPrimitive.Panel, 128, 368, 10, 0);
        Pair(GameText.Decode(messages[frame.Kind == SaveLoadMenuKind.Save ? 362 : 260].Span), 156, 390, SaveMenuTextStyle.Selected);
        if (slots.Count == 0)
        {
            Text(GameText.Get("ui.saves.empty"), 210, 195, sm: true, color: 0xffff);
        }

        Text($"{(slots.Count == 0 ? 0 : frame.Selection + 1)} / {slots.Count}", 440, 350, sm: true, color: 0xffff);
        for (int i = frame.Top; i < Math.Min(slots.Count, frame.Top + 5); i++)
        {
            var metadata = slots[i].Metadata;
            var style = i == frame.Selection ? SaveMenuTextStyle.Purple : SaveMenuTextStyle.Normal;
            bool flash = frame.Flash != 0 && i == frame.Selection;
            int y = 99 + (i - frame.Top) * 48;
            if (slots[i].IsNew)
            {
                Outline(GameText.Get("ui.saves.new"), 140, y, style, flash);
                continue;
            }

            if (metadata.Present == 0)
            {
                if (slots[i].Status is SaveSlotStatus.Invalid or SaveSlotStatus.Unreadable)
                {
                    Outline(GameText.Get("ui.saves.readFailed"), 140, y, style, flash);
                    continue;
                }

                var label = frame.Kind == SaveLoadMenuKind.Save && alternateEmptyName ? metadata.SecondaryName : metadata.PrimaryName;
                // Native empty-slot metadata can have a zero-length name. Keep the slot visible
                // using the supplied localization; do not change its empty/load-rejection state.
                if (label.IsEmpty && slots[i].Status == SaveSlotStatus.Empty)
                {
                    label = messages[361];
                }

                Outline(GameText.Decode(GameText.Name(label).Span), 140, y, style, flash);
                continue;
            }

            Outline(slots[i].Location?.Text ?? GameText.Decode(metadata.PrimaryName.Span), 140, y, style, flash);
            Pair(Join(GameText.Decode(messages[310].Span), " ", Number(metadata.ActorZeroLevel)), 440, y + 1, style, flash: flash, shadowX: -1, shadowY: -1, w: 82);
            var date = slots[i].Date;
            var dateText = Join(
                GameText.Decode(messages[311].Span),
                " ",
                Number(date.Year),
                ".",
                Number(date.Month),
                ".",
                Number(date.Day),
                " ]"
            );
            if (flash)
            {
                Text(dateText, 190, y + 26, sm: true, color: 0xffff);
            }
            else
            {
                Pair(dateText, 190, y + 26, style, true);
            }

            Text(slots[i].Automatic ? GameText.Get("ui.saves.automatic") : GameText.Get("ui.saves.manual"), 478, y + 26, sm: true, color: slots[i].Automatic ? (ushort)0x354a : (ushort)0x443e);
        }

        if (slots.Count > 0 && !slots[frame.Selection].IsNew)
        {
            var row = slots[frame.Selection];
            string detail = AutosaveReason.Text(row.Reason);
            if (row.WrittenUtc != default)
            {
                detail = row.WrittenUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") + "  " + detail;
            }

            Text(UnicodeText.Fit(detail, 58 * small.Advance, small.MeasureText), 88, 438, sm: true, color: 0xffff);
        }

        void ModalBox(int x, int y, int w, int h, int horizontal, int vertical)
        {
            Box(SaveMenuPrimitive.Tint, x + 14, y + 14, w, h);
            Box(SaveMenuPrimitive.Tint, x + 14, y + 14, w, h, 15);
            Box(SaveMenuPrimitive.Panel, x, y, horizontal, vertical);
        }

        if (frame.EmptyNotice)
        {
            ModalBox(160, 128, 292, 36, 8, 0);
            Pair(GameText.Decode(messages[267].Span), 192, 150, SaveMenuTextStyle.Title);
        }

        if (frame.Countdown > 0)
        {
            ModalBox(192, 128, 228, 100, 6, 2);
            Pair(GameText.Decode(messages[frame.Kind == SaveLoadMenuKind.Save ? 363 : 266].Span), frame.Kind == SaveLoadMenuKind.Save ? 224 : 228, 160, SaveMenuTextStyle.Normal);
            Box(SaveMenuPrimitive.Fill, 250, 200, 161 - frame.Countdown * 8, 13, 0xf81f);
        }

        if (frame.Confirmation)
        {
            ModalBox(192, 128, 228, 100, 6, 2);
            int first = frame.Kind == SaveLoadMenuKind.Save ? 312 : 263;
            Pair(GameText.Decode(messages[first].Span), 220, 160, SaveMenuTextStyle.Title);
            Pair(GameText.Decode(messages[first + 1].Span), 260, 192, frame.ConfirmationChoice == 0 ? SaveMenuTextStyle.Selected : SaveMenuTextStyle.Normal);
            Pair(GameText.Decode(messages[first + 2].Span), 320, 192, frame.ConfirmationChoice == 1 ? SaveMenuTextStyle.Selected : SaveMenuTextStyle.Normal);
        }

        // This supplied diagnostic is an explicit native-engine extension: original CRT errors had no recovered safe modal.
        if (!string.IsNullOrEmpty(safetyFailureLabel))
        {
            ModalBox(160, 128, 292, 36, 8, 0);
            Pair(safetyFailureLabel, 192, 150, SaveMenuTextStyle.Title);
        }

        return ops.AsReadOnly();
    }

    public void Draw(IReadOnlyList<SaveMenuDrawCommand> plan, Span<ushort> target)
    {
        if (target.Length != 640 * 480)
        {
            throw new ArgumentException("Save UI target must be640x480.");
        }

        foreach (var op in plan)
        {
            switch (op.Primitive)
            {
                case SaveMenuPrimitive.Tint:
                    UiRaster.TintHalf(target, 640, 480, 640, op.X, op.Y, op.A, op.B, op.Color);
                    break;
                case SaveMenuPrimitive.Fill:
                    for (int y = Math.Max(0, op.Y); y < Math.Min(480, op.Y + op.B); y++)
                    {
                        target.Slice(y * 640 + Math.Max(0, op.X), Math.Max(0, Math.Min(640, op.X + op.A) - Math.Max(0, op.X))).Fill(op.Color);
                    }

                    break;
                case SaveMenuPrimitive.Panel:
                    UiRaster.DrawPanel(etc, op.X, op.Y, op.A, op.B, target);
                    break;
                case SaveMenuPrimitive.Text:
                    var font = op.Small ? small : large;
                    if (op.A > 0)
                    {
                        TextBoxRaster.Draw(font, op.Text, target, 640, 480, 640, new(op.X, op.Y, op.X + op.A, op.Y + op.B), op.Color,
                            op.Style == SaveMenuTextStyle.Solid ? default : Palette(op.Style));
                    }
                    else if (op.Style == SaveMenuTextStyle.Solid)
                    {
                        font.DrawText(op.Text, target, 640, 480, 640, op.X, op.Y, op.Color);
                    }
                    else
                    {
                        UiRaster.DrawStyledText(font, op.Text, Palette(op.Style), 0, target, 640, 480, 640, op.X, op.Y);
                    }

                    break;
            }
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Join(params string[] parts) => string.Concat(parts);

    private static ReadOnlySpan<ushort> Palette(SaveMenuTextStyle style) => style switch
    {
        SaveMenuTextStyle.Normal => NativeUiPalette.Normal,
        SaveMenuTextStyle.Selected => NativeUiPalette.Selected,
        SaveMenuTextStyle.Title => NativeUiPalette.Title,
        SaveMenuTextStyle.Purple => [0xffff, 0xff1f, 0xfe9f, 0xfe1f, 0xfd9f, 0xfd1f, 0xfc9f, 0xfc9f, 0xfb9f, 0xfb1f, 0xfa9f, 0xfa1f, 0xe19c, 0xd19a, 0xc198, 0xb196, 0xa114, 0x9112, 0x8110, 0x710e, 0x608c, 0x588b, 0x508a, 0x4889, 0x4008, 0x3807, 0x3006, 0x2805, 0x2004, 0x1803, 0x1002, 0x0801],
        _ => throw new ArgumentOutOfRangeException(nameof(style))
    };
}
