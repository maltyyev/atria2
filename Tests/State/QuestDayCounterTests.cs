using Atria.Game.Exploration;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.State;

public static class QuestDayCounterTests
{
    public static string Run(string root, string? saveFile = null)
    {
        using var archive = BinderArchive.Open(Path.Combine(root, "SCRIPT.000"));
        byte[] source = archive.ReadMember(archive.FindResource("SUBEVENT"u8));
        var program = ScriptProgram.Parse("SUBEVENT", source);
        foreach (int quest in new[] { 24, 25, 26, 27, 86 })
        {
            var state = new GameState { Mode = 410, ScriptActive = 1, DayCounterStorage = 0x12345678 };
            state.EventStatuses[23] = 4;
            new ScriptMachine(program, new ScriptStateHost(state, new AcceptQuest())).Invoke($"sub_{quest:D3}");
            Check(state.EventStatuses[quest] == 1 && state.ScriptActive == 0 && state.DayCounterStorage == 0x12345600,
                $"Original quest {quest} accepts and resets only the timer byte");
            ExplorationRules.AdvanceTravelDay(state);
            Check(state.ScriptDay == 1 && state.DayCounterStorage == 0x12345601, "Travel advances the reset timer");
        }

        foreach (int elapsed in new[] { 20, 21 })
        {
            var state = new GameState { Mode = 410, ScriptActive = 1, DayCounterStorage = 47 };
            state.EventStatuses[23] = 4;
            var machine = new ScriptMachine(program, new ScriptStateHost(state, new AcceptQuest()));
            machine.Invoke("sub_024");
            for (int day = 0; day < elapsed; day++)
            {
                ExplorationRules.AdvanceTravelDay(state);
            }
            // Fixture: delivery completed; do not simulate the battle or destination event.
            state.EventStatuses[24] = 2;
            machine.Invoke("sub_024");
            Check(state.EventStatuses[24] == (elapsed == 20 ? 4 : 3)
                && state.Money == (elapsed == 20 ? 100 : 0)
                && state.PendingExperience == (elapsed == 20 ? 200 : 0), "Original 20-day deadline and reward remain exact");
        }

        var explicitState = new GameState { DayCounterStorage = 0x12345678 };
        var host = new ScriptStateHost(explicitState);
        int Call(params ScriptValue[] values) => host.Invoke(new("TEST", "set_daycount", 0, values));
        Check(Call(new ScriptValue(255)) == 1 && explicitState.DayCounterStorage == 0x123456ff && explicitState.ScriptDay == -1,
            "Explicit setter retains low-byte and signed getter behavior");
        Reject(() => Call(new ScriptValue("bad"u8.ToArray())), "ARGUMENT_TYPE");
        Reject(() => Call(new ScriptValue(1), new ScriptValue(2)), "ARGUMENT_COUNT");
        Check(explicitState.DayCounterStorage == 0x123456ff, "Rejected arguments cannot mutate the timer");

        if (saveFile is not null)
        {
            byte[] bytes = File.ReadAllBytes(saveFile);
            var state = NativeSaveImage.Parse(bytes).State;
            Check(state.EventStatuses[23] == 4 && state.EventStatuses[24] == 0, "User autosave is immediately before reported quest acceptance");
            new ScriptMachine(program, new ScriptStateHost(state, new AcceptQuest())).Invoke("sub_024");
            Check(state.EventStatuses[24] == 1 && state.ScriptDay == 0 && state.ScriptActive == 0, "Reported dialogue completes from real saved state");
            Check(bytes.SequenceEqual(File.ReadAllBytes(saveFile)), "User save remains unchanged on disk");
        }
        return "Quest timer: all five original acceptance scripts, travel increment, 20/21-day reward boundary, explicit setter, invalid arguments and optional read-only player-save replay passed.";
    }

    private sealed class AcceptQuest : IScriptEffects
    {
        public void Execute(ScriptEffect effect, GameState state)
        {
            if (effect.Kind is ScriptEffectKind.QuestionText or ScriptEffectKind.QuestionKey)
            {
                state.Globals[10] = 1;
            }
            else if (effect.Kind is not (ScriptEffectKind.DialogueText or ScriptEffectKind.DialogueKey))
            {
                throw new InvalidOperationException("Timer fixture cannot simulate battle or other effects.");
            }
        }
    }

    private static void Reject(Action action, string code)
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

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Quest timer: " + message);
        }
    }
}
