using System.Buffers.Binary;
using Atria.Game.State;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.State;

public static class StateTests
{
    public static string Run(string resourceRoot)
    {
        var state = new GameState();
        var host = new ScriptStateHost(state);
        int Call(string name, params int[] args) => host.Invoke(new("TEST", name, 0, args.Select(v => new ScriptValue(v)).ToArray()));
        void Check(bool value, string message)
        {
            if (!value)
            {
                throw new InvalidOperationException(message);
            }
        }

        Call("set_global", 7, 65535);
        Check(Call("get_global", 7) == -1, "Signed 16-bit global truncation.");
        Call("set_global", 7, 65536);
        Check(Call("get_global", 7) == 0, "Global low16 write.");
        Call("eventflag_check", 32, 2, 71);
        Check(Call("get_event", 32, 2) == 71, "eventflag_check is a setter alias.");
        Call("set_event", 2, 11, 33);
        Check(Call("get_event", 3, 1) == 33, "Native event flattening.");
        state.Money = int.MaxValue;
        Call("get_money", 1);
        Check(state.Money == int.MinValue, "Money wrapping.");
        state.Money = 3;
        Call("lost_money", 7);
        Check(state.Money == 0, "Money floor.");
        Call("get_item", 3);
        Check(state.OwnedItems[3] == 1 && state.AvailableItems[3] == 1, "Both item banks increment.");
        state.AvailableItems[3] = 0;
        Call("lost_item", 3);
        Check(state.OwnedItems[3] == 0 && state.AvailableItems[3] == -1, "Available count only clamps when owned goes negative.");
        Call("lost_item", 3);
        Check(state.OwnedItems[3] == 0 && state.AvailableItems[3] == 0, "Joint item underflow floor.");
        Call("get_item", 84);
        Call("get_item", 85);
        Call("get_item", 86);
        Call("get_item", 87);
        Check(state.ResourceCounterDc88 == 6 && state.ResourceCounterFc9a == 35 && state.OwnedItems[84] == 0, "Special item routes.");
        Call("get_eventitem", 50);
        Check(Call("check_eventitem", 50) == 1 && state.GetQuestItemFlag(49) == 0, "Quest item packed stride.");
        Call("member_inout", 9, -1);
        Check(Call("member_check", 9) == -1 && state.GetMemberStatus(8) == 0, "Party packed stride.");
        Check(BinaryPrimitives.ReadInt32LittleEndian(PartyCodec.Encode(state.Party).AsSpan(9 * 280 + 60, 4)) == -1, "Party membership raw offset.");
        state.DayCounterStorage = 0x12345678;
        Call("set_daycount", 255);
        Check(state.DayCounterStorage == 0x123456ff && Call("get_daycount") == -1, "Day low-byte/signed getter.");
        state.Globals[71] = unchecked((short)0x8123);
        state.BattleVariant = unchecked((int)0x81234567);
        Check(state.Globals[70] == 0x4567 && state.Globals[71] == unchecked((short)0x8123), "Battle WORD alias preserves neighboring global.");
        Call("set_global", 70, 1);
        Check(state.BattleVariant == 1 && state.Globals[71] == unchecked((short)0x8123), "Global/battle WORD alias reverse write.");
        Call("event_count_plus", 777);
        Call("event_count_plus");
        Check(Call("get_event_count") == 2, "Event counter ignores argument.");
        Call("get_xp", 12000);
        Check(state.PendingExperience == 12000 && state.ExperiencePending == 1, "XP queues pending award.");
        ExpectError(() => Call("get_global", 1000), "STATE_INDEX");
        ExpectError(() => Call("member_check", 10), "STATE_INDEX");
        ExpectError(() => Call("check_eventitem", 51), "STATE_INDEX");
        Check(Call("battle_start", 2) == 0 && state.PendingBattle == 2 && state.EndingResetFlag == 1 && state.BattleVariant == 0, "Plain battle queues42cdd0 transition without fabricating or invoking immediate combat.");
        ExpectError(() => Call("battle_start2", 1), "UNIMPLEMENTED_EFFECT");
        Check(state.PendingBattle == 2, "Unimplemented immediate battle must fail before changing pending encounter.");
        state.Mode = 1100;
        var skipProgram = ScriptProgram.Parse("TEST", "run(){int a;a=0;get_xp(a=12);set_global(1,a);enter_out(1);}"u8);
        new ScriptMachine(skipProgram, host).Invoke("run");
        Check(state.Globals[1] == 0 && state.EnterPermission == 0 && state.PendingExperience == 12000, "Suppressed callback must skip argument side effects and preserve pending XP.");
        using var archive = BinderArchive.Open(Path.Combine(resourceRoot, "SCRIPT.000"));
        ScriptProgram Read(string name) => ScriptProgram.Parse(name, archive.ReadMember(archive.FindResource(System.Text.Encoding.ASCII.GetBytes(name))));
        var recorder = new ChoiceFixtureEffects();
        var libraryState = new GameState();
        var libraryHost = new ScriptStateHost(libraryState, recorder);
        var library = new ScriptMachine(Read("32_LIBRA"), libraryHost);
        library.Invoke("enter");
        library.Invoke("enter");
        library.Invoke("black_talk");
        Check(libraryState.Globals[821] == 1 && libraryState.People.Count == 4 && libraryState.Score == 5 && recorder.Calls.Count == 13, "Real state host library entry/repeat/black-first sequence.");
        Check(libraryState.Events[322] == 1, "Library event state.");
        var questState = new GameState();
        var questHost = new ScriptStateHost(questState, recorder);
        var subevent = new ScriptMachine(Read("SUBEVENT"), questHost);
        subevent.Invoke("sub_063");
        Check(questState.EventStatuses[63] == 1 && questState.EventStatuses[161] == 1 && questState.EventStatuses[162] == 1, "Original quest acceptance must retain both missing encounter triggers.");
        subevent.Invoke("suba_161");
        Check(questState.Globals[901] == 0 && questState.EventStatuses[63] == 1, "Recovered stage161 guard leaves an unstarted first encounter unchanged.");
        questState.EventStatuses[63] = 2; // Explicit fixture branch setup, not a fabricated quest completion.
        subevent.Invoke("sub_063");
        Check(questState.Money == 500 && questState.PendingExperience == 12000 && questState.ExperiencePending == 1 && questState.EventStatuses[63] == 4, "Existing quest reward callback branch.");
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(resourceRoot, "ATITEM.DAT")));
        var messages = LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(resourceRoot, "TEXTmsg.dat")));
        GameState initial = InitialGameState.Create(items, messages);
        Check(initial.WorldX == 4 && initial.WorldY == 4 && initial.Money == 200 && initial.EventStatuses[0] == 4 && initial.Year == 1450
&& initial.Month == 3 && initial.Day == 14 && initial.EventCounter == 1, "Fresh initial world/date/flags.");
        Check(initial.GetPartyInt(0, 72) == 208 && initial.GetPartyInt(0, 80) == 158 && initial.GetPartyInt(0, 204) == 3
&& initial.GetPartyInt(0, 208) == 5 && initial.GetPartyInt(0, 212) == 29 && initial.GetPartyInt(0, 252) == 3, "First active actor static fixture.");
        Check(initial.GetPartyInt(1, 72) == 114 && initial.GetPartyInt(1, 80) == 270 && initial.GetPartyInt(1, 204) == 2
&& initial.GetPartyInt(1, 208) == 3 && initial.GetPartyInt(1, 212) == 52 && initial.GetPartyInt(1, 252) == 4, "Second active actor static fixture.");
        Check(initial.OwnedItems[0] == 1 && initial.OwnedItems[18] == 1 && initial.OwnedItems[25] == 1 && initial.OwnedItems[26] == 1
&& initial.AvailableItems[0] == 0, "Starting inventory is not decremented by equipment initialization.");
        initial.OwnedItems[150] = 123;
        initial.SetPartyInt(5, 88, 77);
        initial.SetPartyInt(7, 88, 88);
        InitialGameState.ResetRecoveredCore(initial, items, messages);
        Check(initial.OwnedItems[150] == 123 && initial.GetPartyInt(5, 88) == 77 && initial.GetPartyInt(7, 88) == 88, "Reset must preserve fields omitted by native reset rather than blanket-clear all storage.");
        return "State checks passed: widths/aliases/bounds, callback arithmetic, mode suppression, library/quest fixtures, recovered initial state and two active-party stat fixtures; interactive outcomes remain fixture-controlled.";
    }

    private static void ExpectError(Action action, string code)
    {
        try
        {
            action();
        }
        catch (ScriptException error) when (error.Code == code)
        {
            return;
        }

        throw new InvalidOperationException("Expected " + code);
    }

    private sealed class ChoiceFixtureEffects : IScriptEffects
    {
        public List<ScriptEffect> Calls { get; } = [];

        public void Execute(ScriptEffect effect, GameState state)
        {
            if (effect.Kind is not (ScriptEffectKind.DialogueText or ScriptEffectKind.DialogueKey or ScriptEffectKind.QuestionText or ScriptEffectKind.QuestionKey))
            {
                throw new InvalidOperationException("Fixture does not simulate battles or mode changes.");
            }

            Calls.Add(effect);
            if (effect.Kind is ScriptEffectKind.QuestionText or ScriptEffectKind.QuestionKey)
            {
                state.Globals[10] = 1;
            }
        }
    }
}
