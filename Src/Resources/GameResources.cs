namespace Atria.Resources;

internal static class GameResources
{
    public static byte[] Read(string root, string archiveName, ReadOnlySpan<byte> member)
    {
        if (archiveName == "SCRIPT.000" && Atria.Game.State.NarrativeScenes.Read(member) is { } authored)
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
