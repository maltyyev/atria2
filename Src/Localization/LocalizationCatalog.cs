using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Atria.Localization;

public sealed class LocalizationCatalog
{
    public const string BaseLanguage = "ru-RU";
    private const int MaximumInterfaceTextCharacters = 1024;
    private const int MaximumLegacyRowTextBytes = 79;
    private const int MaximumParagraphCharacters = 16_384;
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> defaults = new(ReadDefaults);
    private readonly Dictionary<string, string> texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> legacyOverrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> nameOverrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> encodedTexts = new(StringComparer.Ordinal);
    public List<LocalizationIssue> Issues { get; } = [];

    public LocalizationCatalog(string? root = null, string language = BaseLanguage)
    {
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
                if (language != Path.GetFileName(language) || language is "." or ".." || language.Contains('/') || language.Contains('\\'))
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
                if (texts[entry.Key] != entry.Value && !entry.Key.StartsWith("ui.", StringComparison.Ordinal) && !TextResourceSchema.IsFlowingText(entry.Key))
                {
                    legacyOverrides.Add(entry.Key, GameText.Encoding.GetBytes(texts[entry.Key]));
                    if (!string.IsNullOrEmpty(entry.Value) && (entry.Key.StartsWith("names.", StringComparison.Ordinal) || IsStoredName(entry.Key)))
                    {
                        nameOverrides[entry.Value] = legacyOverrides[entry.Key];
                    }
                }
            }
        }
    }

    public string Get(string key) => texts.TryGetValue(key, out var value) ? value : throw new KeyNotFoundException($"Unknown localization key: {key}");
    public bool TryGet(string key, out string text) => texts.TryGetValue(key, out text!);
    public ReadOnlyMemory<byte> Legacy(string key, ReadOnlyMemory<byte> original) => legacyOverrides.TryGetValue(key, out var bytes) ? bytes : original;
    public ReadOnlyMemory<byte> Name(ReadOnlyMemory<byte> original)
        => FindName(original.Span) is { } replacement ? replacement : original;

    public ReadOnlySpan<byte> NameSpan(ReadOnlySpan<byte> original)
        => FindName(original) is { } replacement ? replacement : original;

    private byte[]? FindName(ReadOnlySpan<byte> original)
    {
        if (nameOverrides.Count == 0)
        {
            return null;
        }

        int end = original.IndexOf((byte)0);
        string name = TextResourceSchema.Normalize(GameText.Encoding.GetString(end < 0 ? original : original[..end]));
        return nameOverrides.GetValueOrDefault(name);
    }

    private static bool IsStoredName(string key)
    {
        // Saved names retain original bytes; only these message ranges supply name fields.
        return key.StartsWith("messages.", StringComparison.Ordinal) && int.TryParse(key.AsSpan("messages.".Length), out int index)
            && index is >= 0 and < 6 or >= 119 and <= 170 or >= 298 and <= 305 or 361;
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
        var languages = Directory.Exists(root) ? Directory.GetDirectories(root).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.Ordinal) : [];
        languages.Add(BaseLanguage);
        foreach (string language in languages.Order(StringComparer.Ordinal))
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
