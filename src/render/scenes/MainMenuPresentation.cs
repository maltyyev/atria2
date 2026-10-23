using Atria.Render.Codecs;

namespace Atria.Render.Scenes;

public static class MainMenuPresentation
{
    // EXE tables0x44d794/0x44d7ac and0x44d764/0x44d77c.
    public static ReadOnlySpan<int> SelectionX => [144,224,370,424,360,212];
    public static ReadOnlySpan<int> SelectionY => [220,286,290,212,144,146];
    public static ReadOnlySpan<int> FlameX => [124,226,386,495,423,205];
    public static ReadOnlySpan<int> FlameY => [193,109,108,189,308,306];
    public static ReadOnlySpan<int> InitialFlameFrames => [0,4,2,8,3,5];

    // Expose separate primitives because confirmation UI is drawn BEFORE the flames in0x423900.
    public static void DrawSelection(SpriteFile menu, int selectedItem, Span<ushort> target,
        int width, int height, int stride, RasterClip? clip = null)
    {
        ValidateItem(selectedItem);
        SpriteRaster.Draw565(GetMenuFrame(menu, selectedItem), target, width, height, stride,
            SelectionX[selectedItem], SelectionY[selectedItem], SpriteBlend.Copy, clip: clip);
    }
    public static void DrawPanel(SpriteFile menu, int item, int revealFrame, Span<ushort> target,
        int width, int height, int stride, RasterClip? clip = null)
    {
        ValidateItem(item);
        if ((uint)revealFrame > 7) throw new ArgumentOutOfRangeException(nameof(revealFrame));
        SpriteRaster.Draw565(GetMenuFrame(menu, 6 + item * 8 + revealFrame), target,
            width, height, stride, 232, 192, SpriteBlend.Copy, clip: clip);
    }
    public static void DrawFlames(AnimationFile animation, SpriteFile sprites, ReadOnlySpan<int> frameIndices,
        Span<ushort> target, int width, int height, int stride, RasterClip? clip = null)
    {
        if (frameIndices.Length != 6) throw new ArgumentException("Menu requires six flame cursors.");
        for (int i = 0; i < 6; i++)
            AnimationRaster.Draw565(animation, sprites, 0, frameIndices[i], target, width, height, stride,
                FlameX[i], FlameY[i], SpriteBlend.HalfType3, clip);
    }
    private static void ValidateItem(int item)
    { if ((uint)item >= 6) throw new ArgumentOutOfRangeException(nameof(item)); }
    private static SpriteFrame GetMenuFrame(SpriteFile sprites, int frame)
    {
        if ((uint)frame >= (uint)sprites.Frames.Length) throw new InvalidDataException("MENU sprite frame is absent.");
        return sprites.Frames[frame];
    }
}
