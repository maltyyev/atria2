using Atria.Core.Contracts.Audio;
using Atria.Core.Resources.Audio;
using Atria.Core.Runtime.Audio;
using Atria.Core.Contracts.Host;
using Atria.Core.Game;
using Atria.Core.Game.Combat;
using Atria.Core.Game.Progression;
using Atria.Core.Game.State;
using Atria.Core.Runtime;
using Atria.Core.Persistence;
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

if (args.Length == 1 && args[0] == "--mobile-handoff")
{
    Console.WriteLine(MobileInputChecks.RunContextHandoff());
    return;
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
window.SubmitTouchActivity(0);
Check(window.LastInputActivity.Sequence > activitySequence && window.LastInputActivity.Kind == InputSourceKind.Touch,
    "A direct touch without a pad-state change was ignored.");
window.SubmitPad(0, down);
window.SetSurface(1280, 720, false);
window.Pump();
Check(!window.Read(0).Connected && !window.Read(1).Connected &&
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
        window.MousePressed);
    window.SubmitTouchActivity(0);
    router.Poll(window);
    Check(router.CurrentInputSource is { Kind: InputSourceKind.Touch, Identity: 0 }, "Direct touch metadata did not select touch hints.");
    Check(router.ProfileMenuHint == GameText.Get("ui.controls.touch"), "Direct touch metadata exposed a physical gamepad hint.");
    int touchChanges = sourceChanges;
    router.Poll(window);
    window.SubmitTouchActivity(0);
    router.Poll(window);
    Check(sourceChanges == touchChanges, "Repeated touch metadata changed the source without a device change.");

    window.SubmitPad(1, new PadSample(true, 0, 0, 0, 0, 0, 0, 0));
    router.Poll(window);
    PollAt(Environment.TickCount64 + 2000);
    window.SubmitPad(1, new PadSample(true, (ushort)GamepadButtons.X, 0, 0, 0, 0, 0, 0));
    PollAt(Environment.TickCount64 + 2001);
    Check(router.CurrentInputSource is { Kind: InputSourceKind.Gamepad, Identity: 1 } && router.LastWasPad && sourceChanges == touchChanges + 1,
        "Physical slot activity did not switch the real router independently of touch.");
    Console.WriteLine("Mobile input routing PASS: real window touch source, physical identity and live action hint.");
}

static MobileTouchLabels Labels() => new("up", "down", "left", "right", "confirm", "back", "attack", "jump", "modifier", "language", "movement", "profiles");
static MobileUiSnapshot Snapshot(ulong context, string scene, MobileOverlayKind overlay) => new(context, context, scene, overlay, false, Labels());

var combatWindow = new MobileWindow();
combatWindow.PublishUiSnapshot(Snapshot(7, "combat", MobileOverlayKind.Combat));
var classicTouchProfile = ControlProfile.Defaults(controlBindings);
classicTouchProfile.Players[0].Classic = true;
classicTouchProfile.Players[0].Set(GameAction.Attack, InputBinding.Key(44));
using (var touchRouter = new InputRouter(classicTouchProfile, controlBindings, combatWindow))
{
    touchRouter.SetMobileContext(7, MobileOverlayKind.Combat);
    combatWindow.SubmitTouchState(new(7, 0, 0, MobileTouchButtons.X));
    touchRouter.Poll(combatWindow);
    Check(touchRouter.CombatInput.Press(0, CombatAction.Attack), "Touch X did not map directly to semantic attack under Classic/remapped controls.");
    Check(!combatWindow.Read(0).Connected && touchRouter.Capture(0, InputKind.Pad).Kind == InputKind.None,
        "Touch attack was exposed as a physical controller or binding capture.");
    Check(touchRouter.ProfileMenuHint == GameText.Get("ui.controls.touch"), "Touch activity exposed a physical gamepad profile hint.");

    combatWindow.SubmitTouchState(new(7, 0, 0, MobileTouchButtons.None));
    touchRouter.Poll(combatWindow);
    combatWindow.SubmitTouchState(new(7, 0, 0, MobileTouchButtons.A));
    touchRouter.Poll(combatWindow);
    Check(touchRouter.CombatInput.Press(0, CombatAction.Jump), "Touch A did not map directly to semantic jump.");

    combatWindow.SubmitTouchState(new(7, 0, 32767, MobileTouchButtons.B));
    touchRouter.Poll(combatWindow);
    touchRouter.Poll(combatWindow);
    Check(touchRouter.CombatInput.PressQuick(0, 0), "Touch B+Up did not invoke quick slot one.");
    Check(!touchRouter.CombatInput.Held(0, CombatAction.Up) && touchRouter.CombatInput.Run(0) is null,
        "Touch B modifier did not suppress movement and analog run.");
    (short X, short Y, int Slot)[] sectors = [(23170, 23170, 1), (23170, -23170, 2), (-23170, -23170, 3), (-23170, 23170, 0)];
    foreach (var sector in sectors)
    {
        combatWindow.SubmitTouchState(new(7, sector.X, sector.Y, MobileTouchButtons.B));
        touchRouter.Poll(combatWindow);
        Check(touchRouter.CombatInput.PressQuick(0, sector.Slot), $"Touch quick-slot sector boundary did not map slot {sector.Slot + 1}.");
    }

    combatWindow.SubmitTouchState(new(7, 32767, 0, MobileTouchButtons.None));
    touchRouter.Poll(combatWindow);
    Check(touchRouter.CombatInput.Held(0, CombatAction.Right) && touchRouter.CombatInput.Run(0) == true,
        "Touch stick did not provide semantic movement and run at high magnitude.");
    combatWindow.SubmitTouchState(new(7, 12000, 0, MobileTouchButtons.None));
    touchRouter.Poll(combatWindow);
    Check(touchRouter.CombatInput.Run(0) == false, "Touch stick did not provide walk at low magnitude.");
}

var navigationWindow = new MobileWindow();
navigationWindow.PublishUiSnapshot(Snapshot(8, "world", MobileOverlayKind.Navigation));
using (var navigationRouter = new InputRouter(ControlProfile.Defaults(controlBindings), controlBindings, navigationWindow))
{
    navigationRouter.SetMobileContext(8, MobileOverlayKind.Navigation);
    navigationWindow.SubmitTouchState(new(8, 0, 0, MobileTouchButtons.Up));
    navigationRouter.Poll(navigationWindow);
    navigationRouter.Poll(navigationWindow);
    Check(navigationRouter.WorldInput.Press(0, WorldAction.Up) && !navigationRouter.WorldInput.Press(0, WorldAction.Up),
        "Held navigation arrow repeated or created duplicate actions.");
    navigationRouter.Clear();
    navigationWindow.SubmitTouchState(new(8, 0, 0, MobileTouchButtons.None));
    navigationRouter.Poll(navigationWindow);
    navigationWindow.SubmitTouchState(new(8, 0, 0, MobileTouchButtons.Up));
    navigationRouter.Poll(navigationWindow);
    Check(navigationRouter.WorldInput.Press(0, WorldAction.Up), "Clear while held failed to rearm after physical finger release.");
    navigationWindow.SubmitTouchState(new(8, 0, 0, MobileTouchButtons.None));
    navigationRouter.Poll(navigationWindow);
    navigationRouter.Clear();
    navigationWindow.SubmitTouchState(new(8, 0, 0, MobileTouchButtons.Up));
    navigationRouter.Poll(navigationWindow);
    Check(navigationRouter.WorldInput.Press(0, WorldAction.Up), "Clear while already neutral dropped the next touch press.");

    navigationRouter.SetMobileContext(9, MobileOverlayKind.Navigation, directionsAreMenu: true);
    navigationWindow.PublishUiSnapshot(Snapshot(9, "status", MobileOverlayKind.Navigation));
    navigationWindow.SubmitTouchState(new(9, 0, 0, MobileTouchButtons.Down));
    navigationRouter.Poll(navigationWindow);
    navigationRouter.Poll(navigationWindow);
    Check(navigationRouter.MenuInput.Menu(MenuAction.Down) && !navigationRouter.WorldInput.Press(0, WorldAction.Down),
        "Status touch direction leaked into both menu and world input.");
}

var townWindow = new MobileWindow();
townWindow.PublishUiSnapshot(Snapshot(12, "town", MobileOverlayKind.Navigation));
using (var townRouter = new InputRouter(ControlProfile.Defaults(controlBindings), controlBindings, townWindow))
{
    townRouter.SetMobileContext(12, MobileOverlayKind.Navigation, directionsAreMenu: true);
    (MobileTouchButtons Button, bool Previous)[] townDirections =
    [
        (MobileTouchButtons.Left, true), (MobileTouchButtons.Up, true),
        (MobileTouchButtons.Right, false), (MobileTouchButtons.Down, false)
    ];
    foreach (var (button, previous) in townDirections)
    {
        townWindow.SubmitTouchState(new(12, 0, 0, button));
        townRouter.Poll(townWindow);
        bool moved = previous ? townRouter.TownInput.TakeMobilePrevious() : townRouter.TownInput.TakeMobileNext();
        Check(moved, $"Town touch direction {button} did not produce one selection step.");
        Check(!(previous ? townRouter.TownInput.TakeMobilePrevious() : townRouter.TownInput.TakeMobileNext()),
            $"Town touch direction {button} repeated without another press.");
        townWindow.SubmitTouchState(new(12, 0, 0, MobileTouchButtons.None));
        townRouter.Poll(townWindow);
    }
}

var townQuantityWindow = new MobileWindow();
townQuantityWindow.PublishUiSnapshot(Snapshot(13, "shop", MobileOverlayKind.Navigation));
using (var townQuantityRouter = new InputRouter(ControlProfile.Defaults(controlBindings), controlBindings, townQuantityWindow))
{
    townQuantityRouter.SetMobileContext(13, MobileOverlayKind.Navigation, directionsAreMenu: true);
    foreach ((MobileTouchButtons Button, int Step) direction in new[]
    {
        (MobileTouchButtons.Up, 1),
        (MobileTouchButtons.Down, -1)
    })
    {
        townQuantityWindow.SubmitTouchState(new(13, 0, 0, direction.Button));
        townQuantityRouter.Poll(townQuantityWindow);
        townQuantityRouter.Poll(townQuantityWindow);
        Check(townQuantityRouter.TownInput.TakeMobileQuantityAdjustment() == direction.Step
            && townQuantityRouter.TownInput.TakeMobileQuantityAdjustment() == 0,
            $"Shop touch {direction.Button} did not produce exactly one quantity step.");
        townQuantityWindow.SubmitTouchState(new(13, 0, 0, MobileTouchButtons.None));
        townQuantityRouter.Poll(townQuantityWindow);
        Check(townQuantityRouter.TownInput.TakeMobileQuantityAdjustment() == 0,
            $"Shop touch {direction.Button} repeated after release.");
    }
}

var focusTouchWindow = new MobileWindow();
focusTouchWindow.PublishUiSnapshot(Snapshot(10, "world", MobileOverlayKind.Navigation));
using (var focusTouchRouter = new InputRouter(ControlProfile.Defaults(controlBindings), controlBindings, focusTouchWindow))
{
    focusTouchRouter.SetMobileContext(10, MobileOverlayKind.Navigation);
    focusTouchWindow.SubmitTouchState(new(10, 0, 0, MobileTouchButtons.Up));
    focusTouchRouter.Poll(focusTouchWindow);
    Check(focusTouchRouter.WorldInput.Press(0, WorldAction.Up), "Focus test setup did not consume the first touch press.");
    focusTouchWindow.SetSurface(1280, 720, false);
    focusTouchWindow.Pump();
    focusTouchRouter.Poll(focusTouchWindow);
    focusTouchWindow.SetSurface(1280, 720, true);
    focusTouchWindow.Pump();
    focusTouchRouter.Poll(focusTouchWindow);
    focusTouchRouter.Poll(focusTouchWindow);
    focusTouchWindow.SubmitTouchState(new(10, 0, 0, MobileTouchButtons.Up));
    focusTouchRouter.Poll(focusTouchWindow);
    Check(focusTouchRouter.WorldInput.Press(0, WorldAction.Up), "Focus reset did not synthesize neutral and rearm fresh touch input.");
}
Console.WriteLine("Mobile semantic input PASS: independent touch actions, sector edges, focus reset and scene rearm.");

var battleModalWindow = new MobileWindow();
var battleModalRouter = new InputRouter(ControlProfile.Defaults(controlBindings), controlBindings, battleModalWindow);
using (battleModalRouter)
{
    var battleState = new GameState();
    foreach (PartyMember member in battleState.Heroes.Take(3))
    {
        member.MemberStatus = 1;
        member.SelectedForBattle = 1;
        member.Health = 10;
        member.Level = 1;
        member.NextExperience = ProgressionRules.Thresholds[0];
        member.Class = 0;
    }

    const ulong selectionContext = 20;
    battleModalWindow.PublishUiSnapshot(Snapshot(selectionContext, "combat.selection", MobileOverlayKind.CombatSelection));
    battleModalRouter.SetMobileContext(selectionContext, MobileOverlayKind.CombatSelection);
    var selection = new CombatPartySelection(battleState, 1);
    void TapSelection(MobileTouchButtons button)
    {
        battleModalWindow.SubmitTouchState(new(selectionContext, 0, 0, button));
        battleModalWindow.SubmitTouchState(new(selectionContext, 0, 0, MobileTouchButtons.None));
        battleModalRouter.Poll(battleModalWindow);
        battleModalRouter.Poll(battleModalWindow);
    }
    void ApplySelection() => selection.ApplyInput((player, action) =>
        player == 0 && battleModalRouter.CombatSelection.TakeCombatSelection(action), _ => { });

    TapSelection(MobileTouchButtons.Down);
    ApplySelection();
    Check(selection.Leaders[0] == 1, "Short touch Down did not reach the real party-selection consumer after repeated polls.");
    TapSelection(MobileTouchButtons.X);
    ApplySelection();
    Check(selection.Selected[1] == 0, "Touch X did not toggle the selected party member.");
    TapSelection(MobileTouchButtons.X);
    ApplySelection();
    Check(selection.Selected[1] == 1, "Touch X did not re-toggle the selected party member after release.");

    battleModalWindow.SubmitTouchState(new(selectionContext, 0, 0, MobileTouchButtons.A));
    battleModalRouter.Poll(battleModalWindow);
    battleModalRouter.Poll(battleModalWindow);
    ApplySelection();
    Check(selection.Completed, "Touch A did not start the real party-selection consumer.");

    const ulong combatContext = 21;
    battleModalRouter.SetMobileContext(combatContext, MobileOverlayKind.Combat);
    battleModalWindow.PublishUiSnapshot(Snapshot(combatContext, "combat", MobileOverlayKind.Combat));
    battleModalRouter.Poll(battleModalWindow);
    Check(!battleModalRouter.CombatInput.Press(0, CombatAction.Jump), "Held selection A leaked into combat after the scene transition.");
    Check(!battleModalRouter.CombatSelection.TakeCombatSelection(CombatSelectionInput.Toggle),
        "Selection X action remained available after battle start.");
    battleModalWindow.SubmitTouchState(new(combatContext, 0, 0, MobileTouchButtons.A));
    battleModalRouter.Poll(battleModalWindow);
    Check(battleModalRouter.CombatInput.Press(0, CombatAction.Jump), "Fresh combat A did not rearm after Core delivered the context-reset release.");

    battleModalWindow.SubmitTouchState(new(combatContext, 0, 0, MobileTouchButtons.None));
    battleModalRouter.Poll(battleModalWindow);
    const ulong experienceContext = 22;
    battleModalRouter.SetMobileContext(experienceContext, MobileOverlayKind.ExperienceAllocation);
    battleModalWindow.PublishUiSnapshot(Snapshot(experienceContext, "combat.experience", MobileOverlayKind.ExperienceAllocation));
    var experienceState = new GameState();
    foreach (PartyMember member in experienceState.Heroes.Take(3))
    {
        member.MemberStatus = 1;
        member.Level = 1;
        member.NextExperience = ProgressionRules.Thresholds[0];
        member.Class = 0;
    }

    var experience = new ExperienceAllocationSession(PartyCodec.Encode(experienceState.Party), 30);
    void TapExperience(MobileTouchButtons button)
    {
        battleModalWindow.SubmitTouchState(new(experienceContext, 0, 0, button));
        battleModalWindow.SubmitTouchState(new(experienceContext, 0, 0, MobileTouchButtons.None));
        battleModalRouter.Poll(battleModalWindow);
        battleModalRouter.Poll(battleModalWindow);
    }
    void ApplyExperience() => experience.ApplyInput(action => battleModalRouter.Experience.TakeExperience(action));

    TapExperience(MobileTouchButtons.A);
    ApplyExperience();
    Check(experience.Mode == 1 && !experience.Finished, "Short touch A did not cycle XP allocation mode without finishing it.");
    TapExperience(MobileTouchButtons.A);
    ApplyExperience();
    Check(experience.Mode == 2 && experience.Allocation[0] == 30, "Touch A did not select the all-to-one XP preset.");

    battleModalWindow.SubmitTouchState(new(experienceContext, 0, 0, MobileTouchButtons.Left));
    battleModalRouter.Poll(battleModalWindow);
    battleModalRouter.Poll(battleModalWindow);
    ApplyExperience();
    ApplyExperience();
    int[] redistributed = new int[experience.Allocation.Length];
    redistributed[0] = redistributed[1] = redistributed[2] = 10;
    Check(experience.Allocation.SequenceEqual(redistributed),
        "Held touch Left did not repeat XP redistribution like the gamepad transfer action.");
    battleModalWindow.SubmitTouchState(new(experienceContext, 0, 0, MobileTouchButtons.None));
    battleModalRouter.Poll(battleModalWindow);

    TapExperience(MobileTouchButtons.Up);
    ApplyExperience();
    Check(experience.SelectedMember == 2, "Touch Up did not move XP selection to the previous active member.");
    TapExperience(MobileTouchButtons.Down);
    ApplyExperience();
    Check(experience.SelectedMember == 0, "Touch Down did not move XP selection to the next active member.");
    TapExperience(MobileTouchButtons.B);
    ApplyExperience();
    Check(experience.Finished, "Short touch B did not confirm the real XP allocation consumer.");

    const ulong afterExperienceContext = 23;
    battleModalRouter.SetMobileContext(afterExperienceContext, MobileOverlayKind.Navigation);
    battleModalWindow.PublishUiSnapshot(Snapshot(afterExperienceContext, "world", MobileOverlayKind.Navigation));
    battleModalWindow.SubmitTouchState(new(afterExperienceContext, 0, 0, MobileTouchButtons.A));
    battleModalWindow.SubmitTouchState(new(afterExperienceContext, 0, 0, MobileTouchButtons.None));
    battleModalRouter.Poll(battleModalWindow);
    battleModalRouter.Poll(battleModalWindow);
    Check(!battleModalRouter.Experience.TakeExperience(ExperienceInput.CycleMode)
        && battleModalRouter.WorldInput.Press(0, WorldAction.Confirm),
        "XP preset A action leaked into the following navigation scene.");
}
Console.WriteLine("Mobile battle scenes PASS: selection and XP consumers, tap queues, held transfer, and phase rearm.");

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
