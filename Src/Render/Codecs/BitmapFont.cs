using System.Text;
using Atria.Render.UI;

namespace Atria.Render.Codecs;

public sealed class BitmapFont
{
    private const int MaximumCachedEncodings = 1024;
    private const float UnicodeEmToLegacyLineHeight = 0.75f;
    private static readonly Dictionary<string, byte[]?> encodings = new(StringComparer.Ordinal);
    private readonly byte[] glyphs;
    public int Width
    {
        get;
    }
    public int Height
    {
        get;
    }
    public int Advance
    {
        get;
    }
    public int BytesPerGlyph => Width / 8 * Height;

    private BitmapFont(byte[] glyphs, int width, int height, int advance)
    {
        this.glyphs = glyphs;
        Width = width;
        Height = height;
        Advance = advance;
    }

    // 0x43cec0 loads fixed buffers; 0x43d480/0x43d4d0 index by unsigned byte.
    public static BitmapFont DecodeEng(ReadOnlySpan<byte> data, bool small)
    {
        int expected = small ? 4096 : 12288;
        if (data.Length != expected)
        {
            throw new InvalidDataException("Unexpected ENG font size.");
        }

        return new(data.ToArray(), small ? 8 : 16, small ? 16 : 24, small ? 8 : 11);
    }

    public ReadOnlySpan<byte> GetGlyph(byte character) => glyphs.AsSpan(character * BytesPerGlyph, BytesPerGlyph);

    public int MeasureText(string text) => TryLegacy(text, out var encoded) ? encoded.Length * Advance : UnicodeText.Layout(text, Height * UnicodeEmToLegacyLineHeight).Advance;

    public int DrawText(string text, Span<ushort> destination, int width, int height, int stride, int x, int y, ushort color, RasterClip? clip = null)
    {
        if (TryLegacy(text, out var encoded))
        {
            return DrawText(encoded, destination, width, height, stride, x, y, color, clip);
        }
        var mask = UnicodeText.Layout(text, Height * UnicodeEmToLegacyLineHeight);
        UnicodeText.Draw(mask, destination, width, height, stride, x, y, color, clip);
        return mask.Advance;
    }

    internal static bool TryLegacy(string text, out byte[] encoded)
    {
        lock (encodings)
        {
            if (!encodings.TryGetValue(text, out var value))
            {
                try
                {
                    int end = text.IndexOf('\0');
                    value = GameText.Encoding.GetBytes(end < 0 ? text : text[..end]);
                }
                catch (EncoderFallbackException)
                {
                    value = null;
                }
                if (encodings.Count >= MaximumCachedEncodings)
                {
                    encodings.Clear();
                }
                encodings.Add(text, value);
            }
            encoded = value ?? [];
            return value is not null;
        }
    }
    // 0x43d130 and0x43d1c0: row-major, left byte first, bit7 leftmost, zero bits transparent.
    public void DrawGlyph(byte character, Span<ushort> destination, int width, int height, int stride, int x, int y, ushort color, RasterClip? clip = null) => DrawBits(GetGlyph(character), Width, Height, destination, width, height, stride, x, y, color, clip);
    // Raw CP1251 bytes are glyph IDs in this localized executable. No implicit Unicode remapping.
    // 0x43d520 does not implement newline/control parsing; higher-level text layout is separate.
    public int DrawText(ReadOnlySpan<byte> text, Span<ushort> destination, int width, int height, int stride, int x, int y, ushort color, RasterClip? clip = null)
    {
        int count = 0;
        foreach (byte character in text)
        {
            if (character == 0)
            {
                break;
            }

            DrawGlyph(character, destination, width, height, stride, checked(x + count * Advance), y, color, clip);
            count++;
        }

        return checked(count * Advance);
    }

    internal static void DrawBits(
        ReadOnlySpan<byte> bits,
        int glyphWidth,
        int glyphHeight,
        Span<ushort> destination,
        int width,
        int height,
        int stride,
        int x,
        int y,
        ushort color,
        RasterClip? clip
    )
    {
        if (width < 0 || height < 0 || stride < width || (long)stride * height > destination.Length)
        {
            throw new ArgumentException("Invalid destination raster.");
        }

        RasterClip bounds = clip ?? new(0, 0, width, height);
        if (bounds.Left < 0 || bounds.Top < 0 || bounds.RightExclusive > width || bounds.BottomExclusive > height || bounds.RightExclusive < bounds.Left
|| bounds.BottomExclusive < bounds.Top)
        {
            throw new ArgumentException("Clip is outside destination.");
        }

        int rowBytes = glyphWidth / 8;
        if (glyphWidth % 8 != 0 || bits.Length != rowBytes * glyphHeight)
        {
            throw new ArgumentException("Invalid glyph bitmap.");
        }

        for (int row = 0; row < glyphHeight; row++)
        {
            long targetY = (long)y + row;
            if (targetY < bounds.Top || targetY >= bounds.BottomExclusive)
            {
                continue;
            }

            for (int column = 0; column < glyphWidth; column++)
            {
                long targetX = (long)x + column;
                if (targetX < bounds.Left || targetX >= bounds.RightExclusive)
                {
                    continue;
                }

                if ((bits[row * rowBytes + column / 8] & (0x80 >> (column & 7))) != 0)
                {
                    destination[(int)targetY * stride + (int)targetX] = color;
                }
            }
        }
    }
}
