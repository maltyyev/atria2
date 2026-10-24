namespace Atria.Audio;

public readonly record struct MusicControllerSnapshot(bool Alternate, int CachedTrack);
public enum MusicPlaybackState
{
    Unavailable,
    Playing,
    Stopped
}

public interface IMusicTransport
{
    MusicPlaybackState State
    {
        get;
    }

    void Stop();
    void Play(int logicalTrack);
}

/// <summary>Recovered 0x404110. Call on the original logical update, never per monitor present.</summary>
public sealed class MusicController(IMusicTransport transport)
{
    private const int NoCachedTrack = -1;
    private bool alternate;
    public int CachedTrack { get; private set; } = NoCachedTrack;

    public MusicControllerSnapshot CaptureRuntime() => new(alternate, CachedTrack);
    public static void ValidateRuntime(MusicControllerSnapshot value)
    {
        if (value.CachedTrack != NoCachedTrack && value.CachedTrack < 1)
        {
            throw new InvalidDataException("Retained music cache must be a positive logical track ID or the empty-cache sentinel.");
        }
    }

    public void RestoreRuntime(MusicControllerSnapshot value)
    {
        ValidateRuntime(value);
        alternate = value.Alternate;
        CachedTrack = value.CachedTrack;
    }

    public void Update(int requestedTrack)
    {
        alternate = !alternate;
        if (!alternate)
        {
            return;
        }

        if (requestedTrack < 1)
        {
            transport.Stop();
            return;
        }

        if (transport.State == MusicPlaybackState.Unavailable)
        {
            return;
        }

        if (transport.State != MusicPlaybackState.Stopped)
        {
            if (CachedTrack == requestedTrack)
            {
                return;
            }

            transport.Stop();
        }

        transport.Play(requestedTrack);
        CachedTrack = requestedTrack;
    }

}
