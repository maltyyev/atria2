using Atria.Core.Contracts.Audio;
using Atria.Core.Resources;
using Atria.Core.Runtime.Audio;

namespace Tests.Core.Audio;

public static class SessionExclusiveSoundChannelTests
{
    /// <summary>Checks owned and borrowed sound lifetime against the real exclusive WAV archive.</summary>
    public static string Run(string root)
    {
        var audio = new RecordingAudio();
        var channel = new SessionExclusiveSoundChannel(root, audio);
        var first = new RecordedSound();
        audio.Enqueue(first);
        byte[] firstWave = [1, 2, 3, 4];
        channel.PlayWave(firstWave, -500);

        var borrowed = new RecordedSound();
        channel.PlayBorrowed(borrowed, -1000);
        Check(first.Events.SequenceEqual(new[] { "volume:-500", "play", "stop", "dispose" }),
            "replacing an owned sound stops it before disposing it");
        Check(borrowed.Events.SequenceEqual(new[] { "volume:-1000", "play" }) && borrowed.Disposals == 0,
            "borrowed notice sound plays without channel disposal ownership");

        using (var archive = BinderArchive.Open(Path.Combine(root, "WAV.000")))
        {
            int member = archive.FindResource("300.wav"u8);
            Check(member >= 0, "test archive contains an exclusive member");
            byte[] expected = archive.ReadMember(member);
            var named = new RecordedSound();
            audio.Enqueue(named);
            channel.PlayMember("300.wav"u8, -250);
            Check(audio.LoadedWaves[^1].AsSpan().SequenceEqual(expected), "named playback loads the requested WAV.000 bytes");
            Check(borrowed.Events.SequenceEqual(new[] { "volume:-1000", "play", "stop" }) && borrowed.Disposals == 0,
                "replacing a borrowed sound stops it but leaves disposal to its owner");
            Check(named.Events.SequenceEqual(new[] { "volume:-250", "play" }), "named playback applies attenuation before starting");
        }

        channel.Stop();
        channel.Stop();
        Check(audio.LastCreated!.Events.SequenceEqual(new[] { "volume:-250", "play", "stop", "dispose" }),
            "stopping an owned sound releases it exactly once");

        var activeBorrowed = new RecordedSound();
        channel.PlayBorrowed(activeBorrowed, -200);
        audio.FailLoad = true;
        bool loadFailed = false;
        try
        {
            channel.PlayWave(new byte[] { 9 }, -300);
        }
        catch (IOException)
        {
            loadFailed = true;
        }

        audio.FailLoad = false;
        Check(loadFailed && activeBorrowed.Events.SequenceEqual(new[] { "volume:-200", "play" }),
            "failed loading leaves the current sound untouched");
        channel.Stop();
        Check(activeBorrowed.Events.SequenceEqual(new[] { "volume:-200", "play", "stop" }) && activeBorrowed.Disposals == 0,
            "borrowed sounds remain externally owned through stop");

        var failedOwned = new RecordedSound { FailPlay = true };
        audio.Enqueue(failedOwned);
        bool playFailed = false;
        try
        {
            channel.PlayWave(new byte[] { 5 }, -400);
        }
        catch (InvalidOperationException)
        {
            playFailed = true;
        }

        channel.Stop();
        Check(playFailed && failedOwned.Events.SequenceEqual(new[] { "volume:-400", "play", "stop", "dispose" }),
            "failed owned playback is released and cleared from the channel");

        var failedBorrowed = new RecordedSound { FailPlay = true };
        try
        {
            channel.PlayBorrowed(failedBorrowed, -600);
            throw new InvalidOperationException("Borrowed play failure was not surfaced.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "Injected play failure.")
        {
        }

        channel.Stop();
        Check(failedBorrowed.Events.SequenceEqual(new[] { "volume:-600", "play", "stop" }) && failedBorrowed.Disposals == 0,
            "failed borrowed playback is stopped but never disposed by the channel");

        var stopFailure = new RecordedSound { FailStop = true, FailDispose = true };
        audio.Enqueue(stopFailure);
        channel.PlayWave(new byte[] { 6 }, -700);
        bool stopFailed = false;
        try
        {
            channel.Stop();
        }
        catch (InvalidOperationException exception) when (exception.Message == "Injected stop failure.")
        {
            stopFailed = true;
        }

        channel.Stop();
        Check(stopFailed && stopFailure.Events.SequenceEqual(new[] { "volume:-700", "play", "stop", "dispose" })
            && stopFailure.Disposals == 1,
            "stop detaches before cleanup, disposes in order, and keeps its primary failure");

        return "Session audio channel: exact WAV loading, replacement order, owned cleanup, borrowed lifetime and failure behavior passed.";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Exclusive sound channel: " + message);
        }
    }

    private sealed class RecordingAudio : IGameAudio
    {
        private readonly Queue<RecordedSound> pending = new();
        public List<byte[]> LoadedWaves { get; } = [];
        public RecordedSound? LastCreated { get; private set; }
        public bool FailLoad { get; set; }

        public void Enqueue(RecordedSound sound) => pending.Enqueue(sound);

        public IBufferedSoundEffect LoadEffect(ReadOnlyMemory<byte> wave)
        {
            if (FailLoad)
            {
                throw new IOException("Injected load failure.");
            }

            LoadedWaves.Add(wave.ToArray());
            LastCreated = pending.Dequeue();
            return LastCreated;
        }
    }

    private sealed class RecordedSound : IBufferedSoundEffect
    {
        public List<string> Events { get; } = [];
        public int Disposals { get; private set; }
        public bool FailPlay { get; init; }
        public bool FailStop { get; init; }
        public bool FailDispose { get; init; }

        public void SetCurrentVolume(int value) => Events.Add("volume:" + value);

        public void Play()
        {
            Events.Add("play");
            if (FailPlay)
            {
                throw new InvalidOperationException("Injected play failure.");
            }
        }

        public void StopAll()
        {
            Events.Add("stop");
            if (FailStop)
            {
                throw new InvalidOperationException("Injected stop failure.");
            }
        }

        public void Dispose()
        {
            Events.Add("dispose");
            Disposals++;
            if (FailDispose)
            {
                throw new InvalidOperationException("Injected dispose failure.");
            }
        }
    }
}
