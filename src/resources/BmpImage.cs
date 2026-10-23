using System;
using System.Buffers.Binary;
using System.IO;

namespace Atria.Resources;

/// <summary>Top-down pixels, packed 0xFFRRGGBB; little-endian memory is BGRA8 for DXGI_FORMAT_B8G8R8A8_UNORM.</summary>
public sealed class BmpImage
{
    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }
    private BmpImage(int width, int height, uint[] pixels) { Width = width; Height = height; Pixels = pixels; }

    public static BmpImage Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < 54 || !source[..2].SequenceEqual("BM"u8)) throw new InvalidDataException("Truncated or invalid BMP.");
        uint fileSize = BinaryPrimitives.ReadUInt32LittleEndian(source[2..]);
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(source[10..]);
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(source[14..]);
        if (headerSize != 40) throw new NotSupportedException("Only BITMAPINFOHEADER (40-byte) BMP is recovered.");
        int width = BinaryPrimitives.ReadInt32LittleEndian(source[18..]);
        int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(source[22..]);
        ushort planes = BinaryPrimitives.ReadUInt16LittleEndian(source[26..]);
        ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(source[28..]);
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(source[30..]);
        uint colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(source[46..]);
        if (compression != 0 || bits is not (8 or 24)) throw new NotSupportedException("Only recovered BI_RGB 8/24-bit BMP is supported.");
        if (planes != 1 || width <= 0 || signedHeight == 0 || signedHeight == int.MinValue)
            throw new InvalidDataException("Invalid BMP dimensions or planes.");
        int height = Math.Abs(signedHeight);
        long stride = ((long)width * bits + 31) / 32 * 4;
        if (offset < 54 || offset > source.Length || height > (source.Length - offset) / stride ||
            (long)width * height > int.MaxValue || fileSize > source.Length || fileSize < offset + stride * height)
            throw new InvalidDataException("BMP pixel range exceeds source.");
        Span<uint> palette = stackalloc uint[256];
        int colorCount = 0;
        if (bits == 8)
        {
            colorCount = colorsUsed == 0 ? 256 : checked((int)Math.Min(colorsUsed, 257u));
            if (colorCount > 256 || 54 + colorCount * 4 > offset) throw new InvalidDataException("Invalid BMP palette range.");
            for (int i = 0; i < colorCount; i++)
                palette[i] = 0xff000000u | (BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(54 + i * 4, 4)) & 0x00ffffff);
        }
        uint[] pixels = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            int sourceY = signedHeight < 0 ? y : height - 1 - y;
            ReadOnlySpan<byte> row = source.Slice((int)(offset + sourceY * stride), (int)stride);
            Span<uint> destination = pixels.AsSpan(y * width, width);
            if (bits == 24)
            {
                for (int x = 0; x < width; x++)
                    destination[x] = 0xff000000u | ((uint)row[x * 3 + 2] << 16) | ((uint)row[x * 3 + 1] << 8) | row[x * 3];
            }
            else
            {
                for (int x = 0; x < width; x++)
                {
                    if (row[x] >= colorCount) throw new InvalidDataException("BMP pixel references a missing palette entry.");
                    destination[x] = palette[row[x]];
                }
            }
        }
        return new BmpImage(width, height, pixels);
    }
}
