using System.Text;
using Atria.Resources;

namespace Atria.Localization;

/// <summary>Fixed resource identities point to existing editable translation keys.</summary>
public static class ResourceLabelKeys
{
    private static readonly IReadOnlyDictionary<string, string> scenes;
    private static readonly IReadOnlyDictionary<string, string> people;

    static ResourceLabelKeys()
    {
        using var data = GameData.Open("ResourceLabelKeys");
        scenes = LocalizationCatalog.ReadObject(data.RootElement.GetProperty("SceneLabels"));
        people = LocalizationCatalog.ReadObject(data.RootElement.GetProperty("PeopleNames"));
    }

    public static string? Scene(string member, int objectIndex) =>
        scenes.GetValueOrDefault($"{MemberId(member)}/{objectIndex}");

    public static string? Person(ReadOnlySpan<byte> scriptMember, ReadOnlySpan<byte> handler)
    {
        string script = MemberId(Encoding.ASCII.GetString(RawBytes.NullTerminated(scriptMember)));
        string function = Encoding.ASCII.GetString(RawBytes.NullTerminated(handler));
        return people.GetValueOrDefault($"{script}/{function}");
    }

    private static string MemberId(string member) => Path.GetFileNameWithoutExtension(member).ToUpperInvariant();
}
