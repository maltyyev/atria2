using System.Globalization;
using System.Text;

namespace Atria.Localization;

public static class TextTemplate
{
    public static HashSet<string> Parameters(string text)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        Scan(text, name => { result.Add(name); return ""; });
        return result;
    }

    public static string Format(string text, params (string Name, object? Value)[] arguments)
    {
        var values = arguments.ToDictionary(a => a.Name, a => Convert.ToString(a.Value, CultureInfo.InvariantCulture) ?? "", StringComparer.Ordinal);
        return Scan(text, name => values.TryGetValue(name, out var value) ? value : throw new FormatException($"Missing text parameter: {name}"));
    }

    private static string Scan(string text, Func<string, string> replace)
    {
        var output = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is not ('{' or '}'))
            {
                output.Append(c);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == c)
            {
                output.Append(c);
                i++;
                continue;
            }

            int end = c == '{' ? text.IndexOf('}', i + 1) : -1;
            if (end < 0)
            {
                throw new FormatException("Unbalanced template braces.");
            }

            string name = text[(i + 1)..end];
            if (name.Length == 0 || !char.IsAsciiLetter(name[0]) || name.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_'))
            {
                throw new FormatException($"Invalid parameter name: {name}");
            }

            output.Append(replace(name));
            i = end;
        }

        return output.ToString();
    }
}
