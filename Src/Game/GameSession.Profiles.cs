using Atria.Game.Controls;
using Atria.Game.Profiles;
using Atria.Game.Settings;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    private readonly PlayerProfileStore playerProfiles;
    private readonly ControlBindings defaultBindings;
    private bool hasStartedGame;
    private string ActiveProfileDirectory => playerProfiles.DirectoryFor(playerProfiles.Active);
    private bool CanChangeProfile => !hasStartedGame && state.Mode == GameMode.MainMenu && !menu.Confirmation;
    private static string DefaultProfileName(long number) => GameText.Format("ui.profiles.defaultName", ("number", number));

    private void SwitchPlayerProfile(PlayerProfile profile)
    {
        if (!CanChangeProfile) throw new InvalidOperationException("Cannot switch player profile during a game.");
        if (profile.Id == playerProfiles.ActiveId) return;
        var store = new UserPreferencesStore(Path.Combine(playerProfiles.DirectoryFor(profile), "config"), root);
        var saved = store.Read(defaultBindings);
        var catalog = new LocalizationCatalog(GameText.Current.Root, saved.Language);
        if (catalog.Issues.Any(issue => issue.IsError))
            throw new InvalidDataException("The profile translation contains invalid resources.");

        playerProfiles.Select(profile.Id);
        preferencesStore = store;
        configuredSettingsBindings = saved.LegacyBindings;
        bindings.CopyFrom(saved.LegacyBindings);
        controls.StopVibration();
        controls.Profile = saved.Controls;
        controls.Clear();
        GameText.Use(catalog);
        settings.Set(saved.Audio.Music, saved.Audio.Effects, saved.Audio.ThirdOption);
        ApplySettingsAudio();
        modernSettings = null;
        persistenceStore = null;
        persistenceSelection = 0;
        persistenceSlots = [];
        persistenceMetadata = [];
        persistenceLegacyTemplate = null;
        controls.Notice = "";
        menu.Enter();
    }

    private void ShowPlayerProfiles()
    {
        if (!CanChangeProfile) return;
        blockingControls = true;
        controls.Clear();
        var panel = new ControlPanel(etc, smallFont);
        panel.Selection = playerProfiles.Profiles.ToList().FindIndex(p => p.Id == playerProfiles.ActiveId) + 1;
        ushort[] background = host.FrameBuffer.ToArray();
        bool done = false;
        string failure = "";
        try
        {
            RunControlModal(() => done, () =>
            {
                background.CopyTo(host.FrameBuffer);
                var rows = new List<ControlRow>
                {
                    new(GameText.Get("ui.profiles.create"), "", () =>
                    {
                        string? name = EditPlayerProfileName(playerProfiles.NextName(DefaultProfileName), background);
                        if (name is null) return;
                        SwitchPlayerProfile(playerProfiles.Create(name));
                        done = true;
                    })
                };
                foreach (var profile in playerProfiles.Profiles)
                {
                    rows.Add(new(profile.Name, profile.Id == playerProfiles.ActiveId ? GameText.Get("ui.profiles.current") : "",
                        () => { SwitchPlayerProfile(profile); done = true; }));
                }
                try
                {
                    if ((controls.Take(0x3d) || controls.PadButtonPressed(2)) && panel.Selection > 0)
                    {
                        var selected = playerProfiles.Profiles[panel.Selection - 1];
                        string? name = EditPlayerProfileName(selected.Name, background);
                        if (name is not null) playerProfiles.Rename(selected.Id, name);
                    }
                    else if (panel.Input(controls, rows)) done = true;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    Console.WriteLine("Player profile operation failed: " + error);
                    failure = GameText.Get("ui.profiles.failed");
                }
                panel.Draw(host.FrameBuffer, GameText.Get("ui.profiles.title"), rows, GameText.Get("ui.profiles.listHelp"), failure);
            });
        }
        finally
        {
            controls.Clear();
            controls.FocusPauseRequested = false;
            blockingControls = false;
            gate.Resume();
        }
    }

    private string? EditPlayerProfileName(string name, ushort[] background)
    {
        var editor = new ProfileNameEditor(name);
        controls.Clear();
        window.TakeTextInput();
        RunControlModal(() => editor.Closed, () =>
        {
            editor.Update(controls, window.TakeTextInput());
            background.CopyTo(host.FrameBuffer);
            editor.Draw(host.FrameBuffer, etc, smallFont);
        });
        controls.Clear();
        return editor.Result;
    }
}
