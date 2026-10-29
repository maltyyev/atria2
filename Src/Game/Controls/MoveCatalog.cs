using System.Text;
using Atria.Game.Combat;
using Atria.Game.State;

namespace Atria.Game.Controls;

public sealed record MoveDefinition(string Id, string Name, int Direction, int RequiredFlags, int Icon, int Category, int Cost, int FirstForm)
{
    public bool IsMagic => Id.StartsWith("p:", StringComparison.Ordinal);
    public bool IsStrategy => Id.StartsWith("s:", StringComparison.Ordinal);
    public string Requirement => IsMagic ? GameText.Format("ui.moves.magicRank", ("rank", Category), ("mana", Cost)) : RequiredFlags == (int)CombatActionFlags.CanChain ? GameText.Get("ui.moves.afterAttack") : RequiredFlags != 0 ? GameText.Get("ui.moves.continuation") : "";
}

public sealed class MoveCatalog
{
    public HeroCatalog Heroes
    {
        get;
    }
    private readonly CombatInfoCharacter[,] infos;
    private readonly IReadOnlyList<MoveDefinition>[] all;
    private LocalizationCatalog language = GameText.Current;
    public MoveCatalog(string root, HeroCatalog? heroCatalog = null)
    {
        Heroes = heroCatalog ?? HeroCatalog.Default;
        infos = new CombatInfoCharacter[Heroes.Count, 5];
        all = new IReadOnlyList<MoveDefinition>[Heroes.Count];
        for (int party = 0; party < Heroes.Count; party++)
        {
            var found = new Dictionary<string, MoveDefinition>(StringComparer.Ordinal);
            for (int form = 1; form <= 5; form++)
            {
                var info = CombatInfoDefinition.Load(root, Encoding.ASCII.GetBytes(Heroes[party].BattleResource(form))).Characters[^1];
                infos[party, form - 1] = info;
                foreach (var m in FromInfo(info, form))
                {
                    found.TryAdd(m.Id, m);
                }
            }

            all[party] = found.Values.ToArray();
        }
    }

    public CombatInfoCharacter Info(int party, int form) => infos[party, form - 1];
    public IReadOnlyList<MoveDefinition> All(int party)
    {
        if (!ReferenceEquals(language, GameText.Current))
        {
            for (int character = 0; character < all.Length; character++)
            {
                all[character] = Enumerable.Range(1, 5)
                    .SelectMany(form => FromInfo(infos[character, form - 1], form))
                    .DistinctBy(move => move.Id).ToArray();
            }
            language = GameText.Current;
        }
        return all[party];
    }
    public static string DirectionName(int code) => code switch
    {
        1 => GameText.Get("ui.moves.downBack"),
        2 => GameText.Get("ui.controls.down"),
        3 => GameText.Get("ui.moves.downForward"),
        4 => GameText.Get("ui.controls.back"),
        6 => GameText.Get("ui.moves.forward"),
        7 => GameText.Get("ui.moves.upBack"),
        8 => GameText.Get("ui.controls.up"),
        9 => GameText.Get("ui.moves.upForward"),
        _ => GameText.Get("ui.controls.attack")
    };
    public static IReadOnlyList<MoveDefinition> FromInfo(CombatInfoCharacter info, int form = 1)
    {
        var moves = new List<MoveDefinition>();
        for (int i = info.FirstVariation; i < info.VariationEnd; i++)
        {
            var attack = info.Attacks[i];
            int d = attack.Direction, flags = attack.RequiredFlags;
            if (!CombatInfoCharacter.IsDirectional(d))
            {
                continue;
            }

            string id = $"v:{d}:{flags}";
            if (moves.Any(m => m.Id == id))
            {
                continue;
            }

            string prerequisite = "";
            if (flags != (int)CombatActionFlags.CanChain)
            {
                var prior = new List<string>();
                for (int j = info.FirstVariation; j < info.VariationEnd; j++)
                {
                    var priorAttack = info.Attacks[j];
                    if (priorAttack.ResultFlags == flags && priorAttack.Direction is > 0 and < 10)
                    {
                        prior.Add(DirectionName(priorAttack.Direction).ToLowerInvariant());
                    }
                }

                prerequisite = " " + (prior.Count > 0 ? GameText.Format("ui.moves.afterPrefix", ("moves", string.Join(" / ", prior.Distinct()))) : GameText.Get("ui.moves.continuationSuffix"));
            }

            moves.Add(new(id, GameText.Format("ui.moves.prefix", ("direction", DirectionName(d).ToLowerInvariant()), ("prerequisite", prerequisite)), d, flags, -1, -1, 0, form));
        }

        for (int i = 0; i < info.Commands.Count; i++)
        {
            var command = info.Commands[i];
            int cat = command.Category, icon = command.Icon;
            string name = cat == 0 ? GameText.Format("ui.moves.strategy", ("strategy", i + 1))
                : NecromancerCombatAssets.IsInfo(info) && NecromancerCombatAssets.Spells.FirstOrDefault(spell => spell.Icon == icon) is { } spell
                    ? GameText.Get(spell.NameKey) : GameText.Format("ui.moves.ability", ("ability", icon));
            moves.Add(new(cat == 0 ? $"s:{i}" : $"p:{icon}", name, 0, 0, icon - 1, cat, command.ManaCost, form));
        }

        return moves;
    }

    public static int FindCommand(CombatInfoCharacter info, string id)
    {
        if (!id.StartsWith("p:", StringComparison.Ordinal) || !int.TryParse(id.AsSpan(2), out int icon))
        {
            return -1;
        }

        for (int i = 0; i < info.Commands.Count; i++)
        {
            if (info.Commands[i].Icon == icon && info.Commands[i].Category > 0)
            {
                return i;
            }
        }

        return -1;
    }
}
