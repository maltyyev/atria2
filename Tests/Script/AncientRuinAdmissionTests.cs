using Atria.Game.State;
using Atria.Script;

namespace Atria.Tests.Script;

internal static class AncientRuinAdmissionTests
{
    public static void Run(ScriptProgram town, ScriptProgram doorway)
    {
        foreach (int completed in new[] { 0, 1 })
        {
            foreach (int status in new[] { 0, 1, 3, 4 })
            {
                var state = new GameState();
                var host = new ScriptStateHost(state, new RecordedDialogue());
                host.Invoke(new("fixture", "set_event", 0, [new(4), new(1), new(1)]));
                host.Invoke(new("fixture", "set_event", 0, [new(5), new(1), new(completed)]));
                state.EventStatuses[5] = status;
                new ScriptMachine(town, host).Invoke("pre_enter");
                Check(state.EnterPermission == (completed == 0 ? 1 : 0), "completed ruins reject world admission even with forced status 1");

                // Also inspect the doorway independently, as a historical save could start inside.
                new ScriptMachine(doorway, host).Invoke("pre_enter");
                string target = System.Text.Encoding.ASCII.GetString(state.PendingDungeonMap);
                Check(target == (completed == 0 ? "md2.amf" : status == 1 ? "ed1.amf" : ""),
                    "only forced completed/status-1 combination selects the missing reference");
                Check(state.EnterPermission == 0, "doorway never enters an interior view");
            }
        }
    }

    private sealed class RecordedDialogue : IScriptEffects
    {
        public void Execute(ScriptEffect effect, GameState state)
        {
            Check(effect.Kind == ScriptEffectKind.DialogueKey, "admission has no gameplay effect besides recorded dialogue");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Ancient ruin admission: " + message);
    }
}
