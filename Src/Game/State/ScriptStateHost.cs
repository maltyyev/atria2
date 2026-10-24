using Atria.Script;

namespace Atria.Game.State;

public enum ScriptEffectKind
{
    DialogueText,
    DialogueKey,
    QuestionText,
    QuestionKey,
    Battle,
    BattleWithPostAction,
    ForcedBattleWithPostAction,
    ModeChange
}

public sealed record ScriptEffect(ScriptEffectKind Kind, ScriptCall Call);
/// <summary>
/// Effects are synchronous, as original callbacks pump a nested modal loop. Implementations
/// must keep rendering/input responsive and may change state only through recovered rules.
/// Question handling writes the actual player choice to global10; never invent a choice.
/// </summary>
public interface IScriptEffects
{
    void Execute(ScriptEffect effect, GameState state);
}

/// <summary>Pure state portions of the 42 callback registry. No simulated battle/dialogue outcomes.</summary>
public sealed class ScriptStateHost(GameState state, IScriptEffects? effects = null) : IScriptHost, IScriptArgumentPolicy
{
    public GameState State => state;

    public bool SkipArguments(string name) => state.Mode == 1100
&& name is "enter_out" or "dialogue_script" or "dialogue_script2" or "question_dialog" or "question_dialog2" or "battle_start" or "battle_start2" or "battle_start3" or "get_xp" or "dungeon_enter" or "change_chip" or "game_end";
    public int Invoke(ScriptCall call)
    {
        int result = InvokeCore(call);
        if (call.Function is "set_global" or "set_event" or "eventflag_check" or "get_eventitem" or "lost_eventitem")
        {
            StoryQuests.Observe(state);
        }
        return result;
    }

    private int InvokeCore(ScriptCall call)
    {
        if (SkipArguments(call.Function))
        {
            if (call.Function == "enter_out")
            {
                state.EnterPermission = 0;
            }

            return 1;
        }

        QuestJournalTriggers.Observe(call, state);
        switch (call.Function)
        {
            case "set_global":
                Count(2);
                state.Globals[Index(0, GameState.GlobalCount)] = unchecked((short)Int(1));
                return 0;
            case "get_global":
                Count(1);
                return state.Globals[Index(0, GameState.GlobalCount)];
            case "set_event":
            case "eventflag_check":
                Count(3);
                state.Events[EventIndex()] = Int(2);
                return 0; // Registered alias of 42ceb0.
            case "get_event":
                Count(2);
                return state.Events[EventIndex()];
            case "set_eventstatus":
                Count(2);
                state.Journal.ObserveStatus(state, Index(0, GameState.EventStatusCount), Int(1));
                state.EventStatuses[Index(0, GameState.EventStatusCount)] = Int(1);
                return 0;
            case "get_eventstatus":
                Count(1);
                return state.EventStatuses[Index(0, GameState.EventStatusCount)];
            case "enter_out":
                Count(1);
                state.EnterPermission = Int(0);
                return 0;
            case "get_money":
                Count(1);
                state.Money = unchecked(state.Money + Int(0));
                return 0;
            case "lost_money":
                Count(1);
                state.Money = unchecked(state.Money - Int(0));
                if (state.Money < 0)
                {
                    state.Money = 0;
                }

                return 0;
            case "check_money":
                Count(0);
                return state.Money;
            case "get_item":
                {
                    Count(1);
                    int item = Int(0);
                    if (item < 0)
                    {
                        throw Error("STATE_INDEX", "Negative item identifier.");
                    }

                    if (item < 84)
                    {
                        state.OwnedItems[item] = unchecked(state.OwnedItems[item] + 1);
                        state.AvailableItems[item] = unchecked(state.AvailableItems[item] + 1);
                    }
                    else if (item == 84)
                    {
                        state.ResourceCounterDc88 = unchecked(state.ResourceCounterDc88 + 1);
                    }
                    else if (item == 85)
                    {
                        state.ResourceCounterDc88 = unchecked(state.ResourceCounterDc88 + 5);
                    }
                    else if (item == 86)
                    {
                        state.ResourceCounterFc9a = unchecked(state.ResourceCounterFc9a + 5);
                    }
                    else if (item == 87)
                    {
                        state.ResourceCounterFc9a = unchecked(state.ResourceCounterFc9a + 30);
                    }
                    // 42d760 intentionally returns zero without an effect for ids >87.
                    return 0;
                }

            case "lost_item":
                {
                    Count(1);
                    int item = Index(0, GameState.InventoryBankCount);
                    state.OwnedItems[item] = unchecked(state.OwnedItems[item] - 1);
                    state.AvailableItems[item] = unchecked(state.AvailableItems[item] - 1);
                    if (state.OwnedItems[item] < 0)
                    {
                        state.OwnedItems[item] = 0;
                        state.AvailableItems[item] = 0;
                    }

                    return 0;
                }

            case "check_item":
                Count(1);
                return state.OwnedItems[Index(0, GameState.InventoryBankCount)] != 0 ? 1 : 0;
            case "get_eventitem":
                Count(1);
                state.SetQuestItemFlag(Index(0, GameState.QuestItemCount), 1);
                return 0;
            case "lost_eventitem":
                Count(1);
                state.SetQuestItemFlag(Index(0, GameState.QuestItemCount), 0);
                return 0;
            case "check_eventitem":
                Count(1);
                return state.GetQuestItemFlag(Index(0, GameState.QuestItemCount)) != 0 ? 1 : 0;
            case "member_inout":
                Count(2);
                state.SetMemberStatus(Index(0, GameState.PartyCount), Int(1));
                return 0;
            case "member_check":
                Count(1);
                return state.GetMemberStatus(Index(0, GameState.PartyCount));
            case "get_xp":
                Count(1);
                state.PendingExperience = Int(0);
                state.ExperiencePending = 1;
                return 0;
            case "subevent_position":
                Count(3);
                Int(0);
                Int(1);
                Int(2);
                return 0; // Verified callback body has no state write.
            case "event_count_plus":
                OptionalIgnoredArgument();
                state.EventCounter = unchecked(state.EventCounter + 1);
                return 0;
            case "get_event_count":
                OptionalIgnoredArgument();
                return state.EventCounter;
            case "game_score_plus":
                Count(1);
                state.Score = unchecked(state.Score + Int(0));
                return 1;
            case "shop_number":
                Count(1);
                state.ShopNumber = Int(0);
                return 1;
            case "set_daycount":
                Count(1);
                state.DayCounterStorage = (state.DayCounterStorage & ~255) | (Int(0) & 255);
                return 1;
            case "get_daycount":
                Count(0);
                return state.ScriptDay;
            case "change_chip":
                Count(1);
                state.ChangedChip = Int(0);
                return 1;
            case "set_dungeonfog":
                Count(1);
                state.DungeonFog = Int(0);
                state.DungeonFogDirty = 1;
                return 1;
            case "newchar_create":
                Count(1);
                state.NewCharacterId = Int(0);
                state.NewCharacterPending = 1;
                return 0;
            case "visual_play":
                Count(0);
                state.VisualMode = 2;
                return 0;
            case "script_end":
                Count(0);
                state.ScriptActive = 0;
                return 0;
            case "people_name":
                {
                    if (call.Arguments.Count % 3 != 0 || call.Arguments.Count > 60)
                    {
                        throw Error("ARGUMENT_COUNT", "Expected at most twenty people triples.");
                    }

                    var people = new List<ScriptPerson>();
                    for (int i = 0; i < call.Arguments.Count; i += 3)
                    {
                        people.Add(new(Bytes(i, 127), Bytes(i + 1, 127), Bytes(i + 2, 127)));
                    }

                    state.People = people.AsReadOnly();
                    return 0;
                }

            case "dungeon_enter":
                Count(2);
                byte[] map = Bytes(0, 19);
                byte[] script = Bytes(1, 19);
                state.PendingDungeonMap = map;
                state.PendingDungeonScript = script;
                state.DungeonEntryPending = 1;
                return 1;
            case "dialogue_script":
                ValidateDialogue(false);
                Effect(ScriptEffectKind.DialogueText);
                return 1;
            case "dialogue_script2":
                ValidateDialogue(true);
                Effect(ScriptEffectKind.DialogueKey);
                return 1;
            case "question_dialog":
                ValidateDialogue(false);
                Effect(ScriptEffectKind.QuestionText);
                return 0;
            case "question_dialog2":
                ValidateDialogue(true);
                Effect(ScriptEffectKind.QuestionKey);
                return 0;
            case "battle_start":
            case "battle_start2":
            case "battle_start3":
                Count(1);
                if (call.Function != "battle_start")
                {
                    RequireEffects();
                }

                state.PendingBattle = Int(0);
                state.BattleVariant = call.Function == "battle_start3" ? 1 : 0;
                state.EndingResetFlag = 1; //42cdd0; plain battle_start returns with a deferred transition.
                if (call.Function != "battle_start")
                {
                    Effect(call.Function == "battle_start2" ? ScriptEffectKind.BattleWithPostAction : ScriptEffectKind.ForcedBattleWithPostAction);
                }

                return 0;
            case "game_end":
                Count(1);
                RequireEffects();
                int ending = Int(0);
                if (ending == 0)
                {
                    state.EndingResetFlag = 0;
                    state.Mode = 1100;
                }
                else
                {
                    state.EndingSelection = ending;
                    state.Mode = 1000;
                }

                state.ModeStackDepth = -1;
                Effect(ScriptEffectKind.ModeChange);
                return 1;
            default:
                throw Error("UNIMPLEMENTED_BUILTIN", $"No recovered callback named '{call.Function}'.");
        }

        ScriptException Error(string code, string detail) => new(code, call.Member, call.ByteOffset, detail);
        void Count(int count)
        {
            if (call.Arguments.Count != count)
            {
                throw Error("ARGUMENT_COUNT", $"{call.Function} requires {count} arguments.");
            }
        }

        int Int(int index)
        {
            if ((uint)index >= call.Arguments.Count || call.Arguments[index].IsString)
            {
                throw Error("ARGUMENT_TYPE", "Expected scalar callback argument.");
            }

            return call.Arguments[index].Integer;
        }

        int Index(int argument, int count)
        {
            int index = Int(argument);
            if ((uint)index >= count)
            {
                throw Error("STATE_INDEX", $"Index {index} is outside recovered storage domain of {count}.");
            }

            return index;
        }

        int EventIndex()
        {
            long index = (long)Int(0) * 10 + Int(1);
            if (index < 0 || index >= GameState.EventCount)
            {
                throw Error("STATE_INDEX", "Flattened event index exceeds recovered array.");
            }

            return (int)index;
        }

        byte[] Bytes(int index, int max = int.MaxValue)
        {
            if ((uint)index >= call.Arguments.Count || !call.Arguments[index].IsString)
            {
                throw Error("ARGUMENT_TYPE", "Expected raw string callback argument.");
            }

            if (call.Arguments[index].Bytes.Length > max)
            {
                throw Error("STRING_CAPACITY", "String exceeds original destination capacity.");
            }

            return call.Arguments[index].Bytes.ToArray();
        }

        void OptionalIgnoredArgument()
        {
            if (call.Arguments.Count > 1)
            {
                throw Error("ARGUMENT_COUNT", "Expected zero or one ignored scalar argument.");
            }

            if (call.Arguments.Count == 1)
            {
                Int(0);
            }
        }

        void ValidateDialogue(bool indexed)
        {
            if (call.Arguments.Count < 2 || indexed && call.Arguments.Count != 3)
            {
                throw Error("ARGUMENT_COUNT", "Invalid dialogue callback arguments.");
            }

            Int(0);
            Int(1);
            for (int i = 2; i < call.Arguments.Count; i++)
            {
                Bytes(i, indexed ? 39 : int.MaxValue);
            }
        }

        void RequireEffects()
        {
            if (effects is null)
            {
                throw Error("UNIMPLEMENTED_EFFECT", $"Interactive effect '{call.Function}' needs an implemented engine subsystem.");
            }
        }

        void Effect(ScriptEffectKind kind)
        {
            RequireEffects();
            effects!.Execute(new(kind, call), state);
        }
    }
}
