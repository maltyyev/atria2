using Atria.Audio;

namespace Atria.Tests.Audio;

public static class MusicTests
{
    public static string Run()
    {
        var transport = new TraceTransport();
        var controller = new MusicController(transport);
        controller.Update(1); controller.Update(2); controller.Update(1);
        Equal(transport, "play1"); // Every other call is ignored, current playing track retained.
        controller.Update(2); controller.Update(2);
        Equal(transport, "play1,stop,play2");
        transport.State = MusicPlaybackState.Stopped;
        controller.Update(2); controller.Update(2);
        Equal(transport, "play1,stop,play2,play2"); // Explicit restart at end, no loop submitted to XAudio2.
        controller.Update(0); controller.Update(0);
        if (controller.CachedTrack != 2) throw new Exception("Stop must preserve music cache.");
        Equal(transport, "play1,stop,play2,play2,stop");
        transport.State = MusicPlaybackState.Unavailable;
        controller.Update(3); controller.Update(3);
        Equal(transport, "play1,stop,play2,play2,stop");
        if (MusicController.TownTrack(0) != 1 || MusicController.TownTrack(31) != 14 || MusicController.TownTrack(51) != 1)
            throw new Exception("Town soundtrack table mismatch.");
        try { MusicController.TownTrack(52); throw new Exception("Unbounded town soundtrack access."); }
        catch (ArgumentOutOfRangeException) { }
        return "Music: alternate-call gating, cache, track changes, stopped restart and town table bounds passed; audibility remains separate.";
    }
    private static void Equal(TraceTransport transport, string expected)
    { if (string.Join(',', transport.Calls) != expected) throw new Exception("Music command order differs from recovered native flow."); }
    private sealed class TraceTransport : IMusicTransport
    {
        public MusicPlaybackState State { get; set; } = MusicPlaybackState.Stopped;
        public List<string> Calls { get; } = [];
        public void Stop() { Calls.Add("stop"); State = MusicPlaybackState.Stopped; }
        public void Play(int logicalTrack) { Calls.Add($"play{logicalTrack}"); State = MusicPlaybackState.Playing; }
    }
}
