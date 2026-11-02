using System.Text;
using Atria.Core.Contracts.Audio;
using Atria.Core.Resources.Audio;
using Atria.Core.Runtime.Audio;
using Atria.Core.Resources;
using Tests.Core.Localization;
using Tests.Core.Persistence;

namespace Tests.Core.Audio;

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

        Check(SoundEffectRotation.UiAttenuation.SequenceEqual(new int[] { -10000, -1500, -1000, -500, 0 }), "Shared five-entry UI attenuation table.");
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
        foreach (string member in SoundEffectRotation.UiMemberNames)
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
