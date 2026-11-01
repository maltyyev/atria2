using Atria.Audio;
using Atria.Game;
using Atria.Ios;
using Atria.Localization;
using Atria.Platform.Input;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
}

var window = new MobileWindow();
var down = new PadSample(true, (ushort)GamepadButtons.A, 0, 0, 0, 0, 0, 0);
var up = down with { Buttons = 0 };
window.SubmitPad(down);
window.SubmitPad(up);
Check(window.Read(0) == down && window.Read(0) == up, "Short touch edge was lost.");
window.SubmitPad(down);
window.SetSurface(1280, 720, false);
window.Pump();
Check(window.Read(0).Buttons == 0 && window.TryDequeueScanInput(out var reset) && reset.Kind == ScanInputEventKind.Reset,
    "Background transition retained input.");
window.SetSurface(1280, 720, true);
window.Pump();
window.SubmitKey(28, true);
window.Pump();
Check(window.ScanKeys[28] && window.ScanPressed[28], "Key down missing.");
window.Pump();
Check(window.ScanKeys[28] && !window.ScanPressed[28], "Key edge repeated.");
window.SubmitKey(28, false);
window.Pump();
window.SubmitKey(28, true);
window.Pump();
window.SetSurface(1280, 720, false);
window.SetSurface(1280, 720, true);
window.Pump();
Check(!window.Focused && !window.ScanKeys[28], "Suspend/resume between pumps lost the focus reset.");
window.Pump();
Check(window.Focused, "Surface did not resume after the focus reset.");

using (var frames = new MobilePresenter())
{
    frames.SetFrameSize(2, 2);
    uint[] original = [1, 2, 3, 4];
    frames.Present(original);
    original[0] = 99;
    uint[] copy = new uint[4];
    Check(frames.CopyFrame(copy, out int width, out int height) > 0 && width == 2 && height == 2 && copy[0] == 1,
        "Presenter retained producer memory.");
    Check(frames.CopyFrame(new uint[3], out _, out _) == -1, "Presenter allowed a short target.");
    frames.SetFrameSize(3, 2);
    Check(frames.CopyFrame(copy, out _, out _) == 0, "Resize exposed old pixels with new dimensions.");
}

if (args.Length == 2)
{
    string assets = Path.GetFullPath(args[0]);
    string output = Path.GetFullPath(args[1]);
    Check(!Directory.Exists(output), "Host smoke test requires a fresh disposable output directory.");
    GameText.Use(new LocalizationCatalog(Path.Combine(assets, "Localization")));
    var sessionWindow = new MobileWindow();
    using var frames = new MobilePresenter();
    var task = Task.Run(() => GameSession.RunHosted(Path.Combine(assets, "Game"), output,
        sessionWindow, frames, new SilentTestAudio(), new SilentTestMusic(),
        new GameHostServices(assets, sessionWindow, (_, _, _, _) => default)));
    uint[] pixels = new uint[MobilePresenter.MaximumPixels];
    var timeout = System.Diagnostics.Stopwatch.StartNew();
    bool menuSeen = false, gameSeen = false, confirmed = false;
    double menuTime = 0;
    try
    {
        while (timeout.Elapsed.TotalSeconds < 20 && !task.IsCompleted)
        {
            if (frames.CopyFrame(pixels, out int width, out _) > 0)
            {
                if (width == 2560 && !menuSeen)
                {
                    menuSeen = true;
                    menuTime = timeout.Elapsed.TotalSeconds;
                    sessionWindow.SubmitPad(down);
                    sessionWindow.SubmitPad(up);
                }
                else if (menuSeen && width == 640) { gameSeen = true; break; }
                else if (menuSeen && !confirmed && timeout.Elapsed.TotalSeconds - menuTime > 0.5)
                {
                    sessionWindow.SubmitPad(down);
                    sessionWindow.SubmitPad(up);
                    confirmed = true;
                }
            }
            Thread.Sleep(10);
        }
    }
    finally { sessionWindow.RequestClose(); }
    Check(task.Wait(TimeSpan.FromSeconds(10)), "Hosted session did not stop.");
    task.GetAwaiter().GetResult();
    Check(menuSeen && gameSeen, "Shared new-game loop did not reach a gameplay/dialogue frame.");
    Console.WriteLine("Hosted GameSession PASS: real assets, menu -> new game, 640x480 frame, clean close. Test audio/movie devices are silent.");
}
Console.WriteLine("Mobile transport PASS: short taps, focus reset, key edges, frame ownership and resize.");

sealed class SilentTestAudio : IGameAudio
{
    public IBufferedSoundEffect LoadEffect(ReadOnlyMemory<byte> wave) => new Effect();
    private sealed class Effect : IBufferedSoundEffect
    {
        public void Play() { }
        public void StopAll() { }
        public void SetCurrentVolume(int volume) { }
        public void Dispose() { }
    }
}
sealed class SilentTestMusic : IFileMusicTransport
{
    public MusicPlaybackState State => MusicPlaybackState.Playing;
    public int CapturePlayingTrack() => 0;
    public void ValidateTrack(int track) { }
    public void SetVolume(float value) { }
    public void Play(int track) { }
    public void Stop() { }
}
