using Atria.Render.Codecs;

namespace Atria.Render.Scenes;

public readonly record struct TownEffect(int Town, int Slot, int Sequence, int FrameCount, int CounterThreshold, int X, int Y, SpriteBlend Blend);
public static class TownEffectCatalog
{
    // Literal assignments in EXE 0x41ca50; audit/render/extract_town_effects.py.
    private static readonly TownEffect[] records = [new(1, 0, 0, 39, 3, 117, 165, SpriteBlend.Copy), new(1, 1, 5, 11, 2, 281, 164, SpriteBlend.Copy), new(9, 0, 1, 20, 1, 250, 270, SpriteBlend.Copy), new(10, 0, 3, 10, 2, 219, 276, SpriteBlend.AlphaPercent), new(12, 0, 3, 10, 2, 529, 253, SpriteBlend.AlphaPercent), new(13, 0, 3, 10, 2, 296, 315, SpriteBlend.AlphaPercent), new(14, 0, 3, 10, 2, 54, 290, SpriteBlend.AlphaPercent), new(16, 0, 3, 10, 2, 384, 326, SpriteBlend.AlphaPercent), new(17, 0, 3, 10, 2, 550, 249, SpriteBlend.AlphaPercent), new(20, 0, 2, 21, 1, 401, 160, SpriteBlend.AlphaPercent), new(22, 0, 3, 10, 2, 319, 101, SpriteBlend.AlphaPercent), new(23, 0, 4, 7, 3, 409, 50, SpriteBlend.HalfAll), new(24, 0, 3, 10, 2, 241, 276, SpriteBlend.AlphaPercent), new(27, 0, 3, 10, 2, 190, 239, SpriteBlend.AlphaPercent), new(27, 1, 2, 21, 1, 171, 275, SpriteBlend.AlphaPercent), new(30, 0, 2, 21, 1, 374, 156, SpriteBlend.AlphaPercent), new(33, 0, 5, 11, 1, 397, 115, SpriteBlend.Copy), new(33, 1, 5, 11, 3, 337, 134, SpriteBlend.Copy), new(36, 0, 3, 10, 2, 412, 196, SpriteBlend.AlphaPercent), new(38, 0, 3, 10, 2, 261, 357, SpriteBlend.AlphaPercent), new(41, 0, 3, 10, 2, 477, 218, SpriteBlend.AlphaPercent), new(43, 0, 2, 21, 1, 92, 139, SpriteBlend.AlphaPercent), new(46, 0, 3, 10, 2, 483, 289, SpriteBlend.AlphaPercent),];
    public static ReadOnlySpan<TownEffect> Records => records;

    public static void Draw565(
        int town,
        AnimationFile animation,
        SpriteFile sprites,
        ReadOnlySpan<int> frameIndices,
        Span<ushort> target,
        int width,
        int height,
        int stride,
        RasterClip? clip = null
    )
    {
        if ((uint)town >= 52)
        {
            throw new ArgumentOutOfRangeException(nameof(town));
        }

        if (frameIndices.Length != 5)
        {
            throw new ArgumentException("Town effects require five cursors.");
        }

        foreach (TownEffect effect in records)
        {
            if (effect.Town != town)
            {
                continue;
            }

            int frame = frameIndices[effect.Slot];
            if ((uint)frame >= (uint)effect.FrameCount)
            {
                throw new InvalidDataException("Town effect cursor is out of range.");
            }

            AnimationRaster.Draw565(animation, sprites, effect.Sequence, frame, target, width, height, stride, effect.X, effect.Y, effect.Blend, clip);
        }
    }

    // Advance once per recovered legacy presentation update, never per display refresh.
    // The host owns gating, initialization and the logical clock.
    public static void Advance(int town, Span<int> frameIndices, Span<int> counters)
    {
        if ((uint)town >= 52)
        {
            throw new ArgumentOutOfRangeException(nameof(town));
        }

        if (frameIndices.Length != 5 || counters.Length != 5)
        {
            throw new ArgumentException("Town effects require five cursors/counters.");
        }

        foreach (TownEffect effect in records)
        {
            if (effect.Town != town)
            {
                continue;
            }

            int slot = effect.Slot;
            counters[slot]++;
            if (counters[slot] <= effect.CounterThreshold)
            {
                continue;
            }

            counters[slot] = 0;
            frameIndices[slot]++;
            if (frameIndices[slot] >= effect.FrameCount)
            {
                frameIndices[slot] = 0;
            }
        }
    }
}
