using Atria.Render.Codecs;
using Atria.Render.UI;

namespace Atria.Game.Dialogue;

public static class DialogueRaster
{
    public static void Draw(
        DialogueRequest request,
        DialogueFrame frame,
        SpriteFile tiles,
        SpriteFrame? face,
        BitmapFont smallFont,
        Span<ushort> target,
        int width = 640,
        int height = 480,
        int stride = 640,
        int textPhase = 0
    )
    {
        if (stride < width || (long)stride * height > target.Length || width < 0 || height < 0)
        {
            throw new ArgumentException("Invalid dialogue raster.");
        }

        if (smallFont.Width != 8 || smallFont.Height != 16)
        {
            throw new ArgumentException("Dialogue requires small ENG font.");
        }

        if (request.Placement is 0 or 1)
        {
            if (face is null)
            {
                throw new InvalidDataException("Native face placement requires a loaded face sprite.");
            }

            SpriteRaster.Draw565(
                face,
                target,
                width,
                height,
                stride,
                request.Placement == 0 ? -64 : 704 - face.Width,
                480 - face.Height + frame.OpeningOffset,
                mirrored: request.Placement == 0
            );
        }

        int x = frame.PanelX, y = frame.PanelY + frame.OpeningOffset;
        for (int row = Math.Max(0, y + 13); row < Math.Min(height, y + 83); row++)
        {
            for (int column = Math.Max(0, x + 13); column < Math.Min(width, x + 467); column++)
            {
                int p = row * stride + column;
                target[p] = (ushort)((target[p] & 0xe79c) >> 2);
            }
        }

        UiRaster.DrawPanel(tiles, x, y, 13, 1, target, width, height, stride, true);
        if (!frame.TextVisible)
        {
            return;
        }

        for (int row = 0; row < 4; row++)
        {
            int line = frame.FirstLine + row;
            if (line >= (request.DisplayLines?.Length ?? request.Lines.Count))
            {
                continue;
            }

            smallFont.DrawText(request.DisplayLines is { } display ? display[line] : GameText.Decode(request.Lines[line].Span), target, width, height, stride, x + 25, y + 18 + row * 16, 0);
        }

        for (int row = 0; row < 4; row++)
        {
            int line = frame.FirstLine + row;
            if (line >= (request.DisplayLines?.Length ?? request.Lines.Count))
            {
                continue;
            }

            if (request.IsQuestion && (request.ChoiceByRow is { } choices ? choices[line] == choices[frame.SelectedLine] : row == frame.SelectedLine))
            {
                UiRaster.DrawStyledText(smallFont, request.DisplayLines is { } display ? display[line] : GameText.Decode(request.Lines[line].Span), NativeUiPalette.Dialogue, textPhase, target, width, height, stride, x + 24, y + 17 + row * 16);
            }
            else
            {
                smallFont.DrawText(request.DisplayLines is { } display ? display[line] : GameText.Decode(request.Lines[line].Span), target, width, height, stride, x + 24, y + 17 + row * 16, 0xffff);
            }
        }

        if (request.IsQuestion)
        {
            SpriteRaster.Draw565(tiles.Frames[13], target, width, height, stride, x + 30 + frame.ArrowXOffset, 385 + (frame.SelectedLine - frame.FirstLine) * 16 + frame.ArrowYOffset);
        }
        else
        {
            SpriteRaster.Draw565(tiles.Frames[19], target, width, height, stride, x + 444, 446 - frame.ArrowYOffset, SpriteBlend.HalfType3);
            SpriteRaster.Draw565(tiles.Frames[19], target, width, height, stride, x + 444, 448 - frame.ArrowYOffset);
        }
    }
}
