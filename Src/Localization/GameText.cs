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
        original.Select((text, index) => Legacy($"{domain}.{index:D3}", text)).ToArray();

    public static ReadOnlyMemory<byte> Name(ReadOnlyMemory<byte> original) => Current.Name(original);

    public static ReadOnlyMemory<byte> ItemName(Atria.Resources.ItemCatalog items, int id) => Legacy($"items.{id:D2}.name", items.GetItem(id).Name);
}
