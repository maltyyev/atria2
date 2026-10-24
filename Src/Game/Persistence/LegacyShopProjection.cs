using Atria.Game.Interiors;
using Atria.Resources;

namespace Atria.Game.Persistence;

/// <summary>The legacy writer persists only first7560 bytes of18360-byte30-shop bank. Do not extend its file format.</summary>
public static class LegacyShopProjection
{
    public const int PersistedBytes = 7560, FullBytes = 30 * 612;
    public static byte[] EncodePrefix(IReadOnlyList<ShopStock> stocks) => EncodeAll(stocks).AsSpan(0, PersistedBytes).ToArray();
    public static byte[] EncodeAll(IReadOnlyList<ShopStock> stocks)
    {
        if (stocks.Count != 30)
        {
            throw new InvalidDataException("Runtime shop bank must contain30 records.");
        }

        var result = new byte[FullBytes];
        for (int i = 0; i < 30; i++)
        {
            stocks[i].Encode().CopyTo(result, i * 612);
        }

        return result;
    }

    public static ShopStock[] Overlay(ReadOnlySpan<byte> prefix, IReadOnlyList<ShopStock> baseline)
    {
        if (prefix.Length != PersistedBytes)
        {
            throw new InvalidDataException("Legacy shop prefix must contain7560 bytes.");
        }

        var all = EncodeAll(baseline);
        prefix.CopyTo(all);
        return DecodeAll(all);
    }

    public static ShopStock[] DecodeAll(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length != FullBytes)
        {
            throw new InvalidDataException("Full shop bank must contain18360 bytes.");
        }

        var catalog = ShopCatalog.Parse(bytes);
        return Enumerable.Range(0, 30).Select(i => ShopStock.FromCatalog(catalog, i)).ToArray();
    }
}
