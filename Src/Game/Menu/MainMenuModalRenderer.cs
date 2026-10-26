using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;

namespace Atria.Game.Menu;

public sealed class MainMenuModalRenderer
{
    private readonly SpriteFile tiles;
    private readonly BitmapFont font;
    private readonly IReadOnlyList<ReadOnlyMemory<byte>> messages;
    private ReadOnlyMemory<byte> heading => messages[268];
    private ReadOnlyMemory<byte> yesText => messages[269];
    private ReadOnlyMemory<byte> noText => messages[270];
    public MainMenuModalRenderer(SpriteFile etc, BitmapFont font, ReadOnlySpan<byte> textMessages)
    {
        tiles = etc;
        this.font = font;
        messages = GameText.Messages(LegacyTextTables.ParseMessages(textMessages));
    }

    public void Draw(MenuFrame frame, Span<ushort> target)
    {
        if (!frame.Confirmation)
        {
            throw new ArgumentException("Not a menu confirmation frame.");
        }

        if (target.Length != 640 * 480)
        {
            throw new ArgumentException("Modal requires native640x480 surface.");
        }

        ReadOnlySpan<ushort> style = NativeUiPalette.Normal;
        ushort color = NativeUiPalette.Shadow;
        foreach (var operation in MainMenuModalPlan.Build(frame.YesSelected, frame.ConfirmationFlash))
        {
            switch (operation.Kind)
            {
                case ModalPrimitive.Fill:
                    UiRaster.TintHalf(target, 640, 480, 640, operation.X, operation.Y, operation.Width, operation.Height, NativeUiPalette.ModalTint);
                    break;
                case ModalPrimitive.WindowTiles:
                    UiRaster.DrawPanel(tiles, operation.X, operation.Y, operation.Width, operation.Height, target);
                    break;
                case ModalPrimitive.TextOffsetX:
                case ModalPrimitive.TextOffsetY:
                    break; // both explicit zero in native modal
                case ModalPrimitive.TextColor:
                    color = operation.DataAddress switch
                    {
                        0x4cc480 => NativeUiPalette.Shadow,
                        0x4cc3b0 => NativeUiPalette.Flash,
                        _ => throw new InvalidDataException("Unknown modal color identity.")
                    };
                    break;
                case ModalPrimitive.TextStyle:
                    style = operation.DataAddress switch
                    {
                        0x4cc228 => NativeUiPalette.Normal,
                        0x4cc370 => NativeUiPalette.Title,
                        0x4cc484 => NativeUiPalette.Selected,
                        _ => throw new InvalidDataException("Unknown modal style identity.")
                    };
                    break;
                case ModalPrimitive.ShadowText:
                    font.DrawText(Text(operation.DataAddress), target, 640, 480, 640, operation.X, operation.Y, color);
                    break;
                case ModalPrimitive.ForegroundText:
                    UiRaster.DrawStyledText(font, Text(operation.DataAddress), style, 0, target, 640, 480, 640, operation.X, operation.Y);
                    break;
                default:
                    throw new InvalidDataException("Unknown modal operation.");
            }
        }
    }

    private ReadOnlySpan<byte> Text(uint identity) => identity switch
    {
        0x456b48 => heading.Span,
        0x456b98 => yesText.Span,
        0x456be8 => noText.Span,
        _ => throw new InvalidDataException("Unknown modal message identity.")
    };
}
