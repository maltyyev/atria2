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
        settings.Set(saved.Audio.Music, saved.Audio.Effects, saved.Audio.ThirdOption);
        initialControlProfile = saved.Controls;
        ControlBindings result = saved.LegacyBindings;
        configuredSettingsBindings = result;
        ApplySettingsAudio();
        return result;
    }

    private int CurrentEffectsVolume => muted ? -10000 : SettingsAudio.EffectsHundredthDecibels(settings.Effects);

    private void ApplySettingsAudio() => musicTransport.SetVolume(muted ? 0 : SettingsAudio.MusicGain(settings.Music));
    private void RestoreSettingsSnapshot(ReadOnlySpan<byte> bytes)
    {
        var value = NativeSettingsState.Parse(bytes);
        settings.Set(value.Music, value.Effects, value.ThirdOption);
        ApplySettingsAudio();
    }

    private void ApplyModernSettings(ControlProfile profile, NativeSettingsState audioSettings)
    {
        preferencesStore!.Write(new(profile, audioSettings, configuredSettingsBindings!));
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

    private ControlSettingsMenu CreateModernSettings() => new(controls, bindings, Moves, etc, smallFont, settings, ApplyModernSettings);
    private void EnterSettings()
    {
        if (optionBackground is null)
        {
            var b = BmpImage.Decode(Read("AT2BMP.000", "OPTION"u8));
            optionBackground = b.ToRgb565();
        }

        modernSettings = CreateModernSettings();
        controls.Clear();
        music.Update(musicId);
    }

    private void HandleSettingsInput()
    {
        modernSettings!.Update();
        if (!modernSettings.Closed)
        {
            return;
        }

        if (!modes.TryPop(out var t))
        {
            throw new InvalidOperationException("Options return stack empty.");
        }

        if (t.RestoreFlag)
        {
            actions.SetRenderEnabled(true);
        }

        EnterMode(t.Mode);
    }

    private void ComposeSettings(Span<ushort> target)
    {
        optionBackground!.CopyTo(target);
        modernSettings!.Draw(target);
    }
}
