using Atria.Audio;
using Atria.Game.Controls;
using Atria.Game.Dialogue;
using Atria.Game.Profiles;
using Atria.Game.Settings;
using Atria.Platform.Win32;
using Atria.Render;

namespace Atria.Game;

public sealed partial class GameSession
{
    internal static string VerifyProfileIntegration(string resourceRoot, string musicRoot, string fixture)
    {
        string output = Path.Combine(Path.GetFullPath(Path.Combine(resourceRoot, "..")), "audit", "profile-session", Guid.NewGuid().ToString("N"));
        var previous = GameText.Current;
        GameText.Use(new LocalizationCatalog(Path.Combine(AppContext.BaseDirectory, "assets", "localization")));
        Directory.CreateDirectory(Path.Combine(output, "saves"));
        File.Copy(fixture, Path.Combine(output, "saves", "atria000.sav"));
        using var window = new NativeWindow("Atria profile verification", 2560, 1440);
        using var presenter = new D3D11Presenter(window.Handle, 2560, 1440);
        using var audio = new AudioDevice();
        using var transport = new FileMusicTransport(audio, musicRoot, 0);
        using var faces = new FaceSpriteSource(Path.Combine(resourceRoot, "face.SPR"));
        var session = new GameSession(resourceRoot, window, presenter, audio, transport, faces, 60, true, null, output);
        try
        {
            var first = session.playerProfiles.Active;
            string save = session.PersistenceStore.GetSlotPath(0);
            Check(File.Exists(save), "real legacy save is migrated at startup");
            var configured = session.controls.Profile;
            configured.AutosaveLimit = 5;
            session.ApplyModernSettings(configured, new NativeSettingsState(1, 3, 1));
            session.ChangeLanguage("be-BY");
            string firstPreferences = File.ReadAllText(session.preferencesStore!.FilePath);
            var second = session.playerProfiles.Create("Святлана");
            session.SwitchPlayerProfile(second);
            Check(session.PersistenceStore.InspectLibrary(false).Length == 0, "new profile has no saves");
            Check(session.controls.Profile.AutosaveLimit == 3 && session.settings.Music == 2
                && GameText.Current.Language == "ru-RU", "new profile gets independent default settings");
            session.ApplyModernSettings(session.controls.Profile, new NativeSettingsState(0, 0, 1));
            session.SwitchPlayerProfile(first);
            Check(session.controls.Profile.AutosaveLimit == 5 && session.settings.Music == 1
                && session.settings.Effects == 3 && GameText.Current.Language == "be-BY", "return restores profile settings and language");
            Check(session.PersistenceStore.GetSlotPath(0) == save && File.ReadAllBytes(save).AsSpan().SequenceEqual(File.ReadAllBytes(fixture)),
                "return restores untouched save library");
            Check(File.ReadAllText(session.preferencesStore!.FilePath) == firstPreferences, "another profile cannot overwrite settings");

            foreach (string language in new[] { "ru-RU", "be-BY" })
            {
                GameText.Use(new LocalizationCatalog(GameText.Current.Root, language));
                var panel = new ControlPanel(session.etc, session.smallFont) { Selection = 1 };
                var rows = new List<ControlRow> { new(GameText.Get("ui.profiles.create"), "") };
                rows.AddRange(session.playerProfiles.Profiles.Select(p => new ControlRow(p.Name,
                    p.Id == first.Id ? GameText.Get("ui.profiles.current") : "")));
                panel.Draw(session.host.FrameBuffer, GameText.Get("ui.profiles.title"), rows, GameText.Get("ui.profiles.listHelp"));
                WriteProbeBitmap(Path.Combine(output, "profiles-" + language + ".bmp"), session.host.FrameBuffer);
                new ProfileNameEditor("Святлана").Draw(session.host.FrameBuffer, session.etc, session.smallFont);
                WriteProbeBitmap(Path.Combine(output, "name-" + language + ".bmp"), session.host.FrameBuffer);
                session.PresentMainMenu();
                window.Pump();
            }
            session.ChangeLanguage("be-BY");
            session.LoadSessionSlot(0);
            Check(!session.CanChangeProfile && session.settings.Music == 1 && session.settings.Effects == 3
                && GameText.Current.Language == "be-BY", "loading retains profile preferences and locks switching");
            session.state.Mode = GameMode.MainMenu;
            bool blocked = false;
            try { session.SwitchPlayerProfile(second); }
            catch (InvalidOperationException) { blocked = true; }
            Check(blocked && session.playerProfiles.ActiveId == first.Id, "returning to main menu cannot bypass the lock");
            return "Profile session PASS: isolated saves/preferences, language/audio/autosave switches, load settings, startup-only guard, D3D11 menu. Evidence: " + output;
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
            if (!value) throw new InvalidOperationException("Profile session: " + message);
        }
    }
}
