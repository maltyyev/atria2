using Atria.Core.Contracts.Presentation;
using Atria.Core.Presentation.UI;

namespace Tests.Ios;

/// <summary>Supplies deterministic blank masks for the host-loop smoke check.</summary>
internal sealed class SmokeTextRasterizer : ITextRasterizer
{
    public TextMask Render(string text, float size, string family, bool bold, int wrapWidth)
    {
        int advance = Math.Min(wrapWidth, (int)Math.Ceiling(text.Length * size * 0.5f));
        int width = advance + TextMask.Padding * 2, height = (int)Math.Ceiling(size) + TextMask.Padding * 2;
        return new(width, height, advance, new byte[width * height], 0, 1);
    }

    public void Dispose() { }
}
