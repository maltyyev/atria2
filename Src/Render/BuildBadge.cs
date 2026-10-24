namespace Atria.Render;

/// <summary>Presentation-only, opaque badge. It never touches the game's logical framebuffer.</summary>
internal static class BuildBadge
{
    public const int Height = 44;
    public static (uint[] Pixels, int Width) Create()
    {
        string text = "v" + BuildInfo.Version;
        const int scale = 4, padding = 8, advance = 25;
        int width = padding * 2 + text.Length * advance;
        uint[] pixels = new uint[width * Height];
        Array.Fill(pixels, 0xff101010u);
        for (int i = 0; i < text.Length; i++)
        {
            ReadOnlySpan<byte> rows = text[i] switch
            {
                '0' => [14, 17, 19, 21, 25, 17, 14],
                '1' => [4, 12, 4, 4, 4, 4, 14],
                '2' => [14, 17, 1, 2, 4, 8, 31],
                '3' => [30, 1, 1, 14, 1, 1, 30],
                '4' => [2, 6, 10, 18, 31, 2, 2],
                '5' => [31, 16, 16, 30, 1, 1, 30],
                '6' => [14, 16, 16, 30, 17, 17, 14],
                '7' => [31, 1, 2, 4, 8, 8, 8],
                '8' => [14, 17, 17, 14, 17, 17, 14],
                '9' => [14, 17, 17, 15, 1, 1, 14],
                '.' => [0, 0, 0, 0, 0, 6, 6],
                'v' => [0, 0, 17, 17, 17, 10, 4],
                _ => throw new InvalidOperationException("Build versions must use numeric major.minor.patch notation.")
            };
            for (int y = 0; y < 7; y++)
            {
                for (int x = 0; x < 5; x++)
                {
                    if ((rows[y] & (1 << (4 - x))) != 0)
                    {
                        for (int dy = 0; dy < scale; dy++)
                        {
                            // One extra horizontal pixel thickens the strokes.
                            for (int dx = 0; dx <= scale; dx++)
                            {
                                pixels[(padding + y * scale + dy) * width + padding + i * advance + x * scale + dx] = 0xffd3d3d3;
                            }
                        }
                    }
                }
            }
        }

        return (pixels, width);
    }
}
