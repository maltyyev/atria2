using System.Text;
using Atria.Game.Combat;

namespace Atria.Game.Controls;

public sealed record MoveDefinition(string Id, string Name, int Direction, int RequiredFlags, int Icon, int Category, int Cost, int FirstForm)
{
    public bool IsMagic => Id.StartsWith("p:", StringComparison.Ordinal);
    public bool IsStrategy => Id.StartsWith("s:", StringComparison.Ordinal);
    public string Requirement => IsMagic ? "Ранг магии " + Category + "; MP " + Cost : RequiredFlags == (int)CombatActionFlags.CanChain ? "После обычного удара" : RequiredFlags != 0 ? "Продолжение приёма" : "";
}

public sealed class MoveCatalog
{
    public static readonly string[] Prefixes = ["ROLAND", "MIST", "SWALLOW", "BRAIUN", "EMERALD", "TIGER"];
    public static readonly string[] Names = ["Роланд", "Мист", "Шпионка", "Брайан", "Эмеральд", "Тигр"];
    private readonly CombatInfoCharacter[,] infos = new CombatInfoCharacter[6, 5];
    private readonly IReadOnlyList<MoveDefinition>[] all = new IReadOnlyList<MoveDefinition>[6];
    public MoveCatalog(string root)
    {
        for (int party = 0; party < 6; party++)
        {
            var found = new Dictionary<string, MoveDefinition>(StringComparer.Ordinal);
            for (int form = 1; form <= 5; form++)
            {
                var info = CombatInfoDefinition.Load(root, Encoding.ASCII.GetBytes(Prefixes[party] + form)).Characters[^1];
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
    public IReadOnlyList<MoveDefinition> All(int party) => all[party];
    public static string DirectionName(int code) => code switch
    {
        1 => "Вниз-назад",
        2 => "Вниз",
        3 => "Вниз-вперёд",
        4 => "Назад",
        6 => "Вперёд",
        7 => "Вверх-назад",
        8 => "Вверх",
        9 => "Вверх-вперёд",
        _ => "Удар"
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

                prerequisite = prior.Count > 0 ? " (после: " + string.Join(" / ", prior.Distinct()) + ")" : " (продолжение)";
            }

            moves.Add(new(id, "Приём: " + DirectionName(d).ToLowerInvariant() + prerequisite, d, flags, -1, -1, 0, form));
        }

        for (int i = 0; i < info.Commands.Count; i++)
        {
            var command = info.Commands[i];
            int cat = command.Category, icon = command.Icon;
            moves.Add(new(cat == 0 ? $"s:{i}" : $"p:{icon}", cat == 0 ? "Тактика " + (i + 1) : "Способность " + icon, 0, 0, icon - 1, cat, command.ManaCost, form));
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
