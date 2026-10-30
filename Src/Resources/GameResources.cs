namespace Atria.Resources;

internal static class GameResources
{
    public static byte[] Read(string root, string archiveName, ReadOnlySpan<byte> member, string? sourceHash = null)
    {
        if (Atria.Game.Dungeon.AuthoredDungeons.Read(root, archiveName, member) is { } dungeon)
            return dungeon;
        if (archiveName == "SCRIPT.000" && Atria.Game.State.NarrativeScenes.Read(member, sourceHash) is { } authored)
        {
            return authored;
        }

        using var archive = BinderArchive.Open(Path.Combine(root, archiveName));
        int index = archive.FindResource(member);
        if (index < 0)
        {
            throw new InvalidDataException($"Required resource is absent from {archiveName}.");
        }

        return archive.ReadMember(index);
    }
}
