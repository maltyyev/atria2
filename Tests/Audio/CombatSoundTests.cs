using System.Globalization;
using System.Text;
using Atria.Audio;
using Atria.Resources;

namespace Atria.Tests.Audio;

public static class CombatSoundTests
{
    public static string Run(string root)
    {
        using var archive = BinderArchive.Open(Path.Combine(root, "WAV.000"));
        var created = new List<RecordedSound>();
        var exclusive = new List<string>();
        var byId = new Dictionary<int, RecordedSound>();
        int expected = 0;
        for (int i = 0; i < 50; i++)
        {
            if (archive.FindResource(Encoding.ASCII.GetBytes(i.ToString("D3", CultureInfo.InvariantCulture))) >= 0)
            {
                expected++;
            }
        }

        var bank = new CombatSoundBank(root, wave =>
        {
            var pcm = PcmWave.Parse(wave);
            Check(pcm.Samples.Length > 0, "Actual numeric effect must contain PCM frames.");
            var sound = new RecordedSound();
            created.Add(sound);
            return sound;
        }, wave =>
        {
            Check(PcmWave.Parse(wave).Samples.Length > 0, "Exclusive callback receives real PCM data.");
            exclusive.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(wave.Span)));
        });
        Check(bank.LoadedCount == expected && created.Count == expected, "Entry loads exactly the present numeric0..49 resources.");
        int cursor = 0;
        for (int id = 0; id < 50; id++)
        {
            if (archive.FindResource(Encoding.ASCII.GetBytes(id.ToString("D3", CultureInfo.InvariantCulture))) >= 0)
            {
                byId[id] = created[cursor++];
            }
        }

        int before = created.Count;
        foreach (int id in byId.Keys)
        {
            Check(bank.Load(id), "Existing cache entry remains available.");
        }

        Check(created.Count == before, "Repeated loading must retain the same eight-voice bank.");
        bank.Play(0, -500);
        Check(created.All(s => s.Events.Count == 0), "Sound0 is a playback sentinel even if its resource exists.");
        int playable = byId.Keys.First(id => id != 0);
        bank.Play(playable, -1500);
        bank.Play(playable, -500);
        Check(byId[playable].Events.SequenceEqual(new[] { "volume:-1500", "play", "volume:-500", "play" }), "Attenuation applies before each rotated playback.");
        bank.Play(300, 0);
        bank.Play(340, 0);
        string Hash(string name) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archive.ReadMember(archive.FindResource(Encoding.ASCII.GetBytes(name)))));
        Check(exclusive.SequenceEqual(new[] { Hash("300.wav"), Hash("340.wav") }), "Large sound IDs dispatch exact archive payloads to the session exclusive channel.");
        try
        {
            bank.Play(200, 0);
            throw new Exception("Missing exclusive WAV accepted.");
        }
        catch (FileNotFoundException e)
        {
            Check(e.Message.Contains("200.wav", StringComparison.Ordinal), "Missing resource diagnostic identifies requested sound.");
        }

        int absent = Enumerable.Range(1, 199).First(id => archive.FindResource(Encoding.ASCII.GetBytes(id.ToString("D3", CultureInfo.InvariantCulture))) < 0);
        Check(!bank.Load(absent), "Missing numeric resource is the original optional empty slot.");
        bank.Play(absent, 0);
        Check(created.Count == before && exclusive.Count == 2, "Empty slot cannot play or manufacture an exclusive request.");
        Reject(() => bank.Load(200));
        Reject(() => bank.Load(-1));
        Reject(() => bank.Play(-1, 0));
        foreach (int id in new[]
        {
            75,
            76,
            77,
            78
        }
        )
        {
            Check(bank.Load(id), "Actual intro INF sound must load from the same cache.");
        }

        bank.Dispose();
        bank.Dispose();
        Check(created.All(s => s.Stopped == 1 && s.Disposed == 1), "Each numeric bank must be stopped and disposed exactly once.");
        Check(exclusive.Count == 2, "Numeric disposal must not stop or replace the separate scenario channel.");
        var partial = new List<RecordedSound>();
        try
        {
            using var failed = new CombatSoundBank(root, _ =>
            {
                if (partial.Count == 3)
                {
                    throw new IOException("Injected voice allocation failure.");
                }

                var sound = new RecordedSound();
                partial.Add(sound);
                return sound;
            }, _ => throw new Exception("Unexpected exclusive sound."));
            throw new Exception("Expected initialization failure.");
        }
        catch (IOException)
        {
        }

        Check(partial.Count == 3 && partial.All(s => s.Stopped == 1 && s.Disposed == 1), "Partial entry initialization releases already-created banks.");
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
            Check(native.LoadedCount == expected, "Actual XAudio2 numeric bank initialization.");
            native.Play(playable, -10000);
            foreach (var entry in archive.Entries)
            {
                if (int.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id >= CombatSoundBank.Capacity)
                {
                    native.Play(id, -10000);
                }
            }
        }

        Check(exclusiveCount > 0, "Actual high-ID WAV coverage");
        return $"Combat audio: {expected} entry WAV banks and {exclusiveCount} exclusive WAV resources; optional slots, INF additions, exact payload dispatch, safe partial cleanup and native muted lifetime passed.";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new Exception("Expected bounded sound index rejection.");
    }

    private sealed class RecordedSound : IBufferedSoundEffect
    {
        public List<string> Events { get; } = [];

        public int Stopped, Disposed;
        public void SetCurrentVolume(int value) => Events.Add("volume:" + value.ToString(CultureInfo.InvariantCulture));
        public void Play() => Events.Add("play");
        public void StopAll() => Stopped++;
        public void Dispose() => Disposed++;
    }
}
