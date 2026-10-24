using Atria.Render.Codecs;

namespace Atria.Game.Settings;

public static class SettingsRenderer
{
    // Caller copies the actual OPTION bitmap first. This method never advances logical state.
    public static void Draw(SpriteFile sprites, ReadOnlySpan<SettingsSprite> commands, Span<ushort> target)
    {
        if (sprites.Frames.Length < 19)
        {
            throw new InvalidDataException("OPTION.SPR requires frames0..18.");
        }

        foreach (var command in commands)
        {
            SpriteRaster.Draw565(
                sprites.Frames[command.Frame],
                target,
                640,
                480,
                640,
                command.X,
                command.Y,
                command.Half ? SpriteBlend.HalfAll : SpriteBlend.Copy,
                preserveLegacyClipping: true
            );
        }
    }
}
