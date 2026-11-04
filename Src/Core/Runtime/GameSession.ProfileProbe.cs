using Atria.Core.Contracts.Audio;
using Atria.Core.Contracts.Input;
using Atria.Core.Contracts.Presentation;
using Atria.Core.Contracts.Window;
using Atria.Core.Game;
using Atria.Core.Game.Dialogue;
using Atria.Core.Game.Settings;
using Atria.Core.Persistence.Controls;
using Atria.Core.Persistence.Profiles;
using Atria.Core.Persistence.Settings;
using Atria.Core.Presentation;
using Atria.Core.Presentation.Controls;
using Atria.Core.Presentation.Dialogue;
using Atria.Core.Presentation.Profiles;
using Atria.Core.Presentation.Scenes;
using Atria.Core.Presentation.Settings;
using Atria.Core.Resources;
using Atria.Core.Resources.Audio;
using Atria.Core.Resources.Localization;
using Atria.Core.Resources.Sprites;
using Atria.Core.Runtime.Audio;
using Atria.Core.Runtime.Input;
using Atria.Core.Runtime.Timing;

namespace Atria.Core.Runtime;

public sealed partial class GameSession
{
    /// <summary>Checks profile isolation, save compatibility, settings, localization, and live main-menu input hints.</summary>
    internal static string VerifyProfileIntegration(string resourceRoot, string musicRoot, string fixture, GameSessionProbeHostFactory createHost)
    {
        string output = Path.Combine(Path.GetFullPath(Path.Combine(resourceRoot, "..")), "audit", "profile-session", Guid.NewGuid().ToString("N"));
        var previous = GameText.Current;
        GameText.Use(new LocalizationCatalog(Path.Combine(AppContext.BaseDirectory, "assets", "localization")));
        Directory.CreateDirectory(Path.Combine(output, "saves"));
        File.Copy(fixture, Path.Combine(output, "saves", "atria000.sav"));
        using var probeHost = createHost(resourceRoot, musicRoot, output, 60, true);
        var session = probeHost.Session;
        var window = probeHost.Window;
        try
        {
            VerifyMainMenuHintIntegration(session);
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
            try
            {
                session.DisposeProgression();
            }
            finally
            {
                try
                {
                    var detachedRouter = session.controls;
                    session.inputHintsDirty = false;
                    session.DisposeInputRouter();
                    if (detachedRouter is not null)
                    {
                        var finalWindow = new HintProbeWindow();
                        finalWindow.ScanKeys[38] = true;
                        detachedRouter.Poll(finalWindow);
                        if (session.inputHintsDirty)
                            throw new InvalidOperationException("Profile session: terminated input router still invalidated menu hints.");
                    }
                }
                finally
                {
                    try
                    {
                        session.StopAudio();
                    }
                    finally
                    {
                        GameText.Use(previous);
                    }
                }
            }
        }

        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Profile session: " + message);
        }
    }

    /// <summary>Checks cached profile-hint pixels as input sources and router ownership change.</summary>
    private static void VerifyMainMenuHintIntegration(GameSession session)
    {
        var pads = new HintProbePads();
        pads.Samples[0] = new(true, 0, 0, 0, 0, 0, 0, 0);
        session.ReplaceInputRouter(new(session.controls.Profile, session.bindings, pads));
        var router = session.controls;
        var window = new HintProbeWindow();

        var expectedView = new MainMenuView(session.hostServices.InterfaceRoot);
        void PresentAndVerify(string source)
        {
            if (!session.PresentMainMenu())
                throw new InvalidOperationException($"Profile session: main menu rejected the {source} hint frame.");
            if (session.inputHintsDirty)
                throw new InvalidOperationException($"Profile session: presenting the {source} hint did not clear its invalidation.");
            var view = session.mainMenuView ?? throw new InvalidOperationException("Profile session: main menu view was not created.");
            string profile = CurrentProfileHint(session);
            var actual = view.Render(session.menu.Selection, session.saveDisabled, session.menu.Confirmation,
                session.menu.YesSelected, false, profile).ToArray();
            var expected = expectedView.Render(session.menu.Selection, session.saveDisabled,
                session.menu.Confirmation, session.menu.YesSelected, false, profile).ToArray();
            if (!actual.AsSpan().SequenceEqual(expected))
                throw new InvalidOperationException($"Profile session: cached pixels do not match the {source} profile hint.");
        }

        router.Poll(window);
        PresentAndVerify("initial keyboard hint");
        var view = session.mainMenuView!;
        var keyboardPixels = view.Render(session.menu.Selection, session.saveDisabled, session.menu.Confirmation,
            session.menu.YesSelected, false, CurrentProfileHint(session)).ToArray();

        pads.Samples[0] = pads.Samples[0] with { Buttons = (ushort)GamepadButtons.X };
        router.Poll(window);
        if (router.CurrentInputSource.Kind != InputSourceKind.Gamepad || !session.inputHintsDirty)
            throw new InvalidOperationException("Profile session: a pad press did not select its input source once.");
        PresentAndVerify("gamepad hint");
        var padPixels = view.Render(session.menu.Selection, session.saveDisabled, session.menu.Confirmation,
            session.menu.YesSelected, false, CurrentProfileHint(session)).ToArray();
        if (keyboardPixels.AsSpan().SequenceEqual(padPixels))
            throw new InvalidOperationException("Profile session: keyboard and gamepad hint bindings rendered identically.");

        pads.Samples[0] = pads.Samples[0] with { Buttons = 0 };
        router.Poll(window);
        window.ScanKeys[36] = true;
        router.Poll(window);
        if (router.CurrentInputSource.Kind != InputSourceKind.Keyboard)
            throw new InvalidOperationException("Profile session: keyboard activity did not restore the keyboard hint.");
        if (!session.inputHintsDirty)
            throw new InvalidOperationException("Profile session: keyboard source change did not invalidate its cached hint strip.");
        PresentAndVerify("keyboard hint");
        keyboardPixels = view.Render(session.menu.Selection, session.saveDisabled, session.menu.Confirmation,
            session.menu.YesSelected, false, CurrentProfileHint(session)).ToArray();

        window.ScanKeys[36] = false;
        router.Poll(window);
        window.MouseX++;
        router.Poll(window);
        if (router.CurrentInputSource.Kind != InputSourceKind.Mouse)
            throw new InvalidOperationException("Profile session: mouse movement did not select its input source once.");
        if (!session.inputHintsDirty)
            throw new InvalidOperationException("Profile session: mouse source change did not invalidate its cached hint strip.");
        PresentAndVerify("mouse hint");
        var mousePixels = view.Render(session.menu.Selection, session.saveDisabled, session.menu.Confirmation,
            session.menu.YesSelected, false, CurrentProfileHint(session)).ToArray();
        if (!keyboardPixels.AsSpan().SequenceEqual(mousePixels))
            throw new InvalidOperationException("Profile session: keyboard and mouse should share the configured fallback hint.");

        window.LastInputActivity = new(1, InputSourceKind.Touch, 7);
        router.Poll(window);
        if (router.CurrentInputSource is not { Kind: InputSourceKind.Touch, Identity: 7 })
            throw new InvalidOperationException("Profile session: touch metadata did not select its source once.");
        if (!session.inputHintsDirty)
            throw new InvalidOperationException("Profile session: touch source change did not invalidate its cached hint strip.");
        PresentAndVerify("touch hint");
        var touchPixels = view.Render(session.menu.Selection, session.saveDisabled, session.menu.Confirmation,
            session.menu.YesSelected, false, CurrentProfileHint(session)).ToArray();
        if (touchPixels.AsSpan().SequenceEqual(padPixels))
            throw new InvalidOperationException("Profile session: touch and gamepad hints should identify their own controls.");

        router.Poll(window);
        if (session.inputHintsDirty)
            throw new InvalidOperationException("Profile session: a held input emitted an extra source-change event.");
        PresentAndVerify("held touch hint");

        // Replacing a router must attach the new source while detaching the old one.
        var previousRouter = router;
        var replacementPads = new HintProbePads();
        replacementPads.Samples[0] = new(true, 0, 0, 0, 0, 0, 0, 0);
        session.ReplaceInputRouter(new(session.controls.Profile, session.bindings, replacementPads));
        router = session.controls;
        if (!session.inputHintsDirty)
            throw new InvalidOperationException("Profile session: replacing the input router did not invalidate menu hints.");
        PresentAndVerify("replaced router keyboard hint");
        var replacementWindow = new HintProbeWindow();
        replacementWindow.ScanKeys[session.bindings.Down] = true;
        router.Poll(replacementWindow);
        if (!session.menuNavigation.Next())
            throw new InvalidOperationException("Profile session: menu navigation retained the replaced input router.");
        replacementWindow.ScanKeys[session.bindings.Down] = false;
        router.Poll(replacementWindow);
        replacementWindow.LastInputActivity = new(1, InputSourceKind.Touch, 7);
        router.Poll(replacementWindow);
        if (!session.inputHintsDirty)
            throw new InvalidOperationException("Profile session: replacement router did not subscribe to source changes.");
        PresentAndVerify("replacement touch hint");

        var oldWindow = new HintProbeWindow();
        oldWindow.ScanKeys[38] = true;
        previousRouter.Poll(oldWindow);
        if (session.inputHintsDirty)
            throw new InvalidOperationException("Profile session: replaced router still invalidated the main menu.");
        PresentAndVerify("replacement remains active");
    }

    /// <summary>Formats the active profile's localized main-menu hint for the current input route.</summary>
    private static string CurrentProfileHint(GameSession session) => GameText.Format(
        session.CanChangeProfile ? "ui.profiles.menuHintDynamic" : "ui.profiles.menuCurrent",
        ("name", session.playerProfiles.Active.Name), ("button", session.controls.ProfileMenuHint));

    /// <summary>Supplies deterministic physical pad samples for the main-menu hint probe.</summary>
    private sealed class HintProbePads : IGamepadDevice
    {
        public PadSample[] Samples { get; } = new PadSample[4];
        public PadSample Read(int slot) => Samples[slot];
        public void Vibrate(int slot, ushort left, ushort right) { }
    }

    /// <summary>Provides controllable keyboard, pointer, and touch-source metadata for the profile probe.</summary>
    private sealed class HintProbeWindow : IGameWindow, IInputActivitySource
    {
        public bool Closed { get; set; }
        public bool Focused { get; set; } = true;
        public (int Width, int Height) ClientSize { get; set; } = (640, 480);
        public bool[] ScanKeys { get; } = new bool[256];
        public bool[] ScanPressed { get; } = new bool[256];
        public int MouseX { get; set; } = 100;
        public int MouseY { get; set; } = 100;
        public bool LeftButton { get; set; }
        public bool RightButton { get; set; }
        public bool MiddleButton { get; set; }
        public bool[] MousePressed { get; } = new bool[3];
        public int MouseWheelDelta { get; set; }
        public uint InputClockMilliseconds => 0;
        public InputActivity LastInputActivity { get; set; }
        public void Pump() { }
        public bool TryDequeueScanInput(out TimestampedScanInput input) { input = default; return false; }
        public string TakeTextInput() => "";
        public void RequestClose() => Closed = true;
    }
}
