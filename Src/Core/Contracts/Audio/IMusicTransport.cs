namespace Atria.Core.Contracts.Audio;

public enum MusicPlaybackState
{
    Unavailable,
    Playing,
    Stopped
}

public interface IMusicTransport
{
    MusicPlaybackState State { get; }
    void Stop();
    void Play(int logicalTrack);
}
