using System.Security.Cryptography;
using System.Text;
using Atria.Game.Navigation;
using Atria.Game.Persistence;
using Atria.Game.State;
using Atria.Game.Town;
using Atria.Script;

namespace Atria.Game;

public sealed partial class GameSession
{
    private void VerifyScenarioCompletions(int townSlot)
    {
        foreach (var (continuation, allowed) in Enum.GetValues<ScenarioContinuation>()
            .Select(value => (value, true)).Append((ScenarioContinuation.EnterTown, false)))
        {
            LoadSessionSlot(townSlot);
            state.Events[11] = state.Events[12] = state.Events[13] = state.Events[14] = state.Events[61] = state.Events[51] = 1;
            byte[] member = town.Scene.ScriptReference.ToArray();
            string handler = "pre_out";
            int expected = GameMode.Town;
            int returnMode = 0;
            switch (continuation)
            {
                case ScenarioContinuation.EnterBuilding:
                    town.ChooseAction(0, _ => { });
                    member = town.Scene.GetObject(town.Selection).GetString(1).ToArray();
                    handler = "pre_enter";
                    expected = GameMode.Interior + town.Scene.GetObject(town.Selection).GetInteger(7) * 10;
                    break;
                case ScenarioContinuation.LeaveTown:
                    town.ChooseAction(1, _ => { });
                    expected = GameMode.World;
                    break;
                case ScenarioContinuation.InteriorInteraction:
                case ScenarioContinuation.LeaveBuilding:
                    int kind = town.Scene.GetObject(town.Selection).GetInteger(7);
                    CommitLocation((LocationKind)(GameMode.Interior + kind * 10));
                    member = scriptMember;
                    expected = continuation == ScenarioContinuation.LeaveBuilding ? GameMode.Town : state.Mode;
                    returnMode = state.Mode;
                    break;
                case ScenarioContinuation.EnterTown:
                    LoadExplorationReturnMap("ground1.amf"u8.ToArray());
                    CommitLocation(LocationKind.World);
                    state.Events[51] = allowed ? 1 : 0;
                    admissionTarget = map!.SelectScene(state.WorldX, state.WorldY);
                    member = admissionTarget.ScriptMember.ToArray();
                    handler = "pre_enter";
                    expected = allowed ? GameMode.Town : GameMode.World;
                    break;
            }

            byte[] source = Read("SCRIPT.000", member);
            string name = Path.GetFileNameWithoutExtension(Encoding.ASCII.GetString(member)).ToUpperInvariant();
            var program = ScriptProgram.Parse(name, source);
            var recording = new CheckpointScriptHost(new ScriptStateHost(state, new ContinuationProbeEffects()));
            new ScriptMachine(program, recording).Invoke(handler);
            if ((state.EnterPermission != 0) != allowed)
                throw new InvalidOperationException("Continuation fixture admission differs from expectation: " + name);

            var point = new ScriptCheckpoint(name, handler, Convert.ToHexString(SHA256.HashData(source)), continuation, returnMode, recording.Capture());
            if (continuation == ScenarioContinuation.EnterTown)
                point = point with { ContinuationMode = 0 };
            activeScriptIdentity = point;
            activeScriptHost = recording;
            NativeSaveImage saved;
            try { saved = CaptureSaveImage(navigation.WorldMode, true, "continuation verification"); }
            finally { activeScriptIdentity = null; activeScriptHost = null; }
            PersistenceStore.WriteNative(92, saved);
            LoadSessionSlot(92);
            int money = state.Money, score = state.Score;
            ResumeSavedScenario();
            if (state.Mode != expected || state.Money != money || state.Score != score || resumeScript is not null)
                throw new InvalidOperationException("Scenario completion did not resume exactly once: " + continuation);
            if (continuation == ScenarioContinuation.LeaveBuilding && state.ScriptActive != 0)
                throw new InvalidOperationException("Building departure replayed the town arrival.");
            if (continuation == ScenarioContinuation.EnterTown && (state.ScriptActive != (allowed ? 1 : 0) || admissionTarget is not null))
                throw new InvalidOperationException("Town arrival must be queued once after restored admission.");

            Console.WriteLine($"Scenario completion passed: {continuation}, allowed={allowed}, original {name}/{handler}, {point.Calls.Length} recorded calls, destination {expected}.");
        }
        LoadSessionSlot(townSlot);
    }

    private sealed class ContinuationProbeEffects : IScriptEffects
    {
        public void Execute(ScriptEffect effect, GameState state)
        {
            if (effect.Kind is not (ScriptEffectKind.DialogueText or ScriptEffectKind.DialogueKey))
                throw new InvalidOperationException("Unexpected effect in continuation fixture: " + effect.Kind);
        }
    }
}
