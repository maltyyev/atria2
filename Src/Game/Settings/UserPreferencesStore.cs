using System.Text.Json;
using Atria.Game.Controls;
using Atria.Resources;

namespace Atria.Game.Settings;

internal sealed record UserPreferences(ControlProfile Controls, NativeSettingsState Audio, ControlBindings LegacyBindings, string Language = LocalizationCatalog.BaseLanguage);
/// <summary>One atomic document for audio and input; older files are read only for migration.</summary>
internal sealed class UserPreferencesStore(string directory, string originalRoot)
{
    public string FilePath => Path.Combine(directory, "preferences.json");

    public UserPreferences Read(ControlBindings originalBindings)
    {
        if (File.Exists(FilePath))
        {
            try
            {
                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > 262144)
                {
                    throw new InvalidDataException("Preferences exceed the supported size.");
                }

                using var json = JsonDocument.Parse(stream);
                var root = json.RootElement;
                if (root.GetProperty("version").GetInt32() != 1)
                {
                    throw new InvalidDataException("Unsupported preferences version.");
                }

                var controls = ControlProfileStore.Decode(System.Text.Encoding.UTF8.GetBytes(root.GetProperty("controls").GetRawText()));
                var settings = LocalSettingsCodec.Parse(root.GetProperty("legacySettings").GetBytesFromBase64());
                string? language = root.TryGetProperty("language", out var selectedLanguage) && selectedLanguage.ValueKind == JsonValueKind.String
                    ? selectedLanguage.GetString() : null;
                return new(controls, settings.Settings, settings.Bindings,
                    LocalizationCatalog.IsSupportedLanguage(language) ? language! : LocalizationCatalog.BaseLanguage);
            }
            catch (Exception error) when (IsReadError(error))
            {
                Console.WriteLine("Invalid preferences; restoring legacy/default settings: " + error.Message);
            }
        }

        var legacy = new LocalSettingsStore(directory, originalRoot).Read();
        var bindings = legacy?.Bindings ?? originalBindings;
        var defaults = ControlProfile.Defaults(originalBindings);
        for (int player = 0; player < defaults.Players.Length; player++)
        {
            bool changed = Enumerable.Range(0, 8).Any(action => bindings[player, action] != originalBindings[player, action]);
            if (!changed)
            {
                continue;
            }

            for (int action = 0; action < 8; action++)
            {
                defaults.Players[player].Bindings[action][0] = InputBinding.Key(bindings[player, action]);
            }

            defaults.Players[player].Preset = "Custom";
            defaults.Players[player].Classic = true;
        }

        var profile = ControlProfileStore.Read(Path.Combine(directory, "controls.json"), defaults);
        return new(profile, legacy?.Settings ?? new NativeSettingsState(), bindings);
    }

    public void Write(UserPreferences preferences)
    {
        if (!LocalizationCatalog.IsSupportedLanguage(preferences.Language))
            throw new InvalidDataException("Unsupported interface language.");
        // Preserve the original-resource and redirected-directory protections used by legacy settings.
        new LocalSettingsStore(directory, originalRoot).CheckDirectory();
        Directory.CreateDirectory(directory);
        new LocalSettingsStore(directory, originalRoot).CheckDirectory();
        if (File.Exists(FilePath) && (File.GetAttributes(FilePath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Preferences file is redirected.");
        }

        byte[] controls = ControlProfileStore.Encode(preferences.Controls);
        _ = ControlProfileStore.Decode(controls);
        byte[] settings = LocalSettingsCodec.Encode(preferences.Audio, preferences.LegacyBindings);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new()
        {
            Indented = true
        }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteString("language", preferences.Language);
            writer.WritePropertyName("controls");
            writer.WriteRawValue(controls);
            writer.WriteBase64String("legacySettings", settings);
            writer.WriteEndObject();
        }

        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(buffer.ToArray());
                file.Flush(true);
            }

            File.Move(temporary, FilePath, true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool IsReadError(Exception error) => error is JsonException or InvalidDataException or IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentException;
}
