using Atria.Platform;
using Atria.Platform.Windows.Win32;
using Atria.Resources;

using Atria.Audio;

namespace Atria.Platform.Windows.Audio;

/// <summary>One immutable PCM asset, eight independent XAudio2 voices and one shared sample allocation.</summary>
public sealed class SoundEffectBank : IBufferedSoundEffect
{
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private readonly AudioDevice device;
    private SoundEffectRotation? rotation;
    private NativePcmBuffer? samples;
    public PcmWaveInfo Format
    {
        get;
    }
    public bool IsDisposed
    {
        get; private set;
    }
    public int Cursor => rotation?.Cursor ?? 0;

    internal SoundEffectBank(AudioDevice device, ReadOnlyMemory<byte> wave)
    {
        this.device = device;
        var pcm = PcmWave.Parse(wave);
        Format = pcm.Info;
        if (pcm.Samples.Length == 0)
        {
            throw new InvalidDataException("An effect requires sample frames.");
        }

        if (device.Engine == 0)
        {
            throw new ObjectDisposedException(nameof(AudioDevice));
        }

        var voices = new ISoundEffectVoice[SoundEffectRotation.VoiceCount];
        try
        {
            samples = new NativePcmBuffer(pcm.Samples.Length);
            pcm.Samples.Span.CopyTo(samples.Bytes);
            for (int i = 0; i < voices.Length; i++)
            {
                var voice = new NativeVoice(device.Engine, Format, samples.Pointer);
                voices[i] = voice;
                ownedVoices.Add(voice);
            }

            rotation = new SoundEffectRotation(voices);
        }
        catch
        {
            foreach (var voice in voices)
            {
                voice?.Dispose();
            }

            samples?.Dispose();
            samples = null;
            throw;
        }
    }

    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != threadId)
        {
            throw new InvalidOperationException("Use effects on the owning audio thread.");
        }

        ObjectDisposedException.ThrowIf(IsDisposed || device.Engine == 0, this);
    }

    public void Play()
    {
        CheckOwner();
        rotation!.Play();
    }

    public void SetCurrentVolume(int hundredthsDb)
    {
        CheckOwner();
        rotation!.SetCurrentVolume(hundredthsDb);
    }

    public void StopAll()
    {
        CheckOwner();
        rotation!.StopAll();
    }

    public uint QueuedVoiceCount
    {
        get
        {
            CheckOwner();
            uint count = 0;
            foreach (var voice in ownedVoices)
            {
                if (voice.IsQueued)
                {
                    count++;
                }
            }

            return count;
        }
    }

    private readonly List<NativeVoice> ownedVoices = [];
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != threadId)
        {
            throw new InvalidOperationException("Dispose effects on the owning audio thread.");
        }

        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        rotation?.Dispose();
        rotation = null;
        samples?.Dispose();
        samples = null;
    }

    private sealed class NativeVoice : ISoundEffectVoice
    {
        private nint voice;
        private readonly nint samples;
        private readonly uint length;
        public NativeVoice(nint engine, PcmWaveInfo info, nint samples)
        {
            this.samples = samples;
            length = info.DataLength;
            voice = XAudio2Native.CreatePcmVoice(engine, info.Channels, info.SampleRate, info.BitsPerSample);
        }

        public bool IsQueued
        {
            get
            {
                var state = XAudio2Native.GetState(voice);
                return state.Queued != 0;
            }
        }

        public void PlayIfIdle()
        {
            if (IsQueued)
            {
                return; // DirectSound Play while playing continues, never restarts or queues another copy.
            }

            XAudio2Native.SubmitBuffer(voice, samples, length, endOfStream: true);
            XAudio2Native.Start(voice);
        }

        public void SetVolume(float amplitude) => XAudio2Native.SetVolume(voice, amplitude);
        public void StopAndReset()
        {
            XAudio2Native.Stop(voice);
            XAudio2Native.FlushBuffers(voice);
        }

        public void Dispose()
        {
            if (voice == 0)
            {
                return;
            }

            XAudio2Native.DestroyVoice(ref voice);
        }
    }
}
