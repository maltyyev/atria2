using System.Globalization;

namespace Atria.Audio;

/// <summary>Logical music IDs mapped to the supplied CD rip, whose first track contains data.</summary>
public sealed class MusicCatalog
{
    private const int DataTrackCount = 1;
    private readonly Dictionary<int, string> paths = [];

    public int Count => paths.Count;

    public MusicCatalog(string directory)
    {
        foreach (string path in Directory.EnumerateFiles(Path.GetFullPath(directory)))
        {
            if (!Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith("Track", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(name.AsSpan("Track".Length), NumberStyles.None, CultureInfo.InvariantCulture, out int cdTrack)
                || cdTrack <= DataTrackCount)
            {
                continue;
            }

            int logicalTrack = cdTrack - DataTrackCount;
            if (!paths.TryAdd(logicalTrack, path))
            {
                throw new InvalidDataException($"Multiple music files map to logical track {logicalTrack}.");
            }
        }
    }

    public string GetPath(int logicalTrack)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(logicalTrack);
        return paths.TryGetValue(logicalTrack, out string? path)
            ? path
            : throw new FileNotFoundException($"Music file for logical track {logicalTrack} is missing (CD track {logicalTrack + (long)DataTrackCount}).");
    }
}
