using Atria.Core.Resources;
using Atria.Platforms.Windows.Audio;

namespace Tests.Windows.Audio;

/// <summary>Checks XAudio2's muted effect playback lifecycle with a real game resource.</summary>
public static class SoundEffectProbeTests
{
    public static string RunMutedProbe(string resourceRoot)
    {
        using var archive = BinderArchive.Open(Path.Combine(resourceRoot, "at2wav.000"));
        byte[] pcm = archive.ReadMember(archive.FindResource("MAINMENU"u8));
        using var device = new AudioDevice();
        using var effect = device.LoadEffect(pcm);
        for (int i = 0; i < 8; i++)
        {
            effect.SetCurrentVolume(-10000);
            effect.Play();
        }

        uint overlapping = effect.QueuedVoiceCount;
        if (overlapping != 8)
            throw new InvalidOperationException($"Expected eight queued effect voices; got {overlapping}.");

        effect.Play();
        if (effect.Cursor != 1)
            throw new InvalidOperationException("Native effect cursor wrap failed.");

        effect.StopAll();
        if (effect.Cursor != 1)
            throw new InvalidOperationException("Stop unexpectedly reset voice cursor.");

        // Allow XAudio2's engine processing pass to apply Stop/Flush before querying its state.
        Thread.Sleep(50);
        if (effect.QueuedVoiceCount != 0)
            throw new InvalidOperationException("Stop/reset did not drain voices.");

        effect.Play();
        double seconds = effect.Format.DurationSeconds;
        if (seconds > 10)
            throw new InvalidOperationException("Unexpectedly long UI probe effect.");

        var deadline = DateTime.UtcNow.AddSeconds(seconds + 2);
        while (effect.QueuedVoiceCount != 0 && DateTime.UtcNow < deadline)
            Thread.Sleep(5);

        if (effect.QueuedVoiceCount != 0)
            throw new InvalidOperationException("Effect failed to finish.");

        return $"Native muted PCM effects: eight overlaps, cursor wrap, no-restart reuse, stop/flush and natural completion passed ({seconds:F3}s asset).";
    }
}
