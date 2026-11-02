namespace Atria.Core.Contracts.Audio;

public interface IBufferedSoundEffect : IDisposable
{
    void SetCurrentVolume(int hundredthsDb);
    void Play();
    void StopAll();
}
