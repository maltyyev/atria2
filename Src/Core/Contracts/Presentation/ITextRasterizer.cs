using Atria.Core.Presentation.UI;
namespace Atria.Core.Contracts.Presentation;

/// <summary>Platform-owned shaping that returns a platform-neutral coverage mask.</summary>
public interface ITextRasterizer : IDisposable
{
    TextMask Render(string text, float size, string family, bool bold, int wrapWidth);
}
