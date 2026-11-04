using System.Reflection;
using System.Text;
using System.Text.Json;
using Atria.Core.Game.Progression;
using Atria.Core.Persistence.Settings;
using Atria.Core.Resources;

namespace Atria.Core.Resources.Localization;

public sealed class LocalizationCatalog
{
    public const string BaseLanguage = "ru-RU";
    public const string BelarusianLanguage = "be-BY";
    public string? Root { get; }
    public string Language { get; }
    public IReadOnlyList<string> AvailableLanguages { get; }
    private const int MaximumInterfaceTextCharacters = 1024;
    private const int MaximumLegacyRowTextBytes = 79;
    private const int MaximumParagraphCharacters = 16_384;
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> defaults = new(ReadDefaults);
    private readonly Dictionary<string, string> texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> legacyOverrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> encodedTexts = new(StringComparer.Ordinal);
    public List<LocalizationIssue> Issues { get; } = [];

    public LocalizationCatalog(string? root = null, string language = BaseLanguage)
    {
        Root = root;
        Language = language;
        AvailableLanguages = DiscoverLanguages(root);
        foreach (var file in defaults.Value.Values)
        {
            foreach (var entry in file)
            {
                texts.Add(entry.Key, entry.Value);
            }
        }

        if (root is not null)
        {
            ReadLanguage(root, BaseLanguage);
            if (language != BaseLanguage)
            {
                if (!IsSafeLanguageId(language))
                {
                    throw new ArgumentException("Language must be a directory name.", nameof(language));
                }

                ReadLanguage(root, language);
            }
        }

        foreach (var file in defaults.Value.Values)
        {
            foreach (var entry in file)
            {
                if (!entry.Key.StartsWith("ui.", StringComparison.Ordinal) && !TextResourceSchema.IsFlowingText(entry.Key))
                {
                    legacyOverrides.Add(entry.Key, GameText.Encoding.GetBytes(texts[entry.Key]));
                }
            }
        }
    }

    public string Get(string key) => texts.TryGetValue(key, out var value) ? value : throw new KeyNotFoundException($"Unknown localization key: {key}");
    public bool TryGet(string key, out string text) => texts.TryGetValue(key, out text!);
    public bool SupportsLanguage(string? language) => language is not null && AvailableLanguages.Contains(language, StringComparer.Ordinal);

    /// <summary>Returns the next installed locale in stable order, wrapping from the final locale.</summary>
    public string NextLanguage()
    {
        for (int i = 0; i < AvailableLanguages.Count; i++)
        {
            if (AvailableLanguages[i] == Language)
            {
                return AvailableLanguages[(i + 1) % AvailableLanguages.Count];
            }
        }

        return AvailableLanguages[0];
    }

    /// <summary>Checks that a locale identifier is a safe, single directory name.</summary>
    public static bool IsSafeLanguageId(string? language)
    {
        if (string.IsNullOrEmpty(language) || language.Length > 63) return false;
        foreach (char c in language)
            if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) return false;
        return IsAsciiLetterOrDigit(language[0]) && IsAsciiLetterOrDigit(language[^1]);
    }

    private static IReadOnlyList<string> DiscoverLanguages(string? root)
    {
        if (root is null)
        {
            return Array.AsReadOnly([BaseLanguage]);
        }

        try
        {
            return ScanLanguages(Path.GetFullPath(root));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Array.AsReadOnly([BaseLanguage]);
        }
    }

    private static IReadOnlyList<string> ScanLanguages(string root)
    {
        var extras = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (string path in Directory.EnumerateDirectories(root))
            {
                string? id = Path.GetFileName(path);
                if (!IsSafeLanguageId(id) || id == BaseLanguage)
                {
                    continue;
                }

                try
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    string uiFile = Path.Combine(path, "ui.json");
                    if (File.Exists(uiFile) && (File.GetAttributes(uiFile) & FileAttributes.ReparsePoint) == 0)
                    {
                        extras.Add(id);
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }

        string[] languages = new string[extras.Count + 1];
        languages[0] = BaseLanguage;
        extras.CopyTo(languages, 1);
        return Array.AsReadOnly(languages);
    }

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    public ReadOnlyMemory<byte> Legacy(string key, ReadOnlyMemory<byte> original)
    {
        if (!legacyOverrides.TryGetValue(key, out var bytes)
            || TextResourceSchema.Normalize(GameText.Decode(original.Span)) == texts[key])
        {
            return original;
        }

        return bytes;
    }
    public byte[] Bytes(string key)
    {
        if (!encodedTexts.TryGetValue(key, out var bytes))
        {
            encodedTexts.Add(key, bytes = GameText.Encoding.GetBytes(Get(key)));
        }

        return bytes;
    }
    public static IReadOnlyDictionary<string, Dictionary<string, string>> Defaults => defaults.Value;

    public static IReadOnlyList<LocalizationIssue> Validate(string root)
    {
        var issues = new List<LocalizationIssue>();
        foreach (string language in DiscoverLanguages(root))
        {
            issues.AddRange(new LocalizationCatalog(root, language).Issues.Where(i => language == BaseLanguage || !i.File.StartsWith(BaseLanguage + "/", StringComparison.Ordinal)));
        }

        return issues;
    }

    private void ReadLanguage(string root, string language)
    {
        // Validate diagnostic templates first so later errors can use their safe translations.
        foreach (var file in defaults.Value.OrderBy(file => file.Key == "validation.json" ? 0 : 1))
        {
            string relative = language + "/" + file.Key;
            string path = Path.Combine(root, language, file.Key);
            Dictionary<string, string> translated;
            byte[]? input = null;
            try
            {
                input = File.ReadAllBytes(path);
                if (input.AsSpan().StartsWith(Encoding.UTF8.Preamble))
                {
                    input = input[Encoding.UTF8.Preamble.Length..];
                }
                using var document = JsonDocument.Parse(input);
                translated = TextResourceSchema.Upgrade(ReadObject(document.RootElement));
            }
            catch (JsonException e)
            {
                Issues.Add(new(relative, null, Diagnostic("json", ("detail", e.Message)), Line: e.LineNumber + 1, Column: JsonColumn(input, e)));
                continue;
            }
            catch (LocalizationDataException e)
            {
                Issues.Add(new(relative, e.Key, Diagnostic(e.Code, ("key", e.Key))));
                continue;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Issues.Add(new(relative, null, Diagnostic("file", ("detail", e.Message)), IsError: language == BaseLanguage || e is not FileNotFoundException));
                continue;
            }

            foreach (string unknown in translated.Keys.Except(file.Value.Keys, StringComparer.Ordinal))
            {
                Issues.Add(new(relative, unknown, Diagnostic("unknownKey")));
            }

            foreach (var entry in file.Value)
            {
                if (!translated.TryGetValue(entry.Key, out string? value))
                {
                    Issues.Add(new(relative, entry.Key, Diagnostic("missing"), IsError: false));
                    continue;
                }

                string? error = ValidateValue(entry.Key, entry.Value, value);
                if (error is not null)
                {
                    Issues.Add(new(relative, entry.Key, error));
                    continue;
                }

                texts[entry.Key] = value;
            }
        }

        string folder = Path.Combine(root, language);
        if (Directory.Exists(folder))
        {
            foreach (string path in Directory.GetFiles(folder, "*.json"))
            {
                if (!defaults.Value.ContainsKey(Path.GetFileName(path)))
                {
                    Issues.Add(new(language + "/" + Path.GetFileName(path), null, Diagnostic("unknownFile")));
                }
            }
        }
    }

    private string? ValidateValue(string key, string baseline, string value)
    {
        if (value == baseline)
        {
            return null;
        }

        try
        {
            if (key.StartsWith("ui.", StringComparison.Ordinal) && !TextTemplate.Parameters(baseline).SetEquals(TextTemplate.Parameters(value)))
            {
                var expected = TextTemplate.Parameters(baseline);
                var actual = TextTemplate.Parameters(value);
                return Diagnostic("parameters", ("missing", string.Join(", ", expected.Except(actual))), ("unknown", string.Join(", ", actual.Except(expected))));
            }
        }
        catch (FormatException e)
        {
            return Diagnostic("template", ("detail", e.Message));
        }

        if (value.Contains('\0') || (!TextResourceSchema.IsFlowingText(key) && !baseline.Contains('\n') && (value.Contains('\n') || value.Contains('\r'))))
        {
            return Diagnostic("lines");
        }

        try
        {
            // shortcut: legacy tables and four UI labels still use bytes; remove this gate after their string migration.
            bool unicode = TextResourceSchema.IsFlowingText(key) || key.StartsWith("ui.", StringComparison.Ordinal) && key is not ("ui.quests.closed" or "ui.quests.active" or "ui.units.gold" or "ui.units.torch");
            _ = new UTF8Encoding(false, true).GetByteCount(value);
            int length = unicode ? value.Length : GameText.Encoding.GetByteCount(value);
            int limit = key.StartsWith("ui.", StringComparison.Ordinal) ? MaximumInterfaceTextCharacters : Math.Max(baseline.Length, MaximumLegacyRowTextBytes);
            if (TextResourceSchema.IsFlowingText(key))
            {
                limit = MaximumParagraphCharacters;
            }

            if (length > limit)
            {
                return Diagnostic("length", ("limit", limit));
            }
        }
        catch (EncoderFallbackException)
        {
            return Diagnostic("font");
        }

        return null;
    }

    private static Dictionary<string, Dictionary<string, string>> ReadDefaults()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        Assembly assembly = typeof(LocalizationCatalog).Assembly;
        const string prefix = "Atria.Localization.Defaults.";
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var document = JsonDocument.Parse(stream);
            result.Add(name[prefix.Length..], ReadObject(document.RootElement));
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("Built-in localization fallback is missing.");
        }

        return result;
    }

    internal static Dictionary<string, string> ReadObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new LocalizationDataException("object", null);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new LocalizationDataException("value", property.Name);
            }

            if (!result.TryAdd(property.Name, property.Value.GetString()!))
            {
                throw new LocalizationDataException("duplicate", property.Name);
            }
        }

        return result;
    }
    private string Diagnostic(string code, params (string Name, object? Value)[] arguments) =>
        TextTemplate.Format(Get("ui.validation." + code), arguments);

    private sealed class LocalizationDataException(string code, string? key) : Exception(code)
    {
        public string Code { get; } = code;
        public string? Key { get; } = key;
    }

    private static long? JsonColumn(byte[]? input, JsonException error)
    {
        if (input is null || error.LineNumber is null || error.BytePositionInLine is null)
        {
            return null;
        }

        int start = 0;
        for (long line = 0; line < error.LineNumber; line++)
        {
            int newline = Array.IndexOf(input, (byte)'\n', start);
            if (newline < 0)
            {
                return null;
            }
            start = newline + 1;
        }

        int count = (int)Math.Min(error.BytePositionInLine.Value, input.Length - start);
        return Encoding.UTF8.GetCharCount(input, start, count) + 1;
    }

}
