namespace Atria.Render.UI;

public sealed record TextMask(int Width, int Height, int Advance, byte[] Coverage, int MissingGlyphs, int Lines);
