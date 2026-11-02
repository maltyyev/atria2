using System.Globalization;
using System.Text;
using Atria.Core.Resources;
using Atria.Core.Resources.Audio;
using Atria.Core.Runtime.Audio;
using Atria.Platforms.Windows.Audio;

namespace Tests.Windows.Audio;

/// <summary>Checks real XAudio2 sound-bank ownership and high-ID resource dispatch.</summary>
public static class CombatSoundProbeTests
{
    public static string Run(string root)
    {
        using var archive = BinderArchive.Open(Path.Combine(root, "WAV.000"));
        int expected = 0;
        int playable = -1;
        for (int i = 0; i < 50; i++)
        {
            if (archive.FindResource(Encoding.ASCII.GetBytes(i.ToString("D3", CultureInfo.InvariantCulture))) >= 0)
            {
                expected++;
                if (i != 0 && playable < 0)
                    playable = i;
            }
        }

        int exclusiveCount = 0;
        using (var device = new AudioDevice())
        using (var native = new CombatSoundBank(root, wave => device.LoadEffect(wave), wave =>
        {
            using var effect = device.LoadEffect(wave);
            effect.SetCurrentVolume(-10000);
            effect.Play();
            effect.StopAll();
            exclusiveCount++;
        }))
        {
            if (native.LoadedCount != expected)
                throw new InvalidOperationException("Actual XAudio2 numeric bank initialization differs from the resource index.");

            if (playable < 0)
                throw new InvalidOperationException("No playable numeric WAV resource was found.");

            native.Play(playable, -10000);
            foreach (var entry in archive.Entries)
            {
                if (int.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id >= CombatSoundBank.Capacity)
                    native.Play(id, -10000);
            }
        }

        if (exclusiveCount == 0)
            throw new InvalidOperationException("Actual high-ID WAV coverage was not exercised.");

        return $"Native combat audio: {expected} entry banks and {exclusiveCount} exclusive WAV resources passed.";
    }
}
