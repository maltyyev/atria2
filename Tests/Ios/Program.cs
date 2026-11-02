using Atria.Core.Contracts.Audio;
using Atria.Core.Resources.Audio;
using Atria.Core.Runtime.Audio;
using Atria.Core.Contracts.Host;
using Atria.Core.Game;
using Atria.Core.Runtime;
using Atria.Core.Persistence.Controls;
using Atria.Core.Presentation.Controls;
using Atria.Core.Runtime.Input;
using Atria.Hosts.Ios;
using Atria.Platforms.Ios;
using Tests.Ios;
using Atria.Core.Resources.Localization;
using Atria.Core.Contracts.Input;
using Atria.Core.Contracts.Presentation;
using Atria.Core.Presentation.UI;
using Atria.Core.Resources;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
}

var window = new MobileWindow();
var down = new PadSample(true, (ushort)(GamepadButtons.A | GamepadButtons.LeftThumb | GamepadButtons.RightThumb), 12, 34, 123, -456, 789, -1024);
var up = down with { Buttons = 0 };
window.SubmitPad(1, down);
window.SubmitPad(1, up);
Check(window.Read(1) == down && window.Read(1) == up, "Short controller edge or full sample was lost.");
window.SubmitPad(0, down);
window.SubmitPad(1, up);
Check(window.Read(0) == down && window.Read(1) == up, "Controller slots were merged.");
var secondButton = down with { Buttons = (ushort)GamepadButtons.B };
var disconnected = new PadSample(false, 0, 0, 0, 0, 0, 0, 0);
window.SubmitPad(1, down);
window.SubmitPad(0, secondButton);
window.SubmitPad(0, disconnected);
Check(window.Read(0) == secondButton && window.Read(1) == down && window.Read(0) == disconnected,
    "Interleaved controller events were consumed by another slot.");
long activitySequence = window.LastInputActivity.Sequence;
var touchUp = new PadSample(false, 0, 0, 0, 0, 0, 0, 0);
window.SubmitVirtualPad(down);
window.SubmitVirtualPad(touchUp);
Check(window.ReadVirtualGamepad() == (down with { Connected = true }) &&
      window.ReadVirtualGamepad() == touchUp, "Virtual controls occupied a physical slot or lost a short edge.");
Check(window.LastInputActivity.Sequence == activitySequence, "Virtual state resubmission invented a touch contact.");
window.SubmitVirtualPad(touchUp);
Check(window.LastInputActivity.Sequence == activitySequence, "A virtual-pad release changed the active source.");
window.SubmitTouchActivity(0);
Check(window.LastInputActivity.Sequence > activitySequence && window.LastInputActivity.Kind == InputSourceKind.Touch,
    "A direct touch without a pad-state change was ignored.");
window.SubmitPad(0, down);
window.SetSurface(1280, 720, false);
window.Pump();
Check(!window.Read(0).Connected && !window.Read(1).Connected && !window.ReadVirtualGamepad().Connected &&
      window.TryDequeueScanInput(out var reset) && reset.Kind == ScanInputEventKind.Reset,
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
window.SubmitPad(0, down);
Check(window.Read(0) == down, "Physical controller state did not reach its slot.");
long beforeSuspend = window.LastInputActivity.Sequence;
window.SetSurface(1280, 720, false);
window.SetSurface(1280, 720, true);
window.Pump();
Check(!window.Focused && !window.ScanKeys[28], "Suspend/resume between pumps lost the focus reset.");
window.Pump();
Check(window.Focused, "Surface did not resume after the focus reset.");
window.SubmitPad(0, down);
Check(window.LastInputActivity.Sequence == beforeSuspend && window.Read(0) == down,
    "A held controller state after resume was not resynchronized without inventing activity.");
window.SubmitPad(0, down with { Buttons = 0, LeftTrigger = 0, RightTrigger = 0, LX = 0, LY = 0, RX = 0, RY = 0 });
window.SubmitPad(0, down);
Check(window.Read(0).Buttons == 0 && window.Read(0) == down,
    "Controller input did not rearm after returning to neutral.");

var fastHost = new MobileWindow();
var neutralPad = new PadSample(true, 0, 0, 0, 0, 0, 0, 0);
fastHost.SubmitPad(0, neutralPad);
_ = fastHost.Read(0);
for (short axis = 1; axis <= 1000; axis++)
    fastHost.SubmitPad(0, neutralPad with { LX = axis });
Check(fastHost.Read(0).LX == 1000, "Fast host stick sampling accumulated stale analog frames.");
fastHost.SubmitPad(0, down);
for (short axis = 1; axis <= 1000; axis++)
    fastHost.SubmitPad(0, down with { LX = axis });
fastHost.SubmitPad(0, neutralPad);
Check(fastHost.Read(0).Buttons == down.Buttons && fastHost.Read(0).LX == 1000 && fastHost.Read(0).Buttons == 0,
    "Analog coalescing lost a short digital press/release pair.");
for (byte trigger = 1; trigger < 255; trigger++)
    fastHost.SubmitPad(0, neutralPad with { LeftTrigger = trigger });
for (int trigger = 253; trigger >= 0; trigger--)
    fastHost.SubmitPad(0, neutralPad with { LeftTrigger = (byte)trigger });
Check(fastHost.Read(0).LeftTrigger == 254 && fastHost.Read(0).LeftTrigger == 0,
    "Analog coalescing lost a short trigger threshold crossing.");

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
        new GameHostServices(assets, sessionWindow, (_, _, _, _) => default, new SmokeTextRasterizer())));
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
                    sessionWindow.SubmitPad(0, down);
                    sessionWindow.SubmitPad(0, up);
                }
                else if (menuSeen && width == 640) { gameSeen = true; break; }
                else if (menuSeen && !confirmed && timeout.Elapsed.TotalSeconds - menuTime > 0.5)
                {
                    sessionWindow.SubmitPad(0, down);
                    sessionWindow.SubmitPad(0, up);
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

var controlBindings = ControlBindings.Parse(new byte[64]);
var staleTouchWindow = new MobileWindow();
using (var staleTouchRouter = new InputRouter(ControlProfile.Defaults(controlBindings), controlBindings, staleTouchWindow))
{
    staleTouchWindow.SubmitTouchActivity(0);
    staleTouchWindow.SetSurface(1280, 720, false);
    staleTouchWindow.Pump();
    staleTouchRouter.Poll(staleTouchWindow);
    staleTouchWindow.SetSurface(1280, 720, true);
    staleTouchWindow.Pump();
    staleTouchRouter.Poll(staleTouchWindow);
    Check(staleTouchRouter.CurrentInputSource.Kind == InputSourceKind.Keyboard,
        "A touch queued before focus loss selected its source after resume.");
    staleTouchWindow.SubmitTouchActivity(0);
    staleTouchRouter.Poll(staleTouchWindow);
    Check(staleTouchRouter.CurrentInputSource.Kind == InputSourceKind.Touch,
        "Fresh touch activity after resume was not recognized.");
}
var resumeWindow = new MobileWindow();
using (var resumeRouter = new InputRouter(ControlProfile.Defaults(controlBindings), controlBindings, resumeWindow))
{
    void PollResume(long now) => resumeRouter.Poll(resumeWindow.ScanKeys, resumeWindow.ScanPressed,
        [false, false, false], resumeWindow.Focused, now);
    resumeWindow.SubmitPad(0, down);
    PollResume(0);
    resumeWindow.SubmitKey(36, true);
    resumeWindow.Pump();
    PollResume(1);
    Check(resumeRouter.CurrentInputSource.Kind == InputSourceKind.Keyboard, "Keyboard did not select the pre-suspend source.");
    resumeWindow.SetSurface(1280, 720, false);
    resumeWindow.Pump();
    PollResume(2);
    resumeWindow.SetSurface(1280, 720, true);
    resumeWindow.Pump();
    resumeWindow.SubmitPad(0, down);
    PollResume(1600);
    Check(resumeRouter.CurrentInputSource.Kind == InputSourceKind.Keyboard,
        "A held physical pad stole the active source after real MobileWindow focus reset.");
    resumeWindow.SubmitPad(0, neutralPad);
    PollResume(1601);
    resumeWindow.SubmitPad(0, down);
    PollResume(1602);
    Check(resumeRouter.CurrentInputSource.Kind == InputSourceKind.Gamepad,
        "A fresh physical press after resume did not select its source.");
}
using (var router = new InputRouter(ControlProfile.Defaults(controlBindings), controlBindings, window))
{
    int sourceChanges = 0;
    router.InputSourceChanged += _ => sourceChanges++;
    void PollAt(long now) => router.Poll(window.ScanKeys, window.ScanPressed,
        [window.LeftButton, window.RightButton, window.MiddleButton], window.Focused, now,
        window.MousePressed, window.ReadVirtualGamepad());
    window.SubmitTouchActivity(0);
    window.SubmitVirtualPad(new PadSample(false, (ushort)GamepadButtons.X, 0, 0, 0, 0, 0, 0));
    router.Poll(window);
    Check(router.CurrentInputSource is { Kind: InputSourceKind.Touch, Identity: 0 } &&
          router.CombatInput.Press(0, GameAction.Attack), "Touch did not reach the router through its independent virtual source.");
    Check(router.Hint(0, GameAction.Attack) == router.Profile.Players[0].Bindings[(int)GameAction.Attack][2].ToString(),
        "Touch activity did not select pad hints.");
    int touchChanges = sourceChanges;
    window.SubmitVirtualPad(touchUp);
    router.Poll(window);
    window.SubmitTouchActivity(0);
    router.Poll(window);
    Check(sourceChanges == touchChanges, "Touch release or repeated activity changed the source without a device change.");

    window.SubmitPad(1, new PadSample(true, 0, 0, 0, 0, 0, 0, 0));
    router.Poll(window);
    PollAt(Environment.TickCount64 + 2000);
    window.SubmitPad(1, new PadSample(true, (ushort)GamepadButtons.X, 0, 0, 0, 0, 0, 0));
    PollAt(Environment.TickCount64 + 2001);
    Check(router.CurrentInputSource is { Kind: InputSourceKind.Gamepad, Identity: 1 } && router.LastWasPad && sourceChanges == touchChanges + 1,
        "Physical slot activity did not switch the real router independently of touch.");
    Console.WriteLine("Mobile input routing PASS: real window touch source, physical identity and live action hint.");
}

var lifecycle = new MobileSession();
Check(lifecycle.Start((gameWindow, _) => { while (!gameWindow.Closed) Thread.Sleep(1); }) == 1, "Session did not start.");
Check(lifecycle.Start((_, _) => { }) == 0, "A second game thread started while the first was active.");
lifecycle.RequestStop();
Check(lifecycle.WaitStopped(TimeSpan.FromSeconds(2)) == 0 && lifecycle.Closed, "Stopped session did not join and release.");
Check(lifecycle.Start((gameWindow, _) => { while (!gameWindow.Closed) Thread.Sleep(1); }) == 1, "Stopped session could not restart.");
lifecycle.RequestStop();
Check(lifecycle.WaitStopped(TimeSpan.FromSeconds(2)) == 0, "Restarted session did not stop.");
Check(lifecycle.Start((_, _) => throw new InvalidOperationException("lifecycle check")) == 1, "Failure case did not start.");
Check(lifecycle.WaitStopped(TimeSpan.FromSeconds(2)) == 0 && lifecycle.Error.Contains("lifecycle check"), "Worker failure escaped lifecycle reporting.");
Console.WriteLine("Mobile session lifecycle PASS: single owner, stop/join, restart and captured worker failure.");

unsafe
{
    var previousOut = Console.Out;
    var previousError = Console.Error;
    string failureOutput = Path.Combine(Path.GetTempPath(), "atria-ios-failed-start-" + Guid.NewGuid().ToString("N"));
    byte[] missingResources = System.Text.Encoding.UTF8.GetBytes(Path.Combine(failureOutput, "missing-assets") + '\0');
    byte[] output = System.Text.Encoding.UTF8.GetBytes(failureOutput + '\0');
    delegate* unmanaged<byte*, byte*, int> start = &GameExports.Start;
    delegate* unmanaged<int> wait = &GameExports.WaitStopped;
    fixed (byte* resources = missingResources, destination = output)
        Check(start(resources, destination) == 1, "Failed-start session did not acquire its worker.");
    Check(wait() == 0 && File.Exists(Path.Combine(failureOutput, "ios-error.log")), "Startup failure was not captured.");
    Check(ReferenceEquals(Console.Out, previousOut) && ReferenceEquals(Console.Error, previousError),
        "Startup failure did not restore console writers before disposing its session log.");
    Console.WriteLine("Mobile failure logging PASS: captured startup failure and restored console writers.");
}

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
