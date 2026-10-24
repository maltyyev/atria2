using Atria.Render.Codecs;

namespace Atria.Render.Scenes;

public static class AnimationRaster
{
    // Wrappers0x4390f0/0x439160/0x4391d0/0x41c9e0 add signed ANI offsets.
    public static void Draw565(
        AnimationFile animation,
        SpriteFile sprites,
        int sequenceIndex,
        int frameIndex,
        Span<ushort> target,
        int width,
        int height,
        int stride,
        int x,
        int y,
        SpriteBlend blend,
        RasterClip? clip = null
    )
    {
        if ((uint)sequenceIndex >= (uint)animation.Sequences.Length)
        {
            throw new InvalidDataException("Animation sequence is absent.");
        }

        AnimationFrame[] frames = animation.Sequences[sequenceIndex].Frames;
        if ((uint)frameIndex >= (uint)frames.Length)
        {
            throw new InvalidDataException("Animation frame is absent.");
        }

        AnimationFrame frame = frames[frameIndex];
        if (frame.SpriteFrameIndex >= sprites.Frames.Length)
        {
            throw new InvalidDataException("Animation refers to an absent sprite frame.");
        }

        SpriteRaster.Draw565(
            sprites.Frames[frame.SpriteFrameIndex],
            target,
            width,
            height,
            stride,
            checked(x + frame.OffsetX),
            checked(y + frame.OffsetY),
            blend,
            clip: clip
        );
    }
}
