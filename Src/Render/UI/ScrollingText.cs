namespace Atria.Render.UI;

public sealed class ScrollingText
{
    private readonly List<(int SourceRow, int DisplayRow)> anchors = [];
    private readonly string domain;
    private readonly int width;
    private LocalizationCatalog? language;
    private IReadOnlyList<string> lines = [];
    public IReadOnlyList<string> Lines
    {
        get
        {
            RefreshLanguage();
            return lines;
        }
    }

    public ScrollingText(string domain, int width)
    {
        this.domain = domain;
        this.width = width;
        RefreshLanguage();
    }

    private void RefreshLanguage()
    {
        if (ReferenceEquals(language, GameText.Current))
            return;

        language = GameText.Current;
        anchors.Clear();
        var lines = new List<string>();
        var keys = LocalizationCatalog.Defaults.Values.SelectMany(file => file.Keys)
            .Where(key => key.StartsWith(domain + ".", StringComparison.Ordinal) && key.EndsWith(".text", StringComparison.Ordinal))
            .Select(key => (Key: key, Row: int.Parse(key.AsSpan(domain.Length + 1, key.Length - domain.Length - 6), System.Globalization.CultureInfo.InvariantCulture)))
            .OrderBy(entry => entry.Row);
        foreach (var entry in keys)
        {
            anchors.Add((entry.Row, lines.Count));
            lines.AddRange(TextFlow.Wrap(GameText.Get(entry.Key), width));
            lines.Add("");
        }
        this.lines = lines.AsReadOnly();
    }

    public int MapSourceRow(int sourceRow)
    {
        RefreshLanguage();
        foreach (var anchor in anchors)
        {
            if (anchor.SourceRow >= sourceRow)
            {
                return anchor.DisplayRow;
            }
        }
        return Lines.Count + Math.Max(0, sourceRow - (anchors.Count == 0 ? 0 : anchors[^1].SourceRow));
    }
}
