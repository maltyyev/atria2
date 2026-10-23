namespace Atria.Audio;

public readonly record struct MusicControllerSnapshot(bool Alternate,int CachedTrack);

public enum MusicPlaybackState { Unavailable, Playing, Stopped }

public interface IMusicTransport
{
    MusicPlaybackState State { get; }
    void Stop();
    void Play(int logicalTrack);
}

/// <summary>Recovered 0x404110. Call on the original logical update, never per monitor present.</summary>
public sealed class MusicController(IMusicTransport transport)
{
    private bool alternate;
    public int CachedTrack { get; private set; } = -1;

    public MusicControllerSnapshot CaptureRuntime()=>new(alternate,CachedTrack);
    public static void ValidateRuntime(MusicControllerSnapshot value)
    {if(value.CachedTrack!=-1 && (value.CachedTrack<1||value.CachedTrack>27))throw new InvalidDataException("Retained music cache is outside recovered soundtrack.");}
    public void RestoreRuntime(MusicControllerSnapshot value)
    {ValidateRuntime(value);alternate=value.Alternate;CachedTrack=value.CachedTrack;}
    public void Update(int requestedTrack)
    {
        alternate = !alternate;
        if (!alternate) return;
        if (requestedTrack < 1) { transport.Stop(); return; }
        if (requestedTrack > 27) throw new InvalidDataException("Music track is outside the supplied original soundtrack.");
        if (transport.State == MusicPlaybackState.Unavailable) return;
        if (transport.State != MusicPlaybackState.Stopped)
        {
            if (CachedTrack == requestedTrack) return;
            transport.Stop();
        }
        transport.Play(requestedTrack);
        CachedTrack = requestedTrack;
    }

    // 0x44d4c0, bounded at the following ASCII literal. No RNG decisions occur here.
    public static int TownTrack(int townId)
    {
        ReadOnlySpan<byte> tracks = [1,10,9,9,10,14,12,11,12,18,9,10,16,17,12,12,13,10,14,17,
            12,14,17,16,13,17,17,10,16,14,18,14,13,14,15,13,12,16,13,9,
            17,10,13,14,14,16,17,18,14,9,19,1];
        if ((uint)townId >= (uint)tracks.Length) throw new ArgumentOutOfRangeException(nameof(townId));
        return tracks[townId];
    }
}

/// <summary>Owns one streaming voice. Device ownership stays with the game host.</summary>
public sealed class FileMusicTransport(AudioDevice device, string musicDirectory, float volume = 1) : IMusicTransport, IDisposable
{
    private readonly string root = Path.GetFullPath(musicDirectory);
    private float currentVolume=volume;
    private MusicStream? stream;
    private int currentLogicalTrack;
    private bool disposed;
    public MusicPlaybackState State
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (stream?.Failure is Exception failure) throw new IOException("Music playback failed.", failure);
            return stream is null || stream.Finished ? MusicPlaybackState.Stopped : MusicPlaybackState.Playing;
        }
    }
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        stream?.Dispose(); stream = null;currentLogicalTrack=0;
    }
    public void Play(int logicalTrack)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (logicalTrack < 1 || logicalTrack > 27) throw new ArgumentOutOfRangeException(nameof(logicalTrack));
        Stop();
        stream = device.PlayFile(Path.Combine(root, $"Track{logicalTrack + 1:00}.wav"), currentVolume);currentLogicalTrack=logicalTrack;
    }
    public int CapturePlayingTrack()=>State==MusicPlaybackState.Playing?currentLogicalTrack:0;
    public void ValidateTrack(int logicalTrack)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(logicalTrack<1||logicalTrack>27)throw new InvalidDataException("Music track is outside supplied soundtrack.");
        using var file=new FileStream(Path.Combine(root,$"Track{logicalTrack+1:00}.wav"),FileMode.Open,FileAccess.Read,FileShare.Read);
        _=Atria.Resources.PcmWaveInfo.Read(file);
    }
    public void SetVolume(float value)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(!float.IsFinite(value)||value<0||value>1) throw new ArgumentOutOfRangeException(nameof(value));
        stream?.SetVolume(value);currentVolume=value;
    }
    public void Dispose()
    {
        if (disposed) return;
        Stop(); disposed = true;
    }
}
