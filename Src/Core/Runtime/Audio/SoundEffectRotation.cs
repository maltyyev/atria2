using Atria.Core.Contracts.Window;
using Atria.Core.Runtime.Clock;
using Atria.Core.Game.Progression;
using Atria.Core.Resources;

namespace Atria.Core.Runtime.Audio;

public interface ISoundEffectVoice : IDisposable
{
    void PlayIfIdle();
    void StopAndReset();
    void SetVolume(float amplitude);
}

/// <summary>Platform-neutral eight-voice rotation and the shared UI sound contract.</summary>
public sealed class SoundEffectRotation : IDisposable
{
    public const int VoiceCount = 8;
    public static IReadOnlyList<string> UiMemberNames { get; } = Array.AsReadOnly(new[] { "MAINMENU", "POPMENU", "CHIPINS", "SELECT", "CANCEL", "DIAL", "BUYITEM", "OUTSHOP", "TREASURE", "DUNGEON1", "DUNGEON2", "DUNGEON3", "WALK1", "EQUIP" });
    public static ReadOnlySpan<int> UiAttenuation => [SilenceHundredthsDb, -1500, -1000, -500, 0];
    public const int SilenceHundredthsDb = -10000;
    private const float HundredthsDbPerAmplitudeDecade = 2000;
    private readonly ISoundEffectVoice[] voices;
    public int Cursor
    {
        get; private set;
    }
    public bool IsDisposed
    {
        get; private set;
    }

    public SoundEffectRotation(ISoundEffectVoice[] voices)
    {
        ArgumentNullException.ThrowIfNull(voices);
        if (voices.Length != VoiceCount || voices.Any(static v => v is null))
        {
            throw new ArgumentException("Exactly eight voices are required.", nameof(voices));
        }

        this.voices = (ISoundEffectVoice[])voices.Clone();
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        voices[Cursor].PlayIfIdle();
        Cursor = (Cursor + 1) % voices.Length;
    }

    public void SetCurrentVolume(int hundredthsDb)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        voices[Cursor].SetVolume(Amplitude(hundredthsDb));
    }

    public static float Amplitude(int hundredthsDb)
    {
        if (hundredthsDb is < SilenceHundredthsDb or > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hundredthsDb));
        }

        return hundredthsDb == SilenceHundredthsDb ? 0 : MathF.Pow(10, hundredthsDb / HundredthsDbPerAmplitudeDecade);
    }

    public void StopAll()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        foreach (var voice in voices)
        {
            voice.StopAndReset();
        }
        // 436010 does not write the separate rotation cursor at record+32.
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        foreach (var voice in voices)
        {
            voice.Dispose();
        }
    }
}
