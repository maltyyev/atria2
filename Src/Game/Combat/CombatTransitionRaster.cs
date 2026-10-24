using Atria.Render.Codecs;
using Atria.Render.UI;

namespace Atria.Game.Combat;

/// <summary>403f70/403f00: fresh cached-scene copy, inclusive black shutters, optional retained label.</summary>
public sealed class CombatTransitionRaster
{
    private readonly ushort[] scene;
    private readonly byte[] text;
    private readonly BitmapFont font;
    private readonly int phase;
    public CombatTransitionRaster(ReadOnlySpan<ushort> cachedScene, BitmapFont largeFont, ReadOnlySpan<byte> retainedText, int stylePhase = 0)
    {
        if (cachedScene.Length != 640 * 480 || largeFont.Width != 16 || largeFont.Height != 24 || (uint)stylePhase >= 32)
        {
            throw new ArgumentException("Invalid native battle transition raster/font/style.");
        }

        int nul = retainedText.IndexOf((byte)0);
        if (nul >= 0)
        {
            retainedText = retainedText[..nul];
        }

        scene = cachedScene.ToArray();
        text = retainedText.ToArray();
        font = largeFont;
        phase = stylePhase;
    }

    public void Draw(CombatTransitionFrame frame, Span<ushort> target)
    {
        if (target.Length != scene.Length)
        {
            throw new ArgumentException("Battle transition target must be640x480.");
        }

        if ((uint)frame.Index >= CombatTransitionRuntime.FrameCount || frame.Top != 24 + 24 * frame.Index || frame.Bottom != 455 - 24 * frame.Index
|| frame.Left != 32 + 32 * frame.Index || frame.Right != 607 - 32 * frame.Index || frame.EntireBlack != (frame.Bottom < 240) || frame.DrawLabel != (frame.Bottom > 47))
        {
            throw new ArgumentException("Transition frame is not a recovered403f70 step.");
        }

        scene.CopyTo(target);
        if (frame.EntireBlack)
        {
            target.Clear();
        }
        else
        {
            Clear(target, 0, 0, 639, frame.Top);
            Clear(target, 0, frame.Bottom, 639, 479);
            Clear(target, 0, frame.Top, frame.Left, frame.Bottom);
            Clear(target, frame.Right, frame.Top, 639, frame.Bottom);
        }

        if (frame.DrawLabel)
        {
            UiRaster.DrawStyledText(font, text, NativeUiPalette.Selected, phase, target, 640, 480, 640, checked(310 - text.Length * 4), 224);
        }
    }

    private static void Clear(Span<ushort> target, int left, int top, int right, int bottom)
    {
        for (int y = top; y <= bottom; y++)
        {
            target.Slice(y * 640 + left, right - left + 1).Clear();
        }
    }
}
