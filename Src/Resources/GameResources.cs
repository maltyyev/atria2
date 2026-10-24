namespace Atria.Resources;

internal static class GameResources
{
    public static byte[] Read(string root, string archiveName, ReadOnlySpan<byte> member)
    {
        using var archive = BinderArchive.Open(Path.Combine(root, archiveName));
        int index = archive.FindResource(member);
        if (index < 0)
        {
            throw new InvalidDataException($"Required resource is absent from {archiveName}.");
        }

        return archive.ReadMember(index);
    }
}
