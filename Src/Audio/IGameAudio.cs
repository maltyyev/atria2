namespace Atria.Audio;

public interface IGameAudio
{
    IBufferedSoundEffect LoadEffect(ReadOnlyMemory<byte> wave);
}

public interface IFileMusicTransport : IMusicTransport
{
    int CapturePlayingTrack();
    void ValidateTrack(int logicalTrack);
    void SetVolume(float value);
}
