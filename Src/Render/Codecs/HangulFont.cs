namespace Atria.Render.Codecs;

// Component compositor0x43d250/0x43d370. These accept an ORIGINAL packed two-byte code,
// not CP949 text or a Unicode codepoint. Localized text paths use BitmapFont instead.
public sealed class HangulFont
{
    private readonly byte[] components;
    public int Width
    {
        get;
    }
    public int Height => Width;
    public int BytesPerGlyph => Width / 8 * Height;
    private static ReadOnlySpan<byte> InitialMap => [0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static ReadOnlySpan<byte> MedialMap => [0, 0, 0, 1, 2, 3, 4, 5, 0, 0, 6, 7, 8, 9, 10, 11, 0, 0, 12, 13, 14, 15, 16, 17, 0, 0, 18, 19, 20, 21, 0, 0];
    private static ReadOnlySpan<byte> FinalMap => [0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 0, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 0, 0];
    private static ReadOnlySpan<byte> MedialWithoutFinal => [0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1];
    private static ReadOnlySpan<byte> MedialWithFinal => [2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2, 3, 3, 3];
    private static ReadOnlySpan<byte> InitialWithoutFinal => [0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 3, 3, 3, 1, 2, 4, 4, 4, 2, 1, 3, 0];
    private static ReadOnlySpan<byte> InitialWithFinal => [1, 5, 5, 5, 5, 5, 5, 5, 5, 6, 7, 7, 7, 6, 6, 7, 7, 7, 6, 6, 7, 5];
    private static ReadOnlySpan<byte> FinalVariant => [0, 0, 2, 0, 2, 1, 2, 1, 2, 3, 0, 2, 1, 3, 3, 1, 2, 1, 3, 3, 1, 1];

    private HangulFont(byte[] data, int width)
    {
        components = data;
        Width = width;
    }

    public static HangulFont Decode(ReadOnlySpan<byte> data, bool small)
    {
        if (data.Length != (small ? 11520 : 25920))
        {
            throw new InvalidDataException("Unexpected HAN font size.");
        }

        return new(data.ToArray(), small ? 16 : 24);
    }

    public void ComposeLegacyCode(ushort code, Span<byte> glyph)
    {
        if (glyph.Length != BytesPerGlyph)
        {
            throw new ArgumentException("Wrong Hangul glyph buffer size.");
        }

        glyph.Clear();
        int initial = InitialMap[(code >> 10) & 31];
        int medial = MedialMap[(code >> 5) & 31];
        int final = FinalMap[code & 31];
        int initialVariant = final == 0 ? InitialWithoutFinal[medial] : InitialWithFinal[medial];
        int medialVariant = final == 0 ? MedialWithoutFinal[initial] : MedialWithFinal[initial];
        if (initial != 0)
        {
            OrComponent(initial + initialVariant * 20, glyph);
        }

        if (medial != 0)
        {
            OrComponent(160 + medial + medialVariant * 22, glyph);
        }

        if (final != 0)
        {
            OrComponent(160 + 88 + final + FinalVariant[medial] * 28, glyph);
        }
    }

    public void DrawGlyph(ushort code, Span<ushort> destination, int width, int height, int stride, int x, int y, ushort color, RasterClip? clip = null)
    {
        Span<byte> glyph = stackalloc byte[72];
        glyph = glyph[..BytesPerGlyph];
        ComposeLegacyCode(code, glyph);
        BitmapFont.DrawBits(glyph, Width, Height, destination, width, height, stride, x, y, color, clip);
    }

    private void OrComponent(int index, Span<byte> glyph)
    {
        ReadOnlySpan<byte> source = components.AsSpan(index * BytesPerGlyph, BytesPerGlyph);
        for (int i = 0; i < glyph.Length; i++)
        {
            glyph[i] |= source[i];
        }
    }
}
