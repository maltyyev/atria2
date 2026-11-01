using Atria.Platform.Win32;

namespace Atria.Render.UI;

public interface ITextRasterizer : IDisposable
{
    TextMask Render(string text, float size, string family, bool bold, int wrapWidth);
}
