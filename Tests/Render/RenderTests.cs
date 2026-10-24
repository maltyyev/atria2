using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Render;

public static class RenderTests
{
    public static void Run(string originalRoot)
    {
        int files = 0, frames = 0, alphaFiles = 0;
        foreach (string archiveName in new[]
        {
            "SPR.000",
            "AT2SPR.000"
        }
        )
        {
            using var archive = BinderArchive.Open(Path.Combine(originalRoot, archiveName));
            foreach (var entry in archive.Entries)
            {
                byte[] data = archive.ReadMember(entry.Index);
                SpriteFile sprite = SpriteFile.Decode(data);
                files++;
                frames += sprite.Frames.Length;
                if (sprite.PerPixelAlpha)
                {
                    alphaFiles++;
                }

                if (entry.Index == 0)
                {
                    Reject(() => SpriteFile.Decode(data.AsSpan(0, data.Length - 1)));
                    byte[] corrupt = (byte[])data.Clone();
                    corrupt[36] = 2;
                    Reject(() => SpriteFile.Decode(corrupt));
                }

                foreach (var frame in sprite.Frames)
                {
                    ushort[] target = new ushort[checked(frame.Width * frame.Height)];
                    SpriteRaster.Draw565(frame, target, frame.Width, frame.Height, frame.Width, 0, 0, sprite.PerPixelAlpha ? SpriteBlend.AlphaPercent : SpriteBlend.Copy);
                }
            }
        }

        SpriteFile face = SpriteFile.Decode(File.ReadAllBytes(Path.Combine(originalRoot, "face.SPR")));
        files++;
        frames += face.Frames.Length;
        Require(files == 148 && frames == 8294, $"Sprite census changed: {files}/{frames}.");
        using (var animationArchive = BinderArchive.Open(Path.Combine(originalRoot, "ANI.000")))
        {
            int total = 0;
            foreach (var entry in animationArchive.Entries)
            {
                byte[] data = animationArchive.ReadMember(entry.Index);
                AnimationFile animation = AnimationFile.Decode(data);
                foreach (var sequence in animation.Sequences)
                {
                    total += sequence.Frames.Length;
                    if (sequence.Frames.Length != 0)
                    {
                        Require(sequence.Frames[0].OffsetX == 0 && sequence.Frames[0].OffsetY == 0, "Initial ANI displacement was not normalized.");
                    }
                }

                if (entry.Index == 0)
                {
                    Reject(() => AnimationFile.Decode(data.AsSpan(0, data.Length - 1)));
                }
            }

            Console.WriteLine($"ANI: {animationArchive.Entries.Count} files, {total} frames parsed.");
        }

        // Independent fixed vectors distinguish separate truncation from averaged/alpha-channel approximations.
        Require(SpriteRaster.Alpha565(0xFFFF, 0xFFFF, 50) == 0xF7DE, "Alpha truncation changed.");
        Require(SpriteRaster.AverageComponents565(0xFFFF, 0xFFFF) == 0xFFFF, "Component average changed.");
        Require(SpriteRaster.Half565(0xFFFF, 0xFFFF) == 0xF7DE, "Mask-half changed.");
        Require(SpriteRaster.Alpha565(0xF800, 0x001F, 100) == 0xF800, "Opaque alpha changed.");
        Require(SpriteRaster.Alpha565(0xF800, 0x001F, 0) == 0x001F, "Transparent alpha changed.");
        var synthetic = new SpriteFrame(0, 0, 0, [], [], 2, 1, [new(0, 0, 0, [0xF800, 0x001F], [])]);
        ushort[] pixels = new ushort[5];
        SpriteRaster.Draw565(synthetic, pixels, 5, 1, 5, 0, 0, mirrored: true);
        Require(pixels[0] == 0 && pixels[1] == 0x001F && pixels[2] == 0xF800, "Legacy mirror origin changed.");
        pixels.AsSpan().Clear();
        SpriteRaster.Draw565(synthetic, pixels, 5, 1, 5, -1, 0);
        Require(pixels[0] == 0x001F, "Left clipping failed.");
        pixels.AsSpan().Clear();
        SpriteRaster.Draw565(synthetic, pixels, 5, 1, 5, -1, 0, SpriteBlend.AlphaPercent);
        Require(pixels[0] == 0x001F && pixels[1] == 0, "Safe alpha clipping must not misalign bytes.");
        // Original 0x439d4d: scratch destination 2*runX+clipLeft-screenRunX =1 byte.
        // Safe destination is2*(runX+clipLeft-screenRunX)=2 bytes for this x=-1 case.
        byte[] legacyScratch = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(legacyScratch.AsSpan(1), 0x001F);
        ushort legacyVisiblePixel = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(legacyScratch.AsSpan(2));
        Require(legacyVisiblePixel == 0 && pixels[0] == 0x001F, "Byte-misalignment regression changed.");
        FontTests.Run(originalRoot);
        SceneRenderTests.Run(originalRoot);
        Console.WriteLine($"SPR: {files} files, {frames} frames, {alphaFiles} alpha files; bounds and blend vectors passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidDataException("Malformed graphics resource was accepted.");
    }
}
