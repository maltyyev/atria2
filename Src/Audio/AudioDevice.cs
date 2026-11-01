using Atria.Platform.Win32;
using static Atria.Platform.NativeCom;

namespace Atria.Audio;

/// <summary>Native XAudio2 ownership; instantiate/dispose on the same thread.</summary>
public sealed class AudioDevice : IGameAudio, IDisposable
{
    internal nint Engine
    {
        get; private set;
    }

    private nint master;
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private bool comInitialized;
    private readonly List<MusicStream> streams = [];
    private readonly List<SoundEffectBank> effects = [];
    private readonly List<PcmChunkStream> chunkStreams = [];
    public AudioDevice()
    {
        comInitialized = InitializeMultithreadedApartment();
        try
        {
            Engine = XAudio2Native.CreateEngine();
            master = XAudio2Native.CreateMasteringVoice(Engine);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public MusicStream PlayFile(string path, float volume = 1)
    {
        if (Engine == 0)
        {
            throw new ObjectDisposedException(nameof(AudioDevice));
        }

        streams.RemoveAll(static stream => stream.IsDisposed);
        var stream = new MusicStream(Engine, path, volume);
        streams.Add(stream);
        return stream;
    }

    public PcmChunkStream PlayPcmChunks(string path, IReadOnlyList<PcmChunk> chunks, PcmAudioFormat format, float volume = 1, bool startPaused = false)
    {
        if (Engine == 0)
        {
            throw new ObjectDisposedException(nameof(AudioDevice));
        }

        if (Environment.CurrentManagedThreadId != threadId)
        {
            throw new InvalidOperationException("Create movie audio on the owning thread.");
        }

        chunkStreams.RemoveAll(static stream => stream.IsDisposed);
        var stream = new PcmChunkStream(Engine, path, chunks, format, volume, startPaused);
        chunkStreams.Add(stream);
        return stream;
    }

    IBufferedSoundEffect IGameAudio.LoadEffect(ReadOnlyMemory<byte> wave) => LoadEffect(wave);

    public SoundEffectBank LoadEffect(ReadOnlyMemory<byte> wave)
    {
        if (Engine == 0)
        {
            throw new ObjectDisposedException(nameof(AudioDevice));
        }

        if (Environment.CurrentManagedThreadId != threadId)
        {
            throw new InvalidOperationException("Create effects on the owning audio thread.");
        }

        effects.RemoveAll(static effect => effect.IsDisposed);
        var effect = new SoundEffectBank(this, wave);
        effects.Add(effect);
        return effect;
    }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != threadId)
        {
            throw new InvalidOperationException("Dispose audio device on its owning thread.");
        }

        foreach (var s in streams)
        {
            s.Dispose();
        }

        streams.Clear();
        foreach (var s in chunkStreams)
        {
            s.Dispose();
        }

        chunkStreams.Clear();
        foreach (var effect in effects)
        {
            effect.Dispose();
        }

        effects.Clear();
        XAudio2Native.DestroyVoice(ref master);

        nint engine = Engine;
        Release(ref engine);
        Engine = 0;
        if (comInitialized)
        {
            CoUninitialize();
            comInitialized = false;
        }
    }
}
