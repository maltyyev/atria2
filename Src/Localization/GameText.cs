using System.Text;

namespace Atria.Localization;

public static class GameText
{
    public static Encoding Encoding { get; } = CodePagesEncodingProvider.Instance.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;
    private static LocalizationCatalog? current;
    public static LocalizationCatalog Current => current ??= new LocalizationCatalog();
    public static void Use(LocalizationCatalog catalog) => current = catalog;
    public static string Get(string key) => Current.Get(key);
    public static string Paragraph(string key, string fallback = "") => Current.TryGet(key, out var text) ? text : fallback;
    public static string Format(string key, params (string Name, object? Value)[] arguments) => TextTemplate.Format(Get(key), arguments);
    public static ReadOnlyMemory<byte> Legacy(string key, ReadOnlyMemory<byte> original) => Current.Legacy(key, original);
    public static byte[] Bytes(string key) => Current.Bytes(key);
    public static string Decode(ReadOnlySpan<byte> text)
    {
        int end = text.IndexOf((byte)0);
        return Encoding.GetString(end < 0 ? text : text[..end]);
    }
    public static IReadOnlyList<ReadOnlyMemory<byte>> Messages(IReadOnlyList<ReadOnlyMemory<byte>> original, string domain = "messages") =>
        new LocalizedRows(original, domain);

    public static IReadOnlyList<ReadOnlyMemory<byte>> ItemNames(Atria.Resources.ItemCatalog items) =>
        new LocalizedRows(Enumerable.Range(0, Atria.Resources.ItemCatalog.Count).Select(index => items.GetItem(index).Name).ToArray(), "items", "D2", ".name");

    private sealed class LocalizedRows(IReadOnlyList<ReadOnlyMemory<byte>> original, string domain, string indexFormat = "D3", string suffix = "") : IReadOnlyList<ReadOnlyMemory<byte>>
    {
        private readonly string[] keys = Enumerable.Range(0, original.Count)
            .Select(index => $"{domain}.{index.ToString(indexFormat, System.Globalization.CultureInfo.InvariantCulture)}{suffix}").ToArray();
        private LocalizationCatalog? catalog;
        private ReadOnlyMemory<byte>[] translated = [];
        public int Count => original.Count;
        public ReadOnlyMemory<byte> this[int index]
        {
            get
            {
                if (!ReferenceEquals(catalog, Current))
                {
                    translated = keys.Select((key, row) => Legacy(key, original[row])).ToArray();
                    catalog = Current;
                }
                return translated[index];
            }
        }
        public IEnumerator<ReadOnlyMemory<byte>> GetEnumerator()
        {
            for (int index = 0; index < Count; index++)
                yield return this[index];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static ReadOnlyMemory<byte> Name(ReadOnlyMemory<byte> original) => Current.Name(original);

    public static ReadOnlyMemory<byte> ItemName(Atria.Resources.ItemCatalog items, int id) => Legacy($"items.{id:D2}.name", items.GetItem(id).Name);
}
