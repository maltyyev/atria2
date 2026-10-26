using Atria.Audio;
using Atria.Game.Dialogue;
using Atria.Game.Persistence;
using Atria.Platform.Win32;
using Atria.Render;

namespace Atria.Game;

public sealed partial class GameSession
{
    // Integration harness owns a fresh output directory and only reads the supplied player save.
    internal static string VerifyLanguageIntegration(string resourceRoot, string musicRoot, string fixture)
    {
        string output = Path.Combine(Path.GetFullPath(Path.Combine(resourceRoot, "..")), "audit", "language-session", Guid.NewGuid().ToString("N"));
        var previous = GameText.Current;
        GameText.Use(new LocalizationCatalog(Path.Combine(AppContext.BaseDirectory, "assets", "localization")));
        using var window = new NativeWindow("Atria language verification", 2560, 1440);
        using var presenter = new D3D11Presenter(window.Handle, 2560, 1440);
        using var audio = new AudioDevice();
        using var transport = new FileMusicTransport(audio, musicRoot, 0);
        using var faces = new FaceSpriteSource(Path.Combine(resourceRoot, "face.SPR"));
        var session = new GameSession(resourceRoot, window, presenter, audio, transport, faces, 60, true, null, output);
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
            session.controls.Dispose();
            session.StopAudio();
            GameText.Use(previous);
        }

        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Language integration: " + message);
        }
    }
}
