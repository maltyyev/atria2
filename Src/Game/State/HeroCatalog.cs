using System.Globalization;
using System.Text.Json;
using Atria.Resources;

namespace Atria.Game.State;

public sealed record HeroDefinition(string Id, int StorageIndex, int Template, string CombatPrefix,
    int Portrait, string NameKey, string IntroductionKey)
{
    public string Name => GameText.Get(NameKey);
    public string BattleResource(int form) => CombatPrefix + form.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Playable identities are separate from legacy party records used by scripted escorts.</summary>
public sealed class HeroCatalog
{
    public static HeroCatalog Default { get; } = Load();
    private readonly HeroDefinition[] heroes;
    public int Count => heroes.Length;
    public int StorageCount { get; }
    public HeroDefinition this[int index] => heroes[index];

    public HeroCatalog(IEnumerable<HeroDefinition> definitions)
    {
        heroes = definitions.ToArray();
        if (heroes.Length == 0 || heroes.Select(hero => hero.Id).Distinct(StringComparer.Ordinal).Count() != heroes.Length
            || heroes.Select(hero => hero.StorageIndex).Distinct().Count() != heroes.Length)
        {
            throw new InvalidDataException("Hero identities and storage indexes must be nonempty and unique.");
        }

        using var initial = GameData.Open("InitialParty");
        int templateCount = initial.RootElement.GetProperty("Members").GetArrayLength();
        if (!heroes.Take(templateCount).Select(hero => hero.StorageIndex).SequenceEqual(Enumerable.Range(0, templateCount)))
            throw new InvalidDataException("Original hero indexes must retain their scenario identities.");

        StorageCount = checked(GameState.LegacyPartyRecordCount + heroes.Count(hero => hero.StorageIndex >= GameState.LegacyPartyRecordCount));
        foreach (var hero in heroes)
        {
            if (string.IsNullOrWhiteSpace(hero.Id) || hero.Id.Length > 128 || hero.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))
                || string.IsNullOrWhiteSpace(hero.NameKey)
                || string.IsNullOrWhiteSpace(hero.IntroductionKey) || string.IsNullOrWhiteSpace(hero.CombatPrefix)
                || (uint)hero.StorageIndex >= (uint)StorageCount || hero.Portrait < 0 || (uint)hero.Template >= templateCount
                || hero.StorageIndex >= templateCount && hero.StorageIndex < GameState.LegacyPartyRecordCount)
            {
                throw new InvalidDataException($"Invalid hero definition or reserved escort slot: {hero.Id}");
            }
        }
    }

    public int Find(string id) => Array.FindIndex(heroes, hero => hero.Id == id);

    public void InitializeAdditionalHeroes(GameState state)
    {
        if (StorageCount == GameState.LegacyPartyRecordCount) return;

        var templates = Enumerable.Range(0, GameState.LegacyPartyRecordCount).Select(_ => new PartyMember()).ToArray();
        InitialPartyDefinition.Apply(templates);
        foreach (var hero in heroes.Where(hero => hero.StorageIndex >= GameState.LegacyPartyRecordCount))
        {
            var member = state.Party[hero.StorageIndex];
            PartyCodec.DecodeMember(PartyCodec.EncodeMember(templates[hero.Template]), member);
            member.MemberStatus = 0;
            member.SelectedForBattle = 0;
        }
    }

    private static HeroCatalog Load()
    {
        using var document = GameData.Open("Heroes");
        return new(document.RootElement.EnumerateArray().Select(Read));
    }

    private static HeroDefinition Read(JsonElement row) => new(
        row.GetProperty("id").GetString()!, row.GetProperty("storageIndex").GetInt32(),
        row.GetProperty("template").GetInt32(), row.GetProperty("combatPrefix").GetString()!,
        row.GetProperty("portrait").GetInt32(), row.GetProperty("nameKey").GetString()!,
        row.GetProperty("introductionKey").GetString()!);
}
