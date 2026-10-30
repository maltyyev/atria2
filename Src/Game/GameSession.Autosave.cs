using System.Text;
using Atria.Game.Persistence;
using Atria.Script;

namespace Atria.Game;

public sealed partial class GameSession
{
    private string autosavePending = "";
    private string autosaveFailure = "";
    private CheckpointScriptHost? activeScriptHost;
    private ScriptCheckpoint? activeScriptIdentity, resumeScript;
    private bool loadingSnapshot;
    private int questCompletionBeforeCall;
    private Atria.Game.World.WorldSceneReference? scriptReturnLocation;
    private uint? nextBattleSeed;
    private void RequestAutosave(string reason)
    {
        if (!loadingSnapshot)
        {
            autosavePending = reason;
        }
    }

    private ScriptCheckpoint? CaptureScriptCheckpoint() => activeScriptIdentity is { } identity ? identity with
    {
        Calls = activeScriptHost!.Capture()
    }

    : null;
    private void FlushAutosave()
    {
        if (autosavePending.Length == 0 || activeScriptHost is not null || state.ScriptActive != 0 || state.EndingResetFlag != 0
|| state.ExperiencePending != 0 || state.NewCharacterPending != 0 || dialogueActive || battleActive || moviePrepared || !GameMode.CanSnapshot(state.Mode) || (state.Mode == GameMode.World && exploration?.Moving == true) || (state.Mode == GameMode.Dungeon && dungeon?.Motion.Moving == true))
        {
            return;
        }

        string reason = autosavePending;
        autosavePending = "";
        WriteAutosave(reason);
    }

    private void WriteAutosave(string reason)
    {
        if (loadingSnapshot || !GameMode.CanSnapshot(state.Mode))
        {
            return;
        }

        try
        {
            var image = CaptureSaveImage(state.Mode, true, reason);
            PersistenceStore.WriteAutomatic(image, controls.Profile.AutosaveLimit);
            autosaveFailure = "";
            Console.WriteLine($"Autosave committed: {reason}; mode={state.Mode}; scenario={activeScriptIdentity?.Member ?? "none"}.");
            autosavePending = "";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            Console.WriteLine("Autosave failed: " + e.Message);
            autosaveFailure = GameText.Get("ui.autosave.failed");
        }
    }

    private void DrawAutosaveFailure(Span<ushort> target)
    {
        if (autosaveFailure.Length == 0)
        {
            return;
        }

        for (int y = 453; y < 474; y++)
        {
            target.Slice(y * 640 + 176, 288).Clear();
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        smallFont.DrawText(autosaveFailure, target, 640, 480, 640, 184, 456, 0xffff);
    }

    private void BeforeScenarioCall(ScriptCall call)
    {
        questCompletionBeforeCall = state.Journal.CompletionRevision;
        if (call.Function is "battle_start2" or "battle_start3")
        {
            nextBattleSeed ??= ReadBattleSeed();
            WriteAutosave(AutosaveReason.BeforeBattle);
        }
    }

    private void AfterScenarioCall(ScriptCall call)
    {
        if (call.Function is "battle_start2" or "battle_start3")
        {
            WriteAutosave(AutosaveReason.AfterBattle);
        }

        if (state.Journal.CompletionRevision != questCompletionBeforeCall)
        {
            RequestAutosave(AutosaveReason.QuestComplete);
        }
    }

    private void ResumeSavedScenario()
    {
        var saved = resumeScript ?? throw new InvalidOperationException();
        resumeScript = null;
        RunScript(Encoding.ASCII.GetBytes(saved.Member), Encoding.ASCII.GetBytes(saved.Function), saved.Continuation, saved.ContinuationMode, saved);
        switch (saved.Continuation)
        {
            case 0:
                break;
            case 1:
                town.CompletePreEnter(state.EnterPermission != 0, state.Mode, HandleTown);
                break;
            case 2:
                town.CompletePreOut(state.EnterPermission != 0, HandleTown, state.Mode);
                break;
            case 3:
                actions.CloseAll();
                if (state.Mode is GameMode.Interior or GameMode.Inn or GameMode.Shop)
                {
                    InteriorActions.OpenAction();
                }

                break;
            case 4:
                CompleteLeaveInterior(saved.ContinuationMode);
                break;
            case 5:
                CompleteWorldPreEnter(saved.ContinuationMode != 0, scriptReturnLocation);
                break;
            default:
                throw new InvalidDataException("Unknown scenario return continuation.");
        }
    }
}
