using Atria.Platform.Win32;

namespace Atria.Audio;

public readonly record struct PcmChunk(long Offset, int Length);
public readonly record struct PcmAudioFormat(ushort Channels, uint SampleRate, ushort BitsPerSample)
{
    private const int MaximumChannelCount = 64;

    public ushort BlockAlign => checked((ushort)(Channels * BitsPerSample / 8));
    public uint ByteRate => checked(SampleRate * BlockAlign);

    public void Validate()
    {
        if (Channels == 0 || Channels > MaximumChannelCount || SampleRate == 0 || BitsPerSample is not (8 or 16 or 24 or 32))
        {
            throw new InvalidDataException("Invalid integer PCM format.");
        }

        _ = BlockAlign;
        _ = ByteRate;
    }
}

/// <summary>Continuous PCM read over immutable, bounded file ranges; no RIFF assumptions or extracted files.</summary>
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

/// <summary>Three fixed PCM buffers; XAudio2 sample count is the movie's audio playback clock.</summary>
public sealed class PcmChunkStream : IDisposable
{
    private const int BufferCount = 3;
    private const int BufferBytes = 64 * 1024;
    private const int RefillPollMilliseconds = 4;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly PcmChunkReader reader;
    private readonly NativePcmBuffer?[] buffers = new NativePcmBuffer?[BufferCount];
    private readonly object gate = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly Thread worker;
    private nint voice;
    private volatile bool stopping, finished, disposed;
    private bool requested, paused, running;
    private Exception? failure;
    private long samplesPlayed;
    public PcmAudioFormat Format
    {
        get;
    }
    public long TotalSampleFrames
    {
        get;
    }
    public long SamplesPlayed => Interlocked.Read(ref samplesPlayed);
    public bool Finished => finished;
    public bool IsDisposed => disposed;
    public Exception? Failure => Volatile.Read(ref failure);
    public int BufferCapacityBytes => BufferCount * BufferBytes;

    internal PcmChunkStream(nint engine, string path, IReadOnlyList<PcmChunk> chunks, PcmAudioFormat format, float volume, bool startPaused)
    {
        if (!float.IsFinite(volume) || volume < 0 || volume > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(volume));
        }

        reader = new PcmChunkReader(path, chunks, format);
        Format = format;
        TotalSampleFrames = reader.TotalBytes / format.BlockAlign;
        paused = startPaused;
        requested = !startPaused;
        worker = new Thread(Feed)
        {
            IsBackground = true,
            Name = "Atria movie PCM"
        };
        try
        {
            voice = XAudio2Native.CreatePcmVoice(engine, format.Channels, format.SampleRate, format.BitsPerSample);

            XAudio2Native.SetVolume(voice, volume);
            for (int i = 0; i < BufferCount; i++)
            {
                buffers[i] = new NativePcmBuffer(BufferBytes);
            }

            worker.Start();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
        {
            throw new InvalidOperationException("Control movie audio on the owning thread.");
        }

        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Start()
    {
        CheckOwner();
        lock (gate)
        {
            requested = true;
            paused = false;
        }

        wake.Set();
    }

    public void Resume() => Start();
    public void Pause()
    {
        CheckOwner();
        lock (gate)
        {
            paused = true;
            if (running)
            {
                XAudio2Native.Stop(voice);
                running = false;
            }
        }
    }

    private void Feed()
    {
        try
        {
            int next = 0;
            while (!stopping)
            {
                lock (gate)
                {
                    var state = XAudio2Native.GetState(voice);
                    // XAudio2 resets SamplesPlayed after an END_OF_STREAM buffer. Preserve our completed timeline.
                    if (reader.RemainingBytes == 0 && state.Queued == 0)
                    {
                        Interlocked.Exchange(ref samplesPlayed, TotalSampleFrames);
                        finished = true;
                        return;
                    }

                    Interlocked.Exchange(ref samplesPlayed, checked((long)state.SamplesPlayed));
                    if (reader.RemainingBytes > 0 && state.Queued < BufferCount)
                    {
                        int capacity = BufferBytes / Format.BlockAlign * Format.BlockAlign;
                        var buffer = buffers[next]!;
                        int count = reader.Read(buffer.Bytes[..capacity]);
                        XAudio2Native.SubmitBuffer(voice, buffer.Pointer, (uint)count, endOfStream: reader.RemainingBytes == 0);
                        next = (next + 1) % BufferCount;
                        state.Queued++;
                    }

                    if (requested && !paused && !running && state.Queued > 0 && (reader.RemainingBytes == 0 || state.Queued == BufferCount))
                    {
                        XAudio2Native.Start(voice);
                        running = true;
                    }
                }

                wake.WaitOne(RefillPollMilliseconds);
            }
        }
        catch (Exception e)
        {
            Volatile.Write(ref failure, e);
            finished = true;
        }
    }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
        {
            throw new InvalidOperationException("Dispose movie audio on the owning thread.");
        }

        if (disposed)
        {
            return;
        }

        disposed = true;
        stopping = true;
        wake.Set();
        if (worker.IsAlive)
        {
            worker.Join();
        }

        XAudio2Native.DestroyVoice(ref voice);

        foreach (var buffer in buffers)
        {
            buffer?.Dispose();
        }

        reader.Dispose();
        wake.Dispose();
    }
}
