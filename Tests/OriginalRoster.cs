using Atria.Game.State;

namespace Atria.Tests;

/// <summary>Binary parity fixtures exercise the original identities independently of optional new content.</summary>
internal static class OriginalRoster
{
    public static HeroCatalog Catalog
    {
        get;
    } = new(Enumerable.Range(0, HeroCatalog.Default.Count)
        .Select(index => HeroCatalog.Default[index]).Where(hero => hero.StorageIndex < GameState.LegacyPartyRecordCount));
}
