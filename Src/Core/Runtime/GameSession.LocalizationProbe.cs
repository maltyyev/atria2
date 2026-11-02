using Atria.Core.Contracts.Audio;
using Atria.Core.Resources.Audio;
using Atria.Core.Runtime.Audio;
using Atria.Core.Game.Dialogue;
using Atria.Core.Presentation.Dialogue;
using Atria.Core.Resources.Sprites;
using Atria.Core.Runtime.Input;
using Atria.Core.Runtime.Timing;
using Atria.Core.Persistence;
using Atria.Core.Presentation.Persistence;
using Atria.Core.Contracts.Presentation;
using Atria.Core.Presentation;
using Atria.Core.Persistence.Settings;
using Atria.Core.Resources.Localization;

namespace Atria.Core.Runtime;

public sealed partial class GameSession
{
    // Integration harness owns a fresh output directory and only reads the supplied player save.
    internal static string VerifyLanguageIntegration(string resourceRoot, string musicRoot, string fixture, GameSessionProbeHostFactory createHost)
    {
        string output = Path.Combine(Path.GetFullPath(Path.Combine(resourceRoot, "..")), "audit", "language-session", Guid.NewGuid().ToString("N"));
        var previous = GameText.Current;
        GameText.Use(new LocalizationCatalog(Path.Combine(AppContext.BaseDirectory, "assets", "localization")));
        using var probeHost = createHost(resourceRoot, musicRoot, output, 60, true);
        var session = probeHost.Session;
        var window = probeHost.Window;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(session.PersistenceStore.GetSlotPath(0))!);
            File.Copy(fixture, session.PersistenceStore.GetSlotPath(0));
            session.LoadSessionSlot(0);
            byte[] state = NativeGameStateCodec.Encode(session.state);
            byte[] context = session.CaptureSaveImage(session.state.Mode, true, "").Context.Payload.ToArray();
            foreach (string language in new[] { "be-BY", "ru-RU", "be-BY" })
            {
                session.ChangeLanguage(language);
                Check(NativeGameStateCodec.Encode(session.state).AsSpan().SequenceEqual(state), "language selection preserves loaded gameplay bytes");
                Check(session.CaptureSaveImage(session.state.Mode, true, "").Context.Payload.Span.SequenceEqual(context), "language selection preserves scenario/runtime context");
            }

            var preferences = session.preferencesStore!.Read(session.bindings);
            Check(preferences.Language == "be-BY", "session saves language in local preferences");
            session.LoadSessionSlot(0);
            Check(GameText.Current.Language == "be-BY", "loading a Russian-era save retains Belarusian preference");
            session.ComposeScene(session.state, session.host.FrameBuffer);
            session.host.Present();
            window.Pump();
            WriteProbeBitmap(Path.Combine(output, "loaded-world-be.bmp"), session.host.FrameBuffer);
            var settingsMenu = session.CreateModernSettings();
            settingsMenu.Draw(session.host.FrameBuffer);
            session.host.Present();
            window.Pump();
            WriteProbeBitmap(Path.Combine(output, "settings-be.bmp"), session.host.FrameBuffer);
            return "Native language integration: real save read-only, ru/be round trips, state/context byte equality, preferences, reload and D3D11 frames passed. Evidence: " + output;
        }
        finally
        {
            session.DisposeProgression();
            session.DisposeInputRouter();
            session.StopAudio();
            GameText.Use(previous);
        }

        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Language integration: " + message);
        }
    }
}
