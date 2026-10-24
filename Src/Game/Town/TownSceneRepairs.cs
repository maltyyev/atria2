using System.Security.Cryptography;

namespace Atria.Game.Town;

internal static class TownSceneRepairs
{
    // AT2SOD/03_EVE1 points to absent03_zero.scr; its03UGA doorway names03_eve1.scr.
    // Restrict the inferred authoring-error correction to this exact shipped member.
    internal static byte[] Normalize(string member, byte[] source)
    {
        if (!member.Equals("03_EVE1", StringComparison.OrdinalIgnoreCase) || source.Length != 120
|| !source.AsSpan(94, 13).SequenceEqual("03_zero.scr\0\0"u8) || Convert.ToHexString(SHA256.HashData(source)) != "A399DC003524392C95A3D46B48F822B0AC00BA8282D13849D62B99F1904CFF71")
        {
            return source;
        }

        byte[] corrected = (byte[])source.Clone();
        "03_eve1.scr\0\0"u8.CopyTo(corrected.AsSpan(94, 13));
        return corrected;
    }
}
