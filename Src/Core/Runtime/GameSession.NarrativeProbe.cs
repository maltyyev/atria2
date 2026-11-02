using Atria.Core.Contracts.Audio;
using Atria.Core.Resources.Audio;
using Atria.Core.Runtime.Audio;
using Atria.Core.Game.Dialogue;
using Atria.Core.Presentation.Dialogue;
using Atria.Core.Resources.Sprites;
using Atria.Core.Runtime.Input;
using Atria.Core.Runtime.Timing;
using Atria.Core.Game.Exploration;
using Atria.Core.Presentation.Exploration;
using Atria.Core.Persistence;
using Atria.Core.Presentation.Persistence;
using Atria.Core.Game.State;
using Atria.Core.Contracts.Presentation;
using Atria.Core.Presentation;
using Atria.Core.Resources;

namespace Atria.Core.Runtime;

public sealed partial class GameSession
{
    internal static string VerifyNarrativeArrival(string root, string musicRoot, string fixture, GameSessionProbeHostFactory createHost)
    {
        string output = Path.GetFullPath(Path.Combine(root, "..", "audit", "narrative-arrival", Guid.NewGuid().ToString("N")));
        using var probeHost = createHost(root, musicRoot, output, 60, true);
        var session = probeHost.Session;
        var window = probeHost.Window;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(session.PersistenceStore.GetSlotPath(0))!);
            File.Copy(fixture, session.PersistenceStore.GetSlotPath(0));
            session.LoadSessionSlot(0);
            int legacyWorld = session.state.WorldIndex;
            var map = session.map!;
            session.state.Journal.RestoreTracking(1020);
            var marker = new QuestMapIndex(root).Marker(session.state, map.AreaIndex);
            Check(marker == new QuestMapPoint(6, 4, 7), "actual save marker is the storehouse");
            Check(session.PendingWorldNarrative() is null, "no scene at the city exit");
            Console.WriteLine($"Loaded save: mapArea={map.AreaIndex}, legacyWorldIndex={legacyWorld}, tile={session.state.WorldX},{session.state.WorldY}.");
            uint clock = 0;
            foreach (var direction in new[] { WorldDirection.Up, WorldDirection.Right })
            {
                Check(session.exploration!.BeginTravel(direction) == TravelStartResult.Started, "actual route is traversable");
                Check(session.PendingWorldNarrative() is null, "no scene while moving");
                for (int frame = 0; session.exploration.Moving && frame < 2000; frame++)
                {
                    clock += 40;
                    var step = session.exploration.AdvanceAnimation(() => clock);
                    if (step.Effect is { } effect) session.HandleExplorationEffect(effect);
                }
                Check(!session.exploration.Moving, "movement completed");
            }
            Check(session.state.WorldX == 4 && session.state.WorldY == 7, "arrived at marker");
            Check(session.state.WorldIndex == legacyWorld, "legacy field preserved");
            Check(session.PendingWorldNarrative()?.Id == "NAR0002", "real session dispatcher must trigger storehouse arrival");
            Check(NarrativeScenes.After(session.state, "WORLD", "arrival", 0) is null, "same coordinates in a different area do not trigger");
            Check(NarrativeScenes.After(session.state, "WORLD", "arrival") is null, "missing map identity cannot trigger");
            session.state.ScriptActive = 1;
            Check(session.PendingWorldNarrative() is null, "queued original scenario retains priority");
            session.state.ScriptActive = 0;
            session.battleActive = true;
            Check(session.PendingWorldNarrative() is null, "active battle retains priority");
            session.battleActive = false;
            session.state.EndingResetFlag = 1;
            Check(session.PendingWorldNarrative() is null, "queued battle retains priority before it starts");
            session.state.EndingResetFlag = 0;
            var checkpoint = session.CaptureSaveImage(session.state.Mode, true, "");
            session.PersistenceStore.WriteNative(1, checkpoint);
            session.LoadSessionSlot(1);
            Check(session.PendingWorldNarrative()?.Id == "NAR0002", "saved arrival still triggers without another step");
            session.state.NarrativeHistory.Add("NAR0002");
            Check(session.PendingWorldNarrative() is null, "completed scene does not repeat");
            session.ComposeScene(session.state, session.host.FrameBuffer);
            session.host.Present();
            window.Pump();
            return "Actual save arrival PASS: real movement, session dispatch, reload and once-only guard. Evidence: " + output;
        }
        finally
        {
            session.DisposeProgression();
            session.DisposeInputRouter();
            session.StopAudio();
        }

        static void Check(bool value, string reason)
        {
            if (!value) throw new InvalidOperationException("Narrative arrival: " + reason);
        }
    }
}
