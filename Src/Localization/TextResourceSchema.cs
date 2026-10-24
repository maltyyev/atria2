using System.Text.Json;
using System.Text.RegularExpressions;

namespace Atria.Localization;

public static class TextResourceSchema
{
    private static readonly HashSet<int> questionRecords;
    private static readonly HashSet<string> scriptQuestions;
    private static readonly Dictionary<string, string> scrollKeys;
    private static readonly Regex whitespace = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    static TextResourceSchema()
    {
        using var stream = typeof(TextResourceSchema).Assembly.GetManifestResourceStream("Atria.Content.Atria2.TextLayout.json")
            ?? throw new InvalidDataException("Missing text layout definitions.");
        using var document = JsonDocument.Parse(stream);
        questionRecords = document.RootElement.GetProperty("questionDialogueRecords").EnumerateArray().Select(v => v.GetInt32()).ToHashSet();
        scriptQuestions = document.RootElement.GetProperty("questionScriptCalls").EnumerateArray().Select(v => v.GetString()!).ToHashSet(StringComparer.Ordinal);
        scrollKeys = new(StringComparer.Ordinal);
        foreach (var block in document.RootElement.GetProperty("scrollBlocks").EnumerateObject())
        {
            foreach (var key in block.Value.EnumerateArray())
            {
                scrollKeys.Add(key.GetString()!, block.Name);
            }
        }
    }

    public static string Normalize(string text) => whitespace.Replace(text, " ").Trim();

    public static bool IsFlowingText(string key) => key.EndsWith(".text", StringComparison.Ordinal)
        || key.EndsWith(".description", StringComparison.Ordinal) || key.EndsWith(".introduction", StringComparison.Ordinal)
        || key.Contains(".choice.", StringComparison.Ordinal)
        || (key.StartsWith("scripts.", StringComparison.Ordinal) && scriptQuestions.Contains(key[..key.LastIndexOf('.')]));

    public static (string Key, bool Merge) UpgradeKey(string key)
    {
        int last = key.LastIndexOf('.');
        if (last < 0 || !int.TryParse(key.AsSpan(last + 1), out _))
        {
            return (key, false);
        }
        string group = key[..last];
        if (scrollKeys.TryGetValue(key, out var blockKey))
        {
            return (blockKey, true);
        }
        if (key.StartsWith("dialogue.", StringComparison.Ordinal) && int.TryParse(group.AsSpan("dialogue.".Length), out int record))
        {
            return questionRecords.Contains(record) ? (group + ".choice" + key[last..], false) : (group + ".text", true);
        }
        if (key.StartsWith("scripts.", StringComparison.Ordinal))
        {
            return scriptQuestions.Contains(group) ? (key, false) : (group + ".text", true);
        }
        if (group.EndsWith(".description", StringComparison.Ordinal) || group.EndsWith(".introduction", StringComparison.Ordinal))
        {
            return (group, true);
        }
        return (key, false);
    }

    public static Dictionary<string, string> Upgrade(IReadOnlyDictionary<string, string> original)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in original.OrderBy(entry => UpgradeKey(entry.Key).Key, StringComparer.Ordinal)
            .ThenBy(entry => int.TryParse(entry.Key.AsSpan(entry.Key.LastIndexOf('.') + 1), out int index) ? index : -1))
        {
            if ((entry.Key.StartsWith("credits.", StringComparison.Ordinal) || entry.Key.StartsWith("endings.", StringComparison.Ordinal))
                && !entry.Key.EndsWith(".text", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(entry.Value))
            {
                continue;
            }
            var (key, merge) = UpgradeKey(entry.Key);
            if (key != entry.Key && original.ContainsKey(key))
            {
                throw new InvalidDataException($"Both old and new text keys are present: {entry.Key}, {key}");
            }
            string value = key != entry.Key || !IsFlowingText(key) && !entry.Key.StartsWith("ui.", StringComparison.Ordinal)
                ? Normalize(entry.Value) : entry.Value;
            if (merge && result.TryGetValue(key, out var previous))
            {
                result[key] = (previous + " " + value).Trim();
            }
            else
            {
                result.Add(key, value);
            }
        }
        return result;
    }

    public static int UpgradeFiles(string root)
    {
        var updates = new List<(string Path, Dictionary<string, string> Values)>();
        foreach (string folder in Directory.GetDirectories(root))
        {
            foreach (string file in LocalizationCatalog.Defaults.Keys)
            {
                string path = Path.Combine(folder, file);
                if (!File.Exists(path))
                {
                    continue;
                }
                string input = File.ReadAllText(path);
                using var document = JsonDocument.Parse(input);
                var original = LocalizationCatalog.ReadObject(document.RootElement);
                var upgraded = Upgrade(original);
                if (original.Count != upgraded.Count || original.Any(entry => !upgraded.TryGetValue(entry.Key, out var value) || value != entry.Value))
                {
                    updates.Add((path, upgraded));
                }
            }
        }

        // Parse every file first. Atomic replacement keeps a recoverable copy of every changed file.
        foreach (var update in updates)
        {
            string temporary = update.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string backup = update.Path + ".before-0.7." + Guid.NewGuid().ToString("N") + ".bak";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                    {
                        writer.WriteStartObject();
                        foreach (var entry in update.Values)
                        {
                            writer.WriteString(entry.Key, entry.Value);
                        }
                        writer.WriteEndObject();
                    }
                    stream.Flush(true);
                }
                File.Replace(temporary, update.Path, backup);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        return updates.Count;
    }
}
