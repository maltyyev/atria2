namespace Atria.Game.Combat;

/// <summary>401950/401980 ->435330: opaque BltFast source rectangles, including the second layer.</summary>
public sealed record CombatBackgroundLayer(int Width, int Height, ushort[] Pixels)
{
    public void Draw(Span<ushort> target, CombatScrollLayer scroll, int shakeX = 0, int shakeY = 0)
    {
        if (target.Length != 640 * 480 || Pixels.Length != (long)Width * Height)
        {
            throw new ArgumentException("Invalid combat background raster.");
        }

        int x = unchecked(scroll.X + (shakeX < scroll.X ? -shakeX : shakeX));
        int y = unchecked(scroll.Y + (shakeY < scroll.Y ? -shakeY : shakeY));
        if (x < 0 || y < 0 || (long)x + 639 > Width || (long)y + 400 > Height)
        {
            throw new InvalidDataException("Native battle background source rectangle is outside loaded bitmap.");
        }
        //435330 passes flags0: DDBLTFAST_NOCOLORKEY. Even a black keyed secondary pixel copies.
        for (int row = 0; row < 400; row++)
        {
            Pixels.AsSpan((row + y) * Width + x, 639).CopyTo(target.Slice(row * 640, 639));
        }
    }
}
