using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;

namespace Atria.Game.Town;

public sealed class TownLeaveModalRenderer
{
    private readonly SpriteFile etc;
    private readonly BitmapFont font;
    private readonly ReadOnlyMemory<byte> yes, no;
    public TownLeaveModalRenderer(SpriteFile etc, BitmapFont activeFont, ReadOnlySpan<byte> textMessages)
    {
        this.etc = etc;
        font = activeFont;
        var messages = GameText.Messages(LegacyTextTables.ParseMessages(textMessages));
        // Native41d570 strings454cf8/454d48: message slots171/172.
        yes = messages[171];
        no = messages[172];
    }

    public void Draw(TownFrame frame, Span<ushort> target)
    {
        if (!frame.LeaveConfirmation || target.Length != 640 * 480)
        {
            throw new ArgumentException("Invalid town leave frame.");
        }

        UiRaster.TintHalf(target, 640, 480, 640, 206, 174, 228, 100, 0);
        UiRaster.DrawPanel(etc, 192, 160, 6, 2, target);
        DrawLine(yes.Span, 192, frame.LeaveYes, frame.LeaveFlash, target);
        DrawLine(no.Span, 236, !frame.LeaveYes, frame.LeaveFlash, target);
    }

    private void DrawLine(ReadOnlySpan<byte> text, int y, bool selected, int flash, Span<ushort> target)
    {
        if (!selected)
        {
            font.DrawText(text, target, 640, 480, 640, 232, y, 0x7bef);
            return;
        }

        if (flash != 0)
        {
            font.DrawText(text, target, 640, 480, 640, 232, y, flash == 2 ? (ushort)0xffff : (ushort)0xffe0);
            return;
        }

        font.DrawText(text, target, 640, 480, 640, 233, y + 1, 0);
        UiRaster.DrawStyledText(font, text, NativeUiPalette.Selected, 0, target, 640, 480, 640, 232, y);
    }
}
