using System.Buffers.Binary;
using Atria.Resources;

namespace Atria.Game.Combat;

public sealed class CombatInfoDefinition
{
    public IReadOnlyList<CombatInfoCharacter> Characters
    {
        get;
    }
    public IReadOnlyList<ReadOnlyMemory<byte>> AnimationResources
    {
        get;
    }
    public ReadOnlyMemory<byte> TrailingBytes
    {
        get;
    }

    internal CombatInfoDefinition(List<CombatInfoCharacter> characters, List<ReadOnlyMemory<byte>> animations, ReadOnlyMemory<byte> trailing)
    {
        Characters = characters.AsReadOnly();
        AnimationResources = animations.AsReadOnly();
        TrailingBytes = trailing;
    }

    public static CombatInfoDefinition Parse(ReadOnlyMemory<byte> source, Action<int>? loadNumericSound = null) => CombatInfoCodec.Parse(source, loadNumericSound);

    public static CombatInfoDefinition Load(string resourceRoot, ReadOnlySpan<byte> resource, Action<int>? loadNumericSound = null)
    {
        using var archive = BinderArchive.Open(Path.Combine(resourceRoot, "INF.000"));
        int index = archive.FindResource(resource);
        if (index < 0)
        {
            throw new FileNotFoundException("Required combat INF resource is missing.");
        }

        return Parse(archive.ReadMember(index), loadNumericSound);
    }
}
