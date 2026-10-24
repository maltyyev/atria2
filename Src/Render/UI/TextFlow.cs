using System.Globalization;
using System.Text;
using Atria.Render.Codecs;

namespace Atria.Render.UI;

public static class TextFlow
{
    public static int MeasureSmall(string text) => BitmapFont.TryLegacy(text, out var bytes)
        ? bytes.Length * 8 : UnicodeText.Layout(text, 12).Advance;

    public static string[] Wrap(string text, int width, Func<string, int>? measure = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        measure ??= MeasureSmall;
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                lines.Add("");
                continue;
            }

            string current = "";
            foreach (string word in paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (measure(candidate) <= width)
                {
                    current = candidate;
                    continue;
                }
                if (current.Length != 0)
                {
                    lines.Add(current);
                    current = "";
                }

                var elements = StringInfo.GetTextElementEnumerator(word);
                while (elements.MoveNext())
                {
                    string element = elements.GetTextElement();
                    if (current.Length != 0 && measure(current + element) > width)
                    {
                        lines.Add(current);
                        current = "";
                    }
                    current += element;
                }
            }
            lines.Add(current);
        }
        return lines.ToArray();
    }
}
