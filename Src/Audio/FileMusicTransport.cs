namespace Atria.Audio;

/// <summary>Owns one streaming voice. Device ownership stays with the game host.</summary>
public sealed class FileMusicTransport(AudioDevice device, string musicDirectory, float volume = 1) : IFileMusicTransport, IDisposable
{
    private readonly MusicCatalog catalog = new(musicDirectory);
    private float currentVolume = volume;
    private MusicStream? stream;
    private int currentLogicalTrack;
    private bool disposed;
    public MusicPlaybackState State
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (stream?.Failure is Exception failure)
            {
                throw new IOException("Music playback failed.", failure);
            }

            return stream is null || stream.Finished ? MusicPlaybackState.Stopped : MusicPlaybackState.Playing;
        }
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        stream?.Dispose();
        stream = null;
        currentLogicalTrack = 0;
    }

    public void Play(int logicalTrack)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string path = catalog.GetPath(logicalTrack);
        Stop();
        stream = device.PlayFile(path, currentVolume);
        currentLogicalTrack = logicalTrack;
    }

    public int CapturePlayingTrack() => State == MusicPlaybackState.Playing ? currentLogicalTrack : 0;
    public void ValidateTrack(int logicalTrack)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var file = new FileStream(catalog.GetPath(logicalTrack), FileMode.Open, FileAccess.Read, FileShare.Read);
        _ = Atria.Resources.PcmWaveInfo.Read(file);
    }

    public void SetVolume(float value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!float.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        stream?.SetVolume(value);
        currentVolume = value;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Stop();
        disposed = true;
    }
}
