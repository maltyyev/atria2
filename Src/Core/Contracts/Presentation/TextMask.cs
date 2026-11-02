namespace Atria.Core.Contracts.Presentation;

/// <summary>Grayscale text coverage positioned relative to the layout origin, with its logical advance.</summary>
public sealed record TextMask(int Width, int Height, int Advance, byte[] Coverage, int MissingGlyphs, int Lines,
    int OriginX = -TextMask.Padding, int OriginY = -TextMask.Padding)
{
    public const int Padding = 4;
}
