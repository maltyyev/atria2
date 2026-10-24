using System.Text;
using Atria.Audio;
using Atria.Resources;

namespace Atria.Tests.Audio;

public static class SoundEffectTests
{
    public static string Run(string resourceRoot)
    {
        static void Check(bool result, string text)
        {
            if (!result)
            {
                throw new InvalidOperationException(text);
            }
        }

        Check(SoundEffectBank.UiAttenuation.SequenceEqual(new int[] { -10000, -1500, -1000, -500, 0 }), "Native five-entry UI attenuation table.");
        var voices = Enumerable.Range(0, 8).Select(_ => new Voice()).ToArray();
        using var rotation = new SoundEffectRotation(voices);
        rotation.SetCurrentVolume(-600);
        Check(Math.Abs(voices[0].Volume - 0.5011872f) < 0.000001f && voices[1].Volume == 1, "-6dB applies only to current voice, using amplitude not power.");
        for (int i = 0; i < 8; i++)
        {
            rotation.Play();
        }

        Check(rotation.Cursor == 0 && voices.All(v => v.Playing && v.Starts == 1), "Eight simultaneous voices and rotation wrap.");
        rotation.Play();
        Check(rotation.Cursor == 1 && voices[0].Starts == 1, "Ninth play continues first voice without restart.");
        voices[1].Playing = false;
        rotation.Play();
        Check(voices[1].Starts == 2 && rotation.Cursor == 2, "Naturally completed voice can replay.");
        rotation.SetCurrentVolume(-10000);
        Check(voices[2].Volume == 0 && voices[0].Volume > 0, "Cursor-specific silence.");
        rotation.StopAll();
        Check(rotation.Cursor == 2 && voices.All(v => !v.Playing && v.Resets == 1), "Stop resets all playback positions but preserves rotation cursor.");
        rotation.Play();
        Check(voices[2].Starts == 2 && rotation.Cursor == 3, "Stop/reset next playback uses existing cursor.");
        try
        {
            SoundEffectRotation.Amplitude(1);
            throw new InvalidOperationException("Amplification accepted.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        using var archive = BinderArchive.Open(Path.Combine(resourceRoot, "at2wav.000"));
        int count = 0;
        foreach (string member in SoundEffectBank.UiMemberNames)
        {
            int index = archive.FindResource(Encoding.ASCII.GetBytes(member));
            Check(index >= 0, $"Missing native UI effect {member}.");
            var pcm = PcmWave.Parse(archive.ReadMember(index));
            Check(pcm.Info.FrameCount > 0 && pcm.Samples.Length == pcm.Info.DataLength, "Real UI PCM has complete frames.");
            count++;
        }

        Check(count == 14, "Native fourteen-effect registry.");
        rotation.Dispose();
        Check(voices.All(v => v.Disposals == 1), "All voices disposed once.");
        return "Sound effects: native14-member PCM registry, eight-voice rotation, overlap/no-restart, current-slot attenuation and stop/reset tests passed.";
    }

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
        {
            throw new InvalidOperationException($"Expected eight queued effect voices; got {overlapping}.");
        }

        effect.Play();
        if (effect.Cursor != 1)
        {
            throw new InvalidOperationException("Native effect cursor wrap failed.");
        }

        effect.StopAll();
        if (effect.Cursor != 1)
        {
            throw new InvalidOperationException("Stop unexpectedly reset voice cursor.");
        }
        // Allow XAudio2's engine processing pass to apply Stop/Flush before querying its state.
        Thread.Sleep(50);
        if (effect.QueuedVoiceCount != 0)
        {
            throw new InvalidOperationException("Stop/reset did not drain voices.");
        }

        effect.Play();
        double seconds = effect.Format.DurationSeconds;
        if (seconds > 10)
        {
            throw new InvalidOperationException("Unexpectedly long UI probe effect.");
        }

        var deadline = DateTime.UtcNow.AddSeconds(seconds + 2);
        while (effect.QueuedVoiceCount != 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        if (effect.QueuedVoiceCount != 0)
        {
            throw new InvalidOperationException("Effect failed to finish.");
        }

        return $"Native muted PCM effects: eight overlaps, cursor wrap, no-restart reuse, stop/flush and natural completion passed ({seconds:F3}s asset).";
    }

    private sealed class Voice : ISoundEffectVoice
    {
        public bool Playing;
        public int Starts, Resets, Disposals;
        public float Volume = 1;
        public void PlayIfIdle()
        {
            if (!Playing)
            {
                Playing = true;
                Starts++;
            }
        }

        public void StopAndReset()
        {
            Playing = false;
            Resets++;
        }

        public void SetVolume(float amplitude) => Volume = amplitude;
        public void Dispose() => Disposals++;
    }
}
