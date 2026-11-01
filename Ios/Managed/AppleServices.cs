using System.Runtime.InteropServices;
using Atria.Audio;
using Atria.Render.UI;
using Atria.Resources;
using Atria.Video;

namespace Atria.Ios;

internal static partial class AppleNative
{
    private const string Library = "__Internal";
    [LibraryImport(Library, EntryPoint = "apple_sound_create")]
    internal static unsafe partial long SoundCreate(byte* data, int length);
    [LibraryImport(Library, EntryPoint = "apple_sound_command")]
    internal static partial int SoundCommand(long id, int command, float value);
    [LibraryImport(Library, EntryPoint = "apple_music_open", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial long MusicOpen(string path);
    [LibraryImport(Library, EntryPoint = "apple_text_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial nint TextCreate(string text, string family, float size, int bold, int wrap, int* metrics);
    [LibraryImport(Library, EntryPoint = "apple_text_free")]
    internal static partial void TextFree(nint pixels);
    [LibraryImport(Library, EntryPoint = "apple_movie_start", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void MovieStart(string path, float volume);
    [LibraryImport(Library, EntryPoint = "apple_movie_status")]
    internal static partial int MovieStatus();
    [LibraryImport(Library, EntryPoint = "apple_movie_stop")]
    internal static partial void MovieStop();
}

internal enum SoundCommand { Play, Stop, Volume, IsPlaying, Release }

public sealed class AppleAudio : IGameAudio, IDisposable
{
    private readonly List<AppleEffect> effects = [];

    public IBufferedSoundEffect LoadEffect(ReadOnlyMemory<byte> wave)
    {
        _ = PcmWave.Parse(wave);
        var voices = new ISoundEffectVoice[SoundEffectRotation.VoiceCount];
        try
        {
            for (int i = 0; i < voices.Length; i++) voices[i] = new AppleVoice(wave);
            var effect = new AppleEffect(new SoundEffectRotation(voices), released => effects.Remove(released));
            effects.Add(effect);
            return effect;
        }
        catch
        {
            foreach (var voice in voices) voice?.Dispose();
            throw;
        }
    }

    public void Dispose() { foreach (var effect in effects.ToArray()) effect.Dispose(); }

    private sealed class AppleEffect(SoundEffectRotation rotation, Action<AppleEffect> released) : IBufferedSoundEffect
    {
        public void Play() => rotation.Play();
        public void StopAll() => rotation.StopAll();
        public void SetCurrentVolume(int volume) => rotation.SetCurrentVolume(volume);
        public void Dispose()
        {
            rotation.Dispose();
            released(this);
        }
    }

    private sealed class AppleVoice : ISoundEffectVoice
    {
        private long handle;
        public unsafe AppleVoice(ReadOnlyMemory<byte> wave)
        {
            fixed (byte* data = wave.Span) handle = AppleNative.SoundCreate(data, wave.Length);
            if (handle == 0) throw new IOException("AVAudioPlayer could not decode a sound effect.");
        }
        private void Command(SoundCommand command, float value = 0)
        {
            if (AppleNative.SoundCommand(handle, (int)command, value) < 0)
                throw new IOException($"Audio command {command} failed.");
        }
        public void PlayIfIdle() => Command(SoundCommand.Play);
        public void StopAndReset() => Command(SoundCommand.Stop);
        public void SetVolume(float amplitude) => Command(SoundCommand.Volume, amplitude);
        public void Dispose()
        {
            if (handle == 0) return;
            Command(SoundCommand.Release);
            handle = 0;
        }
    }
}

public sealed class AppleMusic(string directory) : IFileMusicTransport, IDisposable
{
    private readonly MusicCatalog catalog = new(directory);
    private long handle;
    private int track;
    private float volume = 1;
    public MusicPlaybackState State => handle != 0 && AppleNative.SoundCommand(handle, (int)SoundCommand.IsPlaying, 0) == 1
        ? MusicPlaybackState.Playing : MusicPlaybackState.Stopped;
    public int CapturePlayingTrack() => State == MusicPlaybackState.Playing ? track : 0;
    public void ValidateTrack(int logicalTrack) { using var file = File.OpenRead(catalog.GetPath(logicalTrack)); _ = PcmWaveInfo.Read(file); }
    public void Play(int logicalTrack)
    {
        Stop();
        handle = AppleNative.MusicOpen(catalog.GetPath(logicalTrack));
        if (handle == 0) throw new IOException($"Cannot open music track {logicalTrack}.");
        track = logicalTrack;
        SetVolume(volume);
        if (AppleNative.SoundCommand(handle, (int)SoundCommand.Play, 0) < 0) throw new IOException("Cannot play music.");
    }
    public void SetVolume(float value)
    {
        volume = Math.Clamp(value, 0, 1);
        if (handle != 0) AppleNative.SoundCommand(handle, (int)SoundCommand.Volume, volume);
    }
    public void Stop()
    {
        if (handle != 0) AppleNative.SoundCommand(handle, (int)SoundCommand.Release, 0);
        handle = 0;
        track = 0;
    }
    public void Dispose() => Stop();
}

public sealed class AppleText : ITextRasterizer
{
    public unsafe TextMask Render(string text, float size, string family, bool bold, int wrapWidth)
    {
        int* metrics = stackalloc int[4];
        nint pixels = AppleNative.TextCreate(text, family, size, bold ? 1 : 0, wrapWidth, metrics);
        if (pixels == 0) throw new InvalidOperationException("CoreText could not render text.");
        try
        {
            int count = checked(metrics[0] * metrics[1]);
            if (metrics[0] <= 0 || metrics[1] <= 0 || count > 8 * 1024 * 1024)
                throw new InvalidDataException("CoreText mask exceeds supported dimensions.");
            var coverage = new byte[count];
            Marshal.Copy(pixels, coverage, 0, count);
            return new(metrics[0], metrics[1], metrics[2], coverage, 0, metrics[3]);
        }
        finally { AppleNative.TextFree(pixels); }
    }
    public void Dispose() { }
}

public sealed class AppleMovies(MobileWindow window, string directory)
{
    public MoviePlaybackResult Play(string path, Func<bool> skip, float volume, double maximumSeconds)
    {
        string converted = Path.Combine(directory, Path.GetFileNameWithoutExtension(path).ToLowerInvariant() + ".mp4");
        if (!File.Exists(converted)) throw new FileNotFoundException("Converted iOS movie is missing.", converted);
        AppleNative.MovieStart(converted, volume);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        bool skipped = false, timedOut = false;
        try
        {
            while (AppleNative.MovieStatus() == 0)
            {
                window.Pump();
                if (window.Closed) throw new OperationCanceledException();
                if (window.Focused && skip()) { skipped = true; break; }
                if (elapsed.Elapsed.TotalSeconds >= maximumSeconds) { timedOut = true; break; }
                Thread.Sleep(10);
            }
            if (AppleNative.MovieStatus() < 0) throw new IOException("AVPlayer failed to play the cutscene.");
        }
        finally { AppleNative.MovieStop(); }
        return new(0, 0, 0, skipped, timedOut);
    }
}
