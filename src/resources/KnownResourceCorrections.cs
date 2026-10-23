using System;
using System.IO;
using System.Security.Cryptography;

namespace Atria.Resources;

/// <summary>Version-specific corrections to owned member copies. Never writes an archive.</summary>
public static class KnownResourceCorrections
{
    public const string TigerSourceSha256 = "3896fb7f9c3952b0b3a5c63ef6562bfc58e4442a368bd5674d35271ced48cc9b";
    public const int TigerLibraryScriptOffset = 120 + 4 * 370 + 170;

    /// <summary>LIBRARY-001 secondary defect: fifth object's 32_libar.scr reference. Caller must select AT2SOD:32TIGER.</summary>
    public static byte[] CorrectTigerLibraryReference(ReadOnlySpan<byte> original)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(original, hash);
        if (original.Length != 1970 || !hash.SequenceEqual(Convert.FromHexString(TigerSourceSha256)))
            throw new InvalidDataException("Tiger Castle correction requires the verified source member identity.");
        if (!original.Slice(TigerLibraryScriptOffset, 13).SequenceEqual("32_libar.scr\0"u8))
            throw new InvalidDataException("Tiger Castle correction source field does not match.");
        byte[] corrected = original.ToArray();
        "32_libra.scr"u8.CopyTo(corrected.AsSpan(TigerLibraryScriptOffset));
        return corrected;
    }
}
