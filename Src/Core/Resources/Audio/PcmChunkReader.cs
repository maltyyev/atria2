namespace Atria.Core.Resources.Audio;

/// <summary>Reads immutable, bounded PCM file ranges without assuming a RIFF container or extracting bytes.</summary>
public sealed class PcmChunkReader : IDisposable
{
    private const int FileBufferBytes = 64 * 1024;
    private readonly FileStream source;
    private readonly PcmChunk[] chunks;
    private int chunkIndex, chunkPosition;
    public PcmAudioFormat Format
    {
        get;
    }
    public long TotalBytes
    {
        get;
    }
    public long RemainingBytes
    {
        get; private set;
    }

    public PcmChunkReader(string path, IReadOnlyList<PcmChunk> chunks, PcmAudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        format.Validate();
        Format = format;
        this.chunks = chunks.ToArray();
        source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileBufferBytes, FileOptions.RandomAccess);
        try
        {
            long total = 0;
            foreach (var chunk in this.chunks)
            {
                if (chunk.Offset < 0 || chunk.Length < 0 || chunk.Offset > source.Length || chunk.Length > source.Length - chunk.Offset
                    || chunk.Length % format.BlockAlign != 0)
                {
                    throw new InvalidDataException("PCM chunk is unaligned or outside source file.");
                }

                total = checked(total + chunk.Length);
            }

            TotalBytes = RemainingBytes = total;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    public int Read(Span<byte> destination)
    {
        if (destination.Length % Format.BlockAlign != 0)
        {
            throw new ArgumentException("PCM destination must hold complete sample frames.", nameof(destination));
        }

        int written = 0;
        while (written < destination.Length && chunkIndex < chunks.Length)
        {
            var chunk = chunks[chunkIndex];
            int count = Math.Min(destination.Length - written, chunk.Length - chunkPosition);
            if (count == 0)
            {
                chunkIndex++;
                chunkPosition = 0;
                continue;
            }

            source.Position = checked(chunk.Offset + chunkPosition);
            source.ReadExactly(destination.Slice(written, count));
            chunkPosition += count;
            written += count;
            RemainingBytes -= count;
        }

        return written;
    }

    public void Dispose() => source.Dispose();
}
