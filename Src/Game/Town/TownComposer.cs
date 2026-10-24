using Atria.Render.Codecs;
using Atria.Render.Scenes;

namespace Atria.Game.Town;

public delegate void TownLeaveRenderer(TownFrame frame, Span<ushort> target);
public static class TownComposer
{
    public static void Draw(
        TownSceneAssets assets,
        TownFrame frame,
        int townId,
        SpriteFile etc,
        BitmapFont smallFont,
        AnimationFile effectAnimation,
        SpriteFile effectSprites,
        Span<ushort> target,
        TownLeaveRenderer? leaveRenderer = null
    )
    {
        if (target.Length != 640 * 480 || assets.Background565.Length != target.Length)
        {
            throw new ArgumentException("Town requires native640x480 framebuffer.");
        }

        if (frame.LeaveConfirmation && leaveRenderer is null)
        {
            throw new NotSupportedException("Town leave confirmation needs a recovered renderer.");
        }

        assets.Background565.CopyTo(target);
        if (frame.DrawObjects)
        {
            SodScenePresentation.DrawSelected(assets.Scene, assets.Objects ?? throw new InvalidDataException("Missing town objects."), frame.Selection, target, 640, 480, 640);
        }

        if (frame.DrawEffects)
        {
            TownEffectCatalog.Draw565(townId, effectAnimation, effectSprites, frame.EffectFrames, target, 640, 480, 640);
        }

        if (frame.DrawLabel)
        {
            SodScenePresentation.DrawSelectionLabel(assets.Scene, assets.Objects!, etc, smallFont, frame.Selection, frame.MotionX, frame.MotionY, target, 640, 480, 640);
        }

        if (frame.LeaveConfirmation)
        {
            leaveRenderer!(frame, target);
        }
    }
}
