using Atria.Render.Codecs;

namespace Atria.Tests.Render;
public static class FontTests
{
    public static void Run(string originalRoot)
    {
        BitmapFont small = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(originalRoot, "eng1.fnt")), true);
        BitmapFont large = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(originalRoot, "eng.fnt")), false);
        Require(small.GetGlyph(65).SequenceEqual(Convert.FromHexString("0000000030102828283844ee00000000")), "Small A fixture changed.");
        Require(large.GetGlyph(65).SequenceEqual(Convert.FromHexString("00000000000000003f003f000d800d801dc018c018c03fe03fe06030f8f8f8f800000000000000000000000000000000")), "Large A fixture changed.");
        Require(small.GetGlyph(0xC0).SequenceEqual(small.GetGlyph(65)), "CP1251 Cyrillic A glyph mapping changed.");
        Require(small.GetGlyph(0xDF).SequenceEqual(Convert.FromHexString("000000003e4444443c44448e00000000")), "CP1251 Cyrillic Ya fixture changed.");
        ushort[] raster = new ushort[40 * 24];
        Array.Fill(raster, (ushort)0x1234);
        small.DrawGlyph(65, raster, 40, 24, 40, 0, 0, 0xFFFF);
        Require(raster[4 * 40 + 2] == 0xFFFF && raster[4 * 40 + 3] == 0xFFFF, "MSB-left font raster failed.");
        Require(raster[4 * 40 + 1] == 0x1234 && raster[4 * 40 + 4] == 0x1234, "Transparent font bits changed destination.");
        Require(large.DrawText([65, 65, 0, 65], raster, 40, 24, 40, 0, 0, 0xFFFF) == 22, "Large font advance or NUL termination failed.");
        Require(small.DrawText([0xC0, 0xDF], raster, 40, 24, 40, -4, -4, 0xFFFF) == 16, "Small font safe clipping/advance failed.");
        var han = HangulFont.Decode(File.ReadAllBytes(Path.Combine(originalRoot, "han.FNT")), false);
        var hanSmall = HangulFont.Decode(File.ReadAllBytes(Path.Combine(originalRoot, "HAN1.FNT")), true);
        byte[] glyph = new byte[72], smallGlyph = new byte[32];
        han.ComposeLegacyCode(0x8861, glyph);
        hanSmall.ComposeLegacyCode(0x8861, smallGlyph);
        Require(glyph.AsSpan().SequenceEqual(Convert.FromHexString("0000000000001ffc38001c38001c38001c38001c38001c38001c3e001c38001c38001c38001c38001c38001c38000038000038000038000038000038000038000000000000000000")), "HAN24 packed-code fixture changed.");
        Require(smallGlyph.AsSpan().SequenceEqual(Convert.FromHexString("000000187e1806180618061f061806180c187818001800180018001800000000")), "HAN16 packed-code fixture changed.");
        for (int code = 0; code <= ushort.MaxValue; code++)
        {
            han.ComposeLegacyCode((ushort)code, glyph);
            hanSmall.ComposeLegacyCode((ushort)code, smallGlyph);
        }
        Console.WriteLine("Fonts: ENG16x24/advance11 and ENG1 8x16/advance8, CP1251 fixtures and clipping passed.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}

