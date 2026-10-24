using Atria.Audio;

namespace Atria.Tests.Audio;

public static class MusicTests
{
    public static string Run()
    {
        CheckCatalog();
        var transport = new TraceTransport();
        var controller = new MusicController(transport);
        controller.Update(1);
        controller.Update(2);
        controller.Update(1);
        Equal(transport, "play1"); // Every other call is ignored, current playing track retained.
        controller.Update(2);
        controller.Update(2);
        Equal(transport, "play1,stop,play2");
        transport.State = MusicPlaybackState.Stopped;
        controller.Update(2);
        controller.Update(2);
        Equal(transport, "play1,stop,play2,play2"); // Explicit restart at end, no loop submitted to XAudio2.
        controller.Update(0);
        controller.Update(0);
        if (controller.CachedTrack != 2)
        {
            throw new Exception("Stop must preserve music cache.");
        }

        Equal(transport, "play1,stop,play2,play2,stop");
        transport.State = MusicPlaybackState.Unavailable;
        controller.Update(3);
        controller.Update(3);
        Equal(transport, "play1,stop,play2,play2,stop");
        if (Atria.Game.Town.TownCatalog.Get(0).MusicTrack != 1 || Atria.Game.Town.TownCatalog.Get(31).MusicTrack != 14 || Atria.Game.Town.TownCatalog.Get(51).MusicTrack != 1)
        {
            throw new Exception("Town soundtrack table mismatch.");
        }

        try
        {
            _ = Atria.Game.Town.TownCatalog.Get(52);
            throw new Exception("Unbounded town soundtrack access.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        controller.RestoreRuntime(new MusicControllerSnapshot(false, 40));
        transport.State = MusicPlaybackState.Stopped;
        controller.Update(40);
        if (transport.Calls[^1] != "play40")
        {
            throw new Exception("Music controller retained a fixed soundtrack limit.");
        }

        return "Music: sparse file catalog, duplicate/missing IDs, extended IDs, alternate-call gating, cache, restart and town table bounds passed; audibility remains separate.";
    }

    private static void CheckCatalog()
    {
        string directory = Path.Combine(Path.GetTempPath(), "atria-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (string name in new[] { "Track01.wav", "Track02.wav", "Track04.WAV", "track41.wav", "ambient.wav", "Track05.mp3" })
            {
                File.WriteAllBytes(Path.Combine(directory, name), []);
            }

            var catalog = new MusicCatalog(directory);
            if (catalog.Count != 3 || Path.GetFileName(catalog.GetPath(1)) != "Track02.wav"
                || Path.GetFileName(catalog.GetPath(3)) != "Track04.WAV"
                || Path.GetFileName(catalog.GetPath(40)) != "track41.wav")
            {
                throw new Exception("CD track mapping or filename filtering changed.");
            }

            try
            {
                catalog.GetPath(2);
                throw new Exception("A gap in the music catalog was accepted.");
            }
            catch (FileNotFoundException)
            {
            }

            File.WriteAllBytes(Path.Combine(directory, "Track002.wav"), []);
            try
            {
                _ = new MusicCatalog(directory);
                throw new Exception("Ambiguous logical music ID was accepted.");
            }
            catch (InvalidDataException)
            {
            }
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                File.Delete(file);
            }

            Directory.Delete(directory);
        }
    }

    private static void Equal(TraceTransport transport, string expected)
    {
        if (string.Join(',', transport.Calls) != expected)
        {
            throw new Exception("Music command order differs from recovered native flow.");
        }
    }

    private sealed class TraceTransport : IMusicTransport
    {
        public MusicPlaybackState State { get; set; } = MusicPlaybackState.Stopped;
        public List<string> Calls { get; } = [];

        public void Stop()
        {
            Calls.Add("stop");
            State = MusicPlaybackState.Stopped;
        }

        public void Play(int logicalTrack)
        {
            Calls.Add($"play{logicalTrack}");
            State = MusicPlaybackState.Playing;
        }
    }
}
