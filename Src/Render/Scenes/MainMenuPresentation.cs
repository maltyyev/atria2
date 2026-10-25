using Atria.Render.Codecs;

namespace Atria.Render.Scenes;

public static class MainMenuPresentation
{
    // Retained save-state animation cursors; the temple artwork no longer draws the wheel's flames.
    public static ReadOnlySpan<int> InitialFlameFrames => [0, 4, 2, 8, 3, 5];

    public static void DrawSelection(int selectedItem, Span<ushort> target, int width, int height, int stride, RasterClip? clip = null)
    {
        if ((uint)selectedItem >= MainMenuLayout.Order.Length)
            throw new ArgumentOutOfRangeException(nameof(selectedItem));
        MainMenuArtwork.DrawLabels(selectedItem, target, width, height, stride, clip);
    }
}
