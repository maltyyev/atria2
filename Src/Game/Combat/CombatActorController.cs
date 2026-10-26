using Atria.Game.Controls;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Combat;

public interface ICombatControlEffects : ICombatFrameEffects
{
    bool GlobalEffectBusy
    {
        get;
    }

    void ShowRankNotice(int rank, uint milliseconds);
}

/// <summary>404c40 actual manual routing and40e1a0 type9 dispatch. Unrecovered AI types diagnose.</summary>
public sealed class CombatActorController : ICombatActorActions
{
    private const uint MaximumForecastOpportunityInterval = 100;
    private readonly CombatSceneRuntime runtime;
    private readonly GameState state;
    private readonly CombatScanInput input;
    private readonly ICombatControlEffects effects;
    private readonly Func<int> nextRandom;
    private readonly byte[][] rows = [new byte[8], new byte[8]];
    private readonly CombatActorState[] bank;
    private readonly uint[] previousAutomaticOpportunity;
    private readonly Action<int> sound, rankNotice, itemNotice;
    private int leaderHeldSince;
    private uint now;
    internal PartySpellDecision? Spells { get; set; }
    public bool CommandSelectionActive
    {
        get; private set;
    }
    public bool AutomationEnabled
    {
        get; private set;
    }
    public int ControlRank { get; private set; } = 3;
    public string[][] SelectedMoves { get; } = [new string[6], new string[6]];
    public bool[] SelectorOpen { get; } = new bool[2];

    public void SetAutomation(bool enabled)
    {
        if (enabled != AutomationEnabled)
        {
            ToggleAutomation();
        }
    }

    private readonly CombatScanInput[] semanticWalk = new CombatScanInput[2];
    private static readonly byte[] semanticDirections = [0, 1, 2, 3, 4, 5, 6, 7];
    private static readonly int[] branchDirections = [1, 2, 3, 4, 6, 7, 8, 9];
    private static readonly string[] partyPrefixes = ["0/", "1/", "2/", "3/", "4/", "5/"];
    public bool ExecuteMove(int actorIndex, string id, uint milliseconds) => CombatSemanticControl.Execute(id, runtime, runtime.Actors[actorIndex], state, milliseconds, effects.GlobalEffectBusy, sound, rankNotice, itemNotice);
    public CombatActorController(
        CombatSceneRuntime runtime,
        GameState state,
        ControlBindings bindings,
        CombatScanInput input,
        ICombatControlEffects effects,
        Func<int> nextNativeRandom
    )
    {
        this.runtime = runtime;
        this.state = state;
        this.input = input;
        this.effects = effects;
        nextRandom = nextNativeRandom;
        for (int p = 0; p < 2; p++)
        {
            for (int a = 0; a < 8; a++)
            {
                rows[p][a] = bindings[p, a];
            }
        }

        bank = runtime.Actors.Select(a => a.State).ToArray();
        previousAutomaticOpportunity = new uint[bank.Length];
        sound = effects.PlaySound;
        rankNotice = RankNotice;
        itemNotice = ItemNotice;
        for (int p = 0; p < 2; p++)
        {
            int player = p;
            semanticWalk[p] = new(scan => scan < 4 ? input.Modern!.Held(player, (GameAction)scan) : scan == 4 && input.Modern!.Profile.Players[player].Classic && input.Modern.Held(player, GameAction.Attack));
        }
    }

    public void Process(int actorIndex, uint milliseconds)
    {
        now = milliseconds;
        CombatActorImage image = runtime.Actors[actorIndex];
        CombatActorState actor = image.State;
        if (actor.ControlledBy == 0)
        {
            Automatic(actorIndex, milliseconds);
        }
        else if (input.Modern is { } modern)
        {
            ProcessSemantic(actorIndex, image, modern, milliseconds);
        }
        else
        {
            if ((uint)image.ControlPlayer >= 2)
            {
                throw new InvalidOperationException("Controlled actor has no actual input row.");
            }

            byte[] row = rows[image.ControlPlayer];
            if (actor.ControlRole != 0)
            {
                if (!input.IsHeld(row[4]))
                {
                    CommandSelectionActive = false;
                    leaderHeldSince = unchecked((int)milliseconds);
                }
                else if (unchecked(leaderHeldSince + 700) < unchecked((int)milliseconds))
                {
                    CommandSelectionActive = true;
                    NavigateCommands(actor, image.Binding!.Info, row);
                }
            }

            if (input.Query(row[7]))
            {
                CombatPartyControl.Cycle(runtime.Actors, runtime.Scene.PartyStartIndex, actorIndex);
            }
            else if (actor.ActionFlags < 2)
            {
                if (input.Query(row[4]))
                {
                    CombatManualControl.Attack(actor, image.Binding!.Info, image.Animation!, milliseconds);
                }
                else if (input.Query(row[5]))
                {
                    if (!CombatJumpRuntime.Begin(actor, image.Binding!.Info, image.Animation!, milliseconds))
                    {
                        CombatManualControl.Walk(actor, image.Binding!.Info, image.Animation!, row, input, milliseconds);
                    }
                }
                else if (input.Query(row[6]))
                {
                    CombatManualControl.ExecuteCommand(runtime, image, state, milliseconds, effects.GlobalEffectBusy, sound, rankNotice, itemNotice);
                }
                else
                {
                    CombatManualControl.Walk(actor, image.Binding!.Info, image.Animation!, row, input, milliseconds);
                }
            }
            else
            {
                CombatManualControl.ContinueAction(image, row, input, milliseconds);
            }
        }

        if (input.Modern is { } modernInput ? modernInput.Press(0, GameAction.Automation) || modernInput.Press(1, GameAction.Automation) : input.Query(30))
        {
            ToggleAutomation();
        }
    }

    private void ProcessSemantic(int actorIndex, CombatActorImage image, InputRouter modern, uint milliseconds)
    {
        int player = image.ControlPlayer;
        if ((uint)player >= 2)
        {
            throw new InvalidOperationException("Missing semantic player.");
        }

        CombatActorState a = image.State;
        var info = image.Binding!.Info;
        var p = modern.Profile.Players[player];
        int party = actorIndex - runtime.Scene.PartyStartIndex;
        if (SelectorOpen[player])
        {
            return;
        }

        if (modern.Press(player, GameAction.Switch))
        {
            CombatPartyControl.Cycle(runtime.Actors, runtime.Scene.PartyStartIndex, actorIndex);
            modern.ClearPlayer(player);
            return;
        }

        string? request = null;
        for (int q = 0; q < 4; q++)
        {
            if (modern.PressQuick(player, q))
            {
                request = p.Quick[party][q];
                break;
            }
        }

        if (request is null)
        {
            foreach (var pair in p.Direct)
            {
                if (pair.Key.StartsWith(partyPrefixes[party], StringComparison.Ordinal) && modern.Press(player, pair.Value))
                {
                    request = pair.Key[(pair.Key.IndexOf('/') + 1)..];
                    break;
                }
            }
        }

        if (request is null)
        {
            for (int i = 0; i < 8; i++)
            {
                if (modern.Press(player, GameAction.BackDown + i))
                {
                    request = "v:" + branchDirections[i];
                    break;
                }
            }
        }

        if (request is not null)
        {
            RequestSemanticMove(actorIndex, player, request, modern, milliseconds);
            return;
        }

        if (modern.Press(player, GameAction.Execute))
        {
            string selected = SelectedMoves[player][party] ?? "";
            if (selected.Length == 0)
            {
                SelectedMoves[player][party] = selected = MoveCatalog.FromInfo(info).FirstOrDefault(m => m.IsMagic)?.Id ?? "s:3";
            }

            RequestSemanticMove(actorIndex, player, selected, modern, milliseconds);
            return;
        }

        if (a.ActionFlags < 2)
        {
            if (modern.Press(player, GameAction.Attack))
            {
                CombatManualControl.Attack(a, info, image.Animation!, milliseconds);
            }
            else if (modern.Press(player, GameAction.Jump))
            {
                if (!CombatJumpRuntime.Begin(a, info, image.Animation!, milliseconds))
                {
                    CombatManualControl.Walk(a, info, image.Animation!, semanticDirections, semanticWalk[player], milliseconds, p.Classic ? null : modern.Held(player, GameAction.Run));
                }
            }
            else
            {
                CombatManualControl.Walk(a, info, image.Animation!, semanticDirections, semanticWalk[player], milliseconds, p.Classic ? null : modern.Held(player, GameAction.Run));
            }
        }
        else
        {
            bool branch = p.Classic ? modern.Held(player, GameAction.Jump) : modern.Held(player, GameAction.Branch);
            if (branch)
            {
                int d = CombatSemanticControl.Direction(modern, player, a.Facing);
                if (d != 0)
                {
                    CombatSemanticControl.Variation(image, d, 0, milliseconds);
                }
            }
            else if (modern.Held(player, GameAction.Attack))
            {
                CombatSemanticControl.AirAttack(image, milliseconds);
            }
            // Do not retain an early attack/jump press until an unrelated future legal state.
            modern.Press(player, GameAction.Attack);
            modern.Press(player, GameAction.Jump);
        }
    }

    private void RequestSemanticMove(int actorIndex, int player, string id, InputRouter input, uint now)
    {
        bool result = ExecuteMove(actorIndex, id, now);
        if (!result && id.StartsWith("v:", StringComparison.Ordinal))
        {
            result = CombatSemanticControl.QuickVariation(runtime.Actors[actorIndex], id, now);
        }

        input.Notice = result ? "" : GameText.Get("ui.combat.moveUnavailable");
        if (result)
        {
            input.Rumble(player);
        }
    }

    private void Automatic(int index, uint milliseconds)
    {
        uint opportunityInterval = Math.Clamp(milliseconds - previousAutomaticOpportunity[index], 1u, MaximumForecastOpportunityInterval);
        previousAutomaticOpportunity[index] = milliseconds;
        CombatActorImage image = runtime.Actors[index];
        CombatActorState actor = image.State;
        if (!effects.GlobalEffectBusy && Spells is { } spells)
        {
            int commandIndex = spells.Select(index, milliseconds, out byte facing);
            if (commandIndex >= 0)
            {
                byte previousFacing = actor.Facing;
                actor.Facing = facing;
                var command = image.Binding!.Info.Commands[commandIndex];
                if (ExecuteMove(index, $"p:{command.Icon}", milliseconds))
                {
                    spells.Cast(index, milliseconds);
                    return;
                }
                actor.Facing = previousFacing;
            }
        }
        if (actor.Team >= 100 && (image.Binding!.Info.Type <= 5 || image.Binding.Info.Type is >= 46 and <= 51))
        {
            int target = runtime.TargetIndices[index];
            if (target < 0 || bank[target].Health <= 0 || bank[target].Active == 0 || bank[target].SpawnCondition != 0)
            {
                long nearest = long.MaxValue;
                target = -1;
                for (int i = 0; i < bank.Length; i++)
                {
                    var candidate = bank[i];
                    if (candidate.Team >= 100 || candidate.Health <= 0 || candidate.Active == 0 || candidate.SpawnCondition != 0)
                    {
                        continue;
                    }
                    long distance = Math.Abs((long)candidate.TrackingX - actor.TrackingX) + Math.Abs((long)candidate.Depth - actor.Depth);
                    if (distance < nearest)
                    {
                        nearest = distance;
                        target = i;
                    }
                }
            }
            if (target >= 0 && PartyAirCombat.TryAct(image, runtime.Actors[target], milliseconds, opportunityInterval))
            {
                runtime.TargetIndices[index] = target;
                return;
            }
        }
        int flags = actor.ActionFlags;
        if (flags is not (0 or 1))
        {
            return;
        }

        int type = image.Binding!.Info.Type;
        if (flags == 0)
        {
            int target = CombatTargetSelection.Select(bank, runtime.TargetIndices, index, 0, bank.Length, milliseconds, nextRandom);
            runtime.TargetIndices[index] = target;
            if (type == 9)
            {
                BanditAiDecision.Begin(actor, target < 0 ? null : bank[target], image.Animation!, runtime.Scene.Header73, runtime.Scene.Header77, milliseconds, nextRandom);
            }
            else if (type == 8)
            {
                EnemyType8AiDecision.Begin(actor, target < 0 ? null : bank[target], image.Animation!, runtime.Scene.Header73, runtime.Scene.Header77, milliseconds, nextRandom);
            }
            else if (CommonEnemyAiDecision.Supports(type))
            {
                CommonEnemyAiDecision.Begin(actor, type, target < 0 ? null : bank[target], image.Animation!, runtime.Scene.Header73, runtime.Scene.Header77, milliseconds, nextRandom);
            }
            else if ((uint)type <= 5 || type is >= 46 and <= 51)
            {
                if (!PartyAiDecision.Begin(
                    actor,
                    type >= 46 ? 0 : type,
                    target < 0 ? null : bank[target],
                    image.Animation!,
                    runtime.Scene.Header73,
                    runtime.Scene.Header77,
                    milliseconds,
                    nextRandom
                ))
                {
                    runtime.TargetIndices[index] = -1;
                }
            }
            // Native default dispatch returns after target selection.
        }
        else if (type == 9)
        {
            int target = runtime.TargetIndices[index];
            BanditAiDecision.Continue(actor, target < 0 ? null : bank[target], image.Animation!, image.Binding.Info, milliseconds, nextRandom);
        }
        else if ((uint)type <= 5 || type is >= 46 and <= 51)
        {
            int target = runtime.TargetIndices[index];
            PartyAiDecision.Continue(actor, type >= 46 ? 1 : type, target < 0 ? null : bank[target], image.Animation!, image.Binding.Info, milliseconds, nextRandom);
        }
        else if (type == 8)
        {
            int target = runtime.TargetIndices[index];
            EnemyType8AiDecision.Continue(actor, target < 0 ? null : bank[target], image.Animation!, image.Binding.Info, milliseconds, nextRandom);
        }
        else if (CommonEnemyAiDecision.Supports(type))
        {
            int target = runtime.TargetIndices[index];
            CommonEnemyAiDecision.Continue(actor, type, target < 0 ? null : bank[target], image.Animation!, image.Binding.Info, milliseconds, nextRandom, state.Globals[51]);
        }
        else
        {
            BanditAiDecision.AdvanceMovement(actor, runtime.TargetIndices[index] < 0 ? null : bank[runtime.TargetIndices[index]], image.Animation!, milliseconds);
        }
    }

    private void NavigateCommands(CombatActorState actor, CombatInfoCharacter info, byte[] row)
    {
        int category = actor.CommandCategory;
        if ((uint)category >= 10)
        {
            throw new InvalidDataException("Combat command category exceeds native count table.");
        }

        if (input.Query(row[3]))
        {
            if (category < 9 && info.CommandCounts[category + 1] != 0 && category < actor.MagicRank)
            {
                if (category == 0)
                {
                    actor.ControlRank = actor.CommandSelection;
                }

                actor.CommandSelection = 0;
                actor.CommandScroll = 0;
                actor.CommandCategory = category + 1;
            }

            sound(6);
        }
        else if (input.Query(row[2]))
        {
            if (category != 0)
            {
                actor.CommandSelection = 0;
                actor.CommandScroll = 0;
                actor.CommandCategory = category - 1;
                if (category == 1)
                {
                    actor.CommandSelection = actor.ControlRank;
                }
            }

            sound(6);
        }
        else if (input.Query(row[1]))
        {
            int selected = actor.CommandSelection, count = info.CommandCounts[category];
            if (selected < 4 && selected < count - 1)
            {
                actor.CommandSelection = selected + 1;
            }
            else if (unchecked(actor.CommandScroll + 4) < count)
            {
                actor.CommandScroll = unchecked(actor.CommandScroll + 1);
            }

            sound(6);
        }
        else if (input.Query(row[0]))
        {
            int selected = actor.CommandSelection;
            if (selected < 1)
            {
                if (actor.CommandScroll != 0)
                {
                    actor.CommandScroll = unchecked(actor.CommandScroll - 1);
                }
            }
            else
            {
                actor.CommandSelection = selected - 1;
            }

            sound(6);
        }
    }

    private void ToggleAutomation()
    {
        if (!AutomationEnabled)
        {
            for (int i = runtime.Scene.PartyStartIndex; i < bank.Length; i++)
            {
                bank[i].SavedControl = bank[i].ControlledBy;
                bank[i].ControlledBy = 0;
            }
        }
        else
        {
            for (int i = runtime.Scene.PartyStartIndex; i < bank.Length; i++)
            {
                CombatActorState actor = bank[i];
                byte saved = actor.SavedControl;
                if (saved == 0)
                {
                    continue;
                }

                if (actor.Health > 0)
                {
                    actor.ControlledBy = saved;
                }
                else
                {
                    for (int j = runtime.Scene.PartyStartIndex; j < bank.Length; j++)
                    {
                        if (bank[j].Active == 0 || bank[j].ControlledBy != 0 || bank[j].Health <= 0)
                        {
                            continue;
                        }

                        runtime.Actors[j].ControlPlayer = runtime.Actors[i].ControlPlayer;
                        runtime.Actors[i].ControlPlayer = -1;
                        bank[j].ControlledBy = saved;
                        break;
                    }
                }

                actor.SavedControl = 0;
            }
        }

        AutomationEnabled = !AutomationEnabled;
    }

    private void RankNotice(int rank)
    {
        ControlRank = rank;
        effects.ShowRankNotice(rank, now);
    }

    private void ItemNotice(int id) => effects.ShowConsumedItemNotice(id, now);
}
