using Atria.Platform.Win32;
using Atria.Resources;

namespace Atria.Audio;

/// <summary>Three bounded PCM buffers, file I/O on a worker. Does not choose tracks or invent looping rules.</summary>
public sealed class MusicStream : IDisposable
{
    private const int BufferCount = 3;
    private const int BufferBytes = 64 * 1024;
    private const int RefillPollMilliseconds = 4;
    private readonly FileStream source;
    private readonly NativePcmBuffer?[] buffers = new NativePcmBuffer?[BufferCount];
    private readonly Thread worker;
    private nint voice;
    private volatile bool stopping, finished;
    private Exception? failure;
    private long samplesPlayed;
    internal bool IsDisposed
    {
        get; private set;
    }
    public PcmWaveInfo Format
    {
        get;
    }
    public bool Finished => finished;
    public Exception? Failure => Volatile.Read(ref failure);
    public long SamplesPlayed => Interlocked.Read(ref samplesPlayed);

    public void SetVolume(float volume)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!float.IsFinite(volume) || volume < 0 || volume > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(volume));
        }

        XAudio2Native.SetVolume(voice, volume);
    }

    internal MusicStream(nint engine, string path, float volume)
    {
        if (!float.IsFinite(volume) || volume < 0 || volume > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(volume));
        }

        source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.SequentialScan);
        worker = new Thread(Feed)
        {
            IsBackground = true,
            Name = "Atria PCM stream"
        };
        try
        {
            Format = PcmWaveInfo.Read(source);
            source.Position = Format.DataOffset;
            voice = XAudio2Native.CreatePcmVoice(engine, Format.Channels, Format.SampleRate, Format.BitsPerSample);

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

    private void Feed()
    {
        try
        {
            long remaining = Format.DataLength;
            int next = 0;
            bool started = false;
            while (!stopping)
            {
                var state = XAudio2Native.GetState(voice);
                if (remaining == 0 && state.Queued == 0)
                {
                    Interlocked.Exchange(ref samplesPlayed, Format.DataLength / Format.BlockAlign);
                    finished = true;
                    return;
                }

                Interlocked.Exchange(ref samplesPlayed, (long)state.SamplesPlayed);
                if (remaining > 0 && state.Queued < BufferCount)
                {
                    int size = (int)Math.Min(remaining, BufferBytes / Format.BlockAlign * Format.BlockAlign);
                    var buffer = buffers[next]!;
                    source.ReadExactly(buffer.Bytes[..size]);
                    remaining -= size;
                    XAudio2Native.SubmitBuffer(voice, buffer.Pointer, (uint)size, endOfStream: remaining == 0);
                    next = (next + 1) % BufferCount;
                    if (!started && (remaining == 0 || state.Queued + 1 >= BufferCount))
                    {
                        XAudio2Native.Start(voice);
                        started = true;
                    }
                }
                else
                {
                    Thread.Sleep(RefillPollMilliseconds);
                }
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
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        stopping = true;
        if (worker.IsAlive)
        {
            worker.Join();
        }

        XAudio2Native.DestroyVoice(ref voice);

        for (int i = 0; i < BufferCount; i++)
        {
            buffers[i]?.Dispose();
            buffers[i] = null;
        }

        source.Dispose();
    }
}
