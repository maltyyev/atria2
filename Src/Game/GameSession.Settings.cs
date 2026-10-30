using Atria.Game.Navigation;
using Atria.Game.Controls;
using Atria.Game.Settings;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    private readonly NativeSettingsState settings = new();
    private UserPreferencesStore? preferencesStore;
    private ControlProfile? initialControlProfile;
    private ControlBindings? configuredSettingsBindings;
    private ushort[]? optionBackground;
    private ControlSettingsMenu? modernSettings;
    private ControlBindings InitializeSettings(string outputDirectory, ControlBindings originalBindings)
    {
        preferencesStore = new(outputDirectory, root);
        var saved = preferencesStore.Read(originalBindings);
        GameText.Use(new LocalizationCatalog(GameText.Current.Root, saved.Language));
        settings.Set(saved.Audio.Music, saved.Audio.Effects, saved.Audio.ThirdOption);
        initialControlProfile = saved.Controls;
        ControlBindings result = saved.LegacyBindings;
        configuredSettingsBindings = result;
        ApplySettingsAudio();
        return result;
    }

    private int CurrentEffectsVolume => muted ? -10000 : SettingsAudio.EffectsHundredthDecibels(settings.Effects);

    private void ApplySettingsAudio() => musicTransport.SetVolume(muted ? 0 : SettingsAudio.MusicGain(settings.Music));
    private void ApplyModernSettings(ControlProfile profile, NativeSettingsState audioSettings)
    {
        preferencesStore!.Write(new(profile, audioSettings, configuredSettingsBindings!, GameText.Current.Language));
        controls.Profile = profile;
        controls.StopVibration();
        controls.Clear();
        try
        {
            PersistenceStore.PruneAutomatic(profile.AutosaveLimit);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("Autosave rotation deferred: " + e.Message);
        }

        settings.Set(audioSettings.Music, audioSettings.Effects, audioSettings.ThirdOption);
        ApplySettingsAudio();
    }

    private void ChangeLanguage(string language)
    {
        var catalog = new LocalizationCatalog(GameText.Current.Root, language);
        if (catalog.Issues.Any(issue => issue.IsError))
            throw new InvalidDataException("The selected translation contains invalid resources.");

        // Persist first: a failed write must not leave the running language different from the saved choice.
        preferencesStore!.Write(new(controls.Profile, settings, configuredSettingsBindings!, language));
        GameText.Use(catalog);
        controls.Notice = "";
    }

    private ControlSettingsMenu CreateModernSettings() => new(controls, bindings, Moves, etc, smallFont, settings, ApplyModernSettings, ChangeLanguage);
    private void EnterSettings()
    {
        if (optionBackground is null)
        {
            var b = BmpImage.Decode(Read("AT2BMP.000", "OPTION"u8));
            optionBackground = b.ToRgb565();
        }

        modernSettings = CreateModernSettings();
        controls.Clear();
    }

    private void HandleSettingsInput()
    {
        modernSettings!.Update();
        if (!modernSettings.Closed)
        {
            return;
        }

        if (!CloseScreen())
        {
            throw new InvalidOperationException("Options return stack empty.");
        }

    }

    private void ComposeSettings(Span<ushort> target)
    {
        optionBackground!.CopyTo(target);
        modernSettings!.Draw(target);
    }
}
