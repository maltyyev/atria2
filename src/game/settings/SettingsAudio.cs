namespace Atria.Game.Settings;

public static class SettingsAudio
{
    // 401400 applies (mixerMinimum+mixerMaximum)/divisor, not a mute at level0.
    // Modern app-local gain uses the native zero-minimum/full-range ratio; never changes Windows mixer.
    public static float MusicGain(int level)=>level switch
    {0=>1f/32,1=>1f/16,2=>1f/8,3=>1f/4,4=>1f/2,_=>throw new ArgumentOutOfRangeException(nameof(level))};
    public static int EffectsHundredthDecibels(int level)=>level switch
    {0=>-10000,1=>-1500,2=>-1000,3=>-500,4=>0,_=>throw new ArgumentOutOfRangeException(nameof(level))};
    public static float EffectsGain(int level)=>Atria.Audio.SoundEffectRotation.Amplitude(EffectsHundredthDecibels(level));
}
