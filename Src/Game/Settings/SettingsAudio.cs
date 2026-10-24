namespace Atria.Game.Settings;

public static class SettingsAudio
{
    // 401400 applies (mixerMinimum+mixerMaximum)/divisor, not a mute at level0.
    // Modern app-local gain uses the native zero-minimum/full-range ratio; never changes Windows mixer.
    public static float MusicGain(int level) => level switch
    {
        0 => 1f / 32,
        1 => 1f / 16,
        2 => 1f / 8,
        3 => 1f / 4,
        4 => 1f / 2,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
    public static int EffectsHundredthDecibels(int level)
    {
        var attenuation = Atria.Audio.SoundEffectBank.UiAttenuation;
        if ((uint)level >= (uint)attenuation.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        return attenuation[level];
    }
    public static float EffectsGain(int level) => Atria.Audio.SoundEffectRotation.Amplitude(EffectsHundredthDecibels(level));
}
