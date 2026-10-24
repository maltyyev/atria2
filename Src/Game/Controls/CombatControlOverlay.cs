using Atria.Game.Combat;
using Atria.Render.Codecs;

namespace Atria.Game.Controls;

public sealed class CombatControlOverlay
{
    private readonly InputRouter input;
    private readonly CombatSceneRuntime runtime;
    private readonly CombatActorController controller;
    private readonly ControlPanel panel;
    private readonly BitmapFont font;
    private readonly SpriteFile icons;
    private readonly Action save;
    private readonly List<ControlRow> rows = [];
    private int owner = -1, actor = -1, party;
    private IReadOnlyList<MoveDefinition> moves = [];
    private string notice = "";
    public CombatControlOverlay(
        InputRouter input,
        CombatSceneRuntime runtime,
        CombatActorController controller,
        SpriteFile tiles,
        BitmapFont font,
        SpriteFile icons,
        Action save
    )
    {
        this.input = input;
        this.runtime = runtime;
        this.controller = controller;
        this.font = font;
        this.icons = icons;
        this.save = save;
        panel = new(tiles, font);
    }

    public int ActorForPlayer(int p)
    {
        for (int i = runtime.Scene.PartyStartIndex; i < runtime.Actors.Count; i++)
        {
            if (runtime.Actors[i].ControlPlayer == p && runtime.Actors[i].State.ControlledBy != 0)
            {
                return i;
            }
        }

        return -1;
    }

    public void Update()
    {
        if (owner < 0)
        {
            for (int p = 0; p < 2; p++)
            {
                if (input.Held(p, GameAction.Selector))
                {
                    actor = ActorForPlayer(p);
                    if (actor < 0)
                    {
                        continue;
                    }

                    owner = p;
                    party = actor - runtime.Scene.PartyStartIndex;
                    controller.SelectorOpen[p] = true;
                    panel.Reset();
                    notice = "";
                    moves = MoveCatalog.FromInfo(runtime.Actors[actor].Binding!.Info);
                    BuildRows();
                    break;
                }
            }
        }

        if (owner < 0)
        {
            return;
        }

        if (ActorForPlayer(owner) != actor)
        {
            controller.SelectorOpen[owner] = false;
            owner = -1;
            return;
        }

        if (!input.Held(owner, GameAction.Selector))
        {
            if (panel.Selection < moves.Count)
            {
                controller.SelectedMoves[owner][party] = moves[panel.Selection].Id;
            }

            controller.SelectorOpen[owner] = false;
            input.ClearPlayer(owner);
            owner = -1;
            return;
        }

        int slot = input.Profile.Players[owner].PadSlot;
        if (slot >= 0)
        {
            if (input.Take(300 + slot * 32 + 22, true))
            {
                panel.Selection = Math.Max(0, panel.Selection - 1);
            }

            if (input.Take(300 + slot * 32 + 23, true))
            {
                panel.Selection = Math.Min(rows.Count - 1, panel.Selection + 1);
            }
        }

        // Assign quick slots before D-pad can be interpreted as menu navigation.
        for (int q = 0; q < 4; q++)
        {
            if (input.PressQuick(owner, q, assigning: true) && panel.Selection < moves.Count)
            {
                input.Profile.Players[owner].Quick[party][q] = moves[panel.Selection].Id;
                save();
                notice = GameText.Format("ui.combat.slotAssigned", ("slot", q + 1));
            }
        }

        panel.Input(input, rows, false, owner);
    }

    private void BuildRows()
    {
        rows.Clear();
        int rank = runtime.Actors[actor].State.MagicRank;
        foreach (var m in moves)
        {
            var move = m;
            string value = m.IsMagic ? (m.Category > rank ? GameText.Format("ui.combat.rankRequired", ("rank", m.Category)) : GameText.Format("ui.units.mana", ("amount", m.Cost))) : m.IsStrategy ? GameText.Get("ui.combat.party") : "";
            rows.Add(new(m.Name, value, () => controller.SelectedMoves[owner][party] = move.Id, Icon: m.Icon));
        }

        rows.Add(new(GameText.Get("ui.controls.automation"), controller.AutomationEnabled ? GameText.Get("ui.common.on") : GameText.Get("ui.common.off"), () => controller.SetAutomation(!controller.AutomationEnabled)));
    }

    public void Draw(Span<ushort> pixels)
    {
        if (owner >= 0)
        {
            string requirement = panel.Selection < moves.Count ? (moves[panel.Selection].Id.StartsWith("v:", StringComparison.Ordinal) ? GameText.Get("ui.combat.automaticChain") : moves[panel.Selection].Requirement) : "";
            panel.Draw(
                pixels,
                GameText.Format("ui.combat.playerCharacter", ("player", owner + 1), ("character", MoveCatalog.Name(party))),
                rows,
                GameText.Get("ui.combat.bindHint"),
                notice.Length > 0 ? notice : requirement,
                icons
            );
            return;
        }

        if (!input.Profile.Hints)
        {
            return;
        }

        for (int p = 0; p < 2; p++)
        {
            int index = ActorForPlayer(p);
            if (index < 0)
            {
                continue;
            }

            int member = index - runtime.Scene.PartyStartIndex;
            string basic = GameText.Format("ui.combat.inputHints", ("player", p + 1), ("attack", input.Hint(p, GameAction.Attack)), ("jump", input.Hint(p, GameAction.Jump)), ("moves", input.Hint(p, GameAction.Selector)), ("pause", input.Hint(p, GameAction.Pause)));
            int y = 84 + p * 58;
            for (int yy = y - 2; yy < y + 52; yy++)
            {
                pixels.Slice(yy * 640 + 8, 624).Fill(0x1082);
            }

            ControlPanel.Text(font, pixels, basic, 12, y, 0xd69a, 76);
            var slots = input.Profile.Players[p].Quick[member].Select((id, q) => input.Hint(p, GameAction.Quick1 + q) + ":" + ShortName(id)).ToArray();
            ControlPanel.Text(font, pixels, string.Join("  ", slots.Take(2)), 12, y + 17, 0xffe0, 76);
            ControlPanel.Text(font, pixels, string.Join("  ", slots.Skip(2)), 12, y + 34, 0xffe0, 76);
        }

        if (input.Notice.Length > 0)
        {
            ControlPanel.Text(font, pixels, input.Notice, 20, 380, 0xffe0, 74);
        }
    }

    private static string ShortName(string id)
    {
        string[] parts = id.Split(':');
        return parts.Length < 2 ? "--" : parts[0] switch
        {
            "v" => int.TryParse(parts[1], out int d) ? MoveCatalog.DirectionName(d) : "--",
            "p" => GameText.Format("ui.moves.magicShortcut", ("ability", parts[1])),
            "s" => GameText.Format("ui.moves.strategy", ("strategy", parts[1])),
            _ => "--"
        };
    }
}
