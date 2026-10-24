using System.Buffers.Binary;

namespace Atria.Render.Codecs;

// Format read by 0x4364d0/0x4367d0. Disk metadata is retained without guessing gameplay meanings.
public readonly record struct SpriteRectangle(short A, short B, short C, short D);
public readonly record struct SpritePoint(short X, short Y);
public sealed record SpriteRun(int X, int Y, ushort Opcode, ushort[] Colors, ushort[] Alpha);
public sealed record SpriteFrame(
    ushort Metadata0,
    ushort Metadata1,
    ushort Metadata2,
    SpriteRectangle[] Rectangles,
    SpritePoint[] Points,
    int Width,
    int Height,
    SpriteRun[] Runs
);
public sealed record SpriteFile(bool PerPixelAlpha, SpriteFrame[] Frames)
{
    public static SpriteFile Decode(ReadOnlySpan<byte> data)
    {
        var reader = new CodecReader(data);
        var header = reader.Take(80);
        if (!header.StartsWith("JAMIE SYSTEM SPRITE TOOL"u8))
        {
            throw new InvalidDataException("Unknown sprite signature.");
        }

        uint alphaFormat = BinaryPrimitives.ReadUInt32LittleEndian(header[36..]);
        if (alphaFormat > 1)
        {
            throw new InvalidDataException("Unknown sprite alpha format.");
        }

        int frameCount = reader.U16(), rectangleCount = reader.U16(), pointCount = reader.U16();
        var frames = new SpriteFrame[frameCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            ushort m0 = reader.U16(), m1 = reader.U16(), m2 = reader.U16();
            var rectangles = new SpriteRectangle[rectangleCount];
            for (int i = 0; i < rectangles.Length; i++)
            {
                rectangles[i] = new(reader.I16(), reader.I16(), reader.I16(), reader.I16());
            }

            var points = new SpritePoint[pointCount];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new(reader.I16(), reader.I16());
            }

            int width = reader.U16(), height = reader.U16();
            uint streamSize = reader.U32();
            if (streamSize > int.MaxValue)
            {
                throw new InvalidDataException("Sprite stream is too large.");
            }

            var stream = new CodecReader(reader.Take((int)streamSize));
            var runs = new List<SpriteRun>();
            int x = 0, y = 0;
            bool ended = false;
            while (stream.Remaining != 0)
            {
                ushort op = stream.U16();
                if (op == 4)
                {
                    ended = true;
                    break;
                }

                if (op == 2)
                {
                    if (x != width || y >= height)
                    {
                        throw new InvalidDataException("Invalid sprite row boundary.");
                    }

                    x = 0;
                    y++;
                    continue;
                }

                if (op is not (0 or 1 or 3))
                {
                    throw new InvalidDataException("Unknown sprite packet opcode.");
                }

                int count = stream.I16(); // Rasterizers sign-extend the run count.
                if (count < 0 || y >= height || count > width - x)
                {
                    throw new InvalidDataException("Sprite run exceeds its frame.");
                }

                if (op != 1)
                {
                    var colors = new ushort[count];
                    var alpha = alphaFormat == 1 && op == 3 ? new ushort[count] : [];
                    for (int i = 0; i < count; i++)
                    {
                        colors[i] = stream.U16();
                        if (alpha.Length != 0)
                        {
                            alpha[i] = stream.U16();
                            if (alpha[i] > 100)
                            {
                                throw new InvalidDataException("Sprite alpha exceeds 100 percent.");
                            }
                        }
                    }

                    runs.Add(new(x, y, op, colors, alpha));
                }

                x += count;
            }

            if (!ended || stream.Remaining != 0 || y != height || x != 0)
            {
                throw new InvalidDataException("Incomplete sprite packet stream.");
            }

            frames[frame] = new(m0, m1, m2, rectangles, points, width, height, runs.ToArray());
        }

        if (reader.Remaining != 0)
        {
            throw new InvalidDataException("Unexpected trailing sprite bytes.");
        }

        return new(alphaFormat == 1, frames);
    }
}

internal ref struct CodecReader(ReadOnlySpan<byte> data)
{
    private ReadOnlySpan<byte> remaining = data;
    public readonly int Remaining => remaining.Length;

    public ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > remaining.Length)
        {
            throw new InvalidDataException("Truncated graphics resource.");
        }

        var part = remaining[..count];
        remaining = remaining[count..];
        return part;
    }

    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public short I16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
}
