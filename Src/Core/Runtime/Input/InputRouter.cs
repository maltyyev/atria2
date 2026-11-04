using Atria.Core.Contracts.Input;
using Atria.Core.Contracts.Host;
using Atria.Core.Contracts.Presentation;
using Atria.Core.Contracts.Window;
using Atria.Core.Game.Menu;
using Atria.Core.Game.Progression;
using Atria.Core.Game.Town;
using Atria.Core.Presentation;
using Atria.Core.Resources;
using Atria.Core.Runtime.Timing;

namespace Atria.Core.Runtime.Input;

/// <summary>Device polling is independent of simulation. Each physical edge has one consumer.</summary>
public sealed class InputRouter : IDisposable
{
    private readonly IGamepadDevice pads;
    private const int KeyCount = 256;
    private const int MouseButtonCount = 3;
    private const int MouseTokenStart = KeyCount;
    private const int KeyboardMouseTokenEnd = MouseTokenStart + MouseButtonCount;
    private const int PadCount = 4;
    private const int PadTokenStart = 300;
    private const int PadTokenStride = 32;
    private const int DpadDirectionCount = 4;
    private const int QuickChordTokenStart = PadTokenStart + PadCount * PadTokenStride;
    private const int InputTokenCapacity = QuickChordTokenStart + PadCount * DpadDirectionCount;
    private const int DpadUp = 10;
    private const int DpadLeft = 13;
    private const int LeftTrigger = 14;
    private const int RightTrigger = 15;
    private const int LeftStickLeft = 16;
    private const int LeftStickRight = 17;
    private const int LeftStickUp = 18;
    private const int LeftStickDown = 19;
    private const int RightStickLeft = 20;
    private const int RightStickUp = 22;
    private const int PadControlCount = 24;
    private const int WheelUnitsPerNotch = 120;
    private const int ReconnectPollMilliseconds = 1500;
    private const int InitialRepeatMilliseconds = 350;
    private const int RepeatIntervalMilliseconds = 90;
    private const int AxisHysteresis = 1800;
    private const int TouchRunThreshold = 24576;
    private const ushort RumbleLowFrequencyStrength = 10000;
    private const ushort RumbleHighFrequencyStrength = 6000;
    private const int RumbleMilliseconds = 80;
    private static readonly GamepadButtons[] buttonMasks =
    [
        GamepadButtons.A, GamepadButtons.B, GamepadButtons.X, GamepadButtons.Y,
        GamepadButtons.LeftShoulder, GamepadButtons.RightShoulder,
        GamepadButtons.Back, GamepadButtons.Start, GamepadButtons.LeftThumb, GamepadButtons.RightThumb,
        GamepadButtons.DpadUp, GamepadButtons.DpadRight, GamepadButtons.DpadDown, GamepadButtons.DpadLeft
    ];
    private readonly ControlBindings legacy;
    private readonly bool[] held = new bool[InputTokenCapacity], previous = new bool[InputTokenCapacity], pending = new bool[InputTokenCapacity], blocked = new bool[InputTokenCapacity];
    private readonly long[] repeat = new long[InputTokenCapacity], nextProbe = new long[PadCount], rumbleEnd = new long[PadCount];
    private readonly PadSample[] samples = new PadSample[PadCount];
    private readonly PadSample[] axisActivityBaselines = new PadSample[PadCount];
    private readonly bool[] connectedBefore = new bool[PadCount];
    private bool wasFocused = true;
    private long externalActivitySequence;
    private int lastWindowMouseX = int.MinValue, lastWindowMouseY = int.MinValue;
    private (int Width, int Height) lastWindowSize;
    private bool hasActivityCandidate;
    private bool hasAuthoritativeActivity;
    private MobileTouchState mobileTouch;
    private MobileOverlayKind mobileOverlay;
    private ulong mobileContextId;
    private bool mobileDirectionsAreMenu;
    private MobileTouchButtons mobileBlockedButtons;
    private bool mobileAxisBlocked;
    private readonly int[] mobilePendingActions = new int[(int)GameAction.Count];
    private readonly int[] mobilePendingQuick = new int[4];
    private int mobileQuickSector = -1;
    private InputActivity activityCandidate;
    private readonly int[] contextVersions = new int[2];
    public int ContextVersion(int player) => contextVersions[player];
    /// <summary>Gets a non-allocating menu action view over the shared input state.</summary>
    public MenuInputView MenuInput => new(this);
    /// <summary>Gets a non-allocating world action view over the shared input state.</summary>
    public WorldInputView WorldInput => new(this);
    /// <summary>Gets a non-allocating town action view over the shared input state.</summary>
    public TownInputView TownInput => new(this);
    /// <summary>Gets a non-allocating combat action view over the shared input state.</summary>
    public CombatInput CombatInput => new(this);
    private int wheel;
    public int TakeWheel()
    {
        int steps = wheel / WheelUnitsPerNotch;
        wheel -= steps * WheelUnitsPerNotch;
        return steps;
    }

    public ControlProfile Profile
    {
        get; set;
    }
    public bool FocusPauseRequested
    {
        get; set;
    }
    /// <summary>Gets the source that most recently produced meaningful user activity.</summary>
    public InputActivity CurrentInputSource { get; private set; } = new(0, InputSourceKind.Keyboard, 0);
    /// <summary>Raised once when meaningful activity changes the active input source.</summary>
    public event Action<InputActivity>? InputSourceChanged;
    /// <summary>Compatibility view for older callers that only distinguish gamepads.</summary>
    public bool LastWasPad => CurrentInputSource.Kind == InputSourceKind.Gamepad;
    /// <summary>Gets the profile-menu binding appropriate to the latest active source.</summary>
    public string ProfileMenuHint => CurrentInputSource.Kind switch
    {
        InputSourceKind.Gamepad => ControlNames.Binding(InputBinding.Pad(3)),
        InputSourceKind.Touch => GameText.Get("ui.controls.touch"),
        _ => ControlNames.Binding(InputBinding.Key(0x3c))
    };
    /// <summary>Gets the profile rename binding appropriate to the latest active source.</summary>
    public string ProfileRenameHint => CurrentInputSource.Kind switch
    {
        InputSourceKind.Gamepad => ControlNames.Binding(InputBinding.Pad(2)),
        InputSourceKind.Touch => GameText.Get("ui.controls.touch"),
        _ => ControlNames.Binding(InputBinding.Key(0x3d))
    };
    public bool InBattle
    {
        get; set;
    }
    public int MouseX { get; private set; } = -1;
    public int MouseY { get; private set; } = -1;
    public string Notice { get; set; } = "";
    public long Now
    {
        get; private set;
    }

    public InputRouter(ControlProfile profile, ControlBindings legacy, IGamepadDevice pads)
    {
        Profile = profile;
        this.legacy = legacy;
        this.pads = pads;
    }

    /// <summary>Sets the Core-selected mobile scene context and its semantic touch layout.</summary>
    public void SetMobileContext(ulong contextId, MobileOverlayKind overlay, bool directionsAreMenu = false)
    {
        if (mobileContextId == contextId && mobileOverlay == overlay && mobileDirectionsAreMenu == directionsAreMenu) return;
        mobileContextId = contextId;
        mobileOverlay = overlay;
        mobileDirectionsAreMenu = directionsAreMenu;
        ResetMobileTouch(blockHeld: false);
    }

    private void ResetMobileTouch(bool blockHeld)
    {
        if (blockHeld)
        {
            mobileBlockedButtons |= mobileTouch.Buttons;
            mobileAxisBlocked |= mobileTouch.LeftX != 0 || mobileTouch.LeftY != 0;
        }
        else
        {
            mobileTouch = new(mobileContextId, 0, 0, MobileTouchButtons.None);
            mobileBlockedButtons = MobileTouchButtons.None;
            mobileAxisBlocked = false;
        }
        mobileQuickSector = -1;
        Array.Clear(mobilePendingActions);
        Array.Clear(mobilePendingQuick);
    }

    private void ApplyMobileTouchState(MobileTouchState state)
    {
        if (state.ContextId != mobileContextId) return;
        mobileBlockedButtons &= state.Buttons;
        if (state.LeftX == 0 && state.LeftY == 0) mobileAxisBlocked = false;

        MobileTouchButtons pressed = state.Buttons & ~mobileTouch.Buttons;
        if (mobileOverlay == MobileOverlayKind.Navigation)
        {
            QueueNavigationDirections(pressed, mobileDirectionsAreMenu);
            if ((pressed & MobileTouchButtons.A) != 0) QueueMobileAction(GameAction.Confirm);
            if ((pressed & MobileTouchButtons.B) != 0) QueueMobileAction(GameAction.Back);
        }
        else if (mobileOverlay == MobileOverlayKind.Combat)
        {
            if ((pressed & MobileTouchButtons.X) != 0) QueueMobileAction(GameAction.Attack);
            if ((pressed & MobileTouchButtons.A) != 0) QueueMobileAction(GameAction.Jump);
            int sector = (state.Buttons & MobileTouchButtons.B) != 0 && (mobileBlockedButtons & MobileTouchButtons.B) == 0
                ? TouchSector(state.LeftX, state.LeftY) : -1;
            if (sector >= 0 && sector != mobileQuickSector) mobilePendingQuick[sector] = Math.Min(255, mobilePendingQuick[sector] + 1);
            mobileQuickSector = sector;
        }

        bool axisMoved = Math.Abs((int)state.LeftX - mobileTouch.LeftX) >= AxisHysteresis && Math.Abs((int)state.LeftX) >= Profile.DeadZone
            || Math.Abs((int)state.LeftY - mobileTouch.LeftY) >= AxisHysteresis && Math.Abs((int)state.LeftY) >= Profile.DeadZone;
        if (pressed != MobileTouchButtons.None || axisMoved)
            RecordInputActivity(InputSourceKind.Touch, 0);
        mobileTouch = state;
    }

    private void QueueNavigationDirections(MobileTouchButtons pressed, bool menu)
    {
        QueueDirection(pressed, MobileTouchButtons.Left, menu ? GameAction.MenuLeft : GameAction.Left);
        QueueDirection(pressed, MobileTouchButtons.Right, menu ? GameAction.MenuRight : GameAction.Right);
        QueueDirection(pressed, MobileTouchButtons.Up, menu ? GameAction.MenuUp : GameAction.Up);
        QueueDirection(pressed, MobileTouchButtons.Down, menu ? GameAction.MenuDown : GameAction.Down);
    }

    private static bool IsNavigationAction(GameAction action) => action is
        GameAction.MenuLeft or GameAction.MenuRight or GameAction.MenuUp or GameAction.MenuDown or GameAction.Confirm or GameAction.Back;

    private void QueueDirection(MobileTouchButtons pressed, MobileTouchButtons button, GameAction action)
    {
        if ((pressed & button) == 0) return;
        QueueMobileAction(action);
    }

    private void QueueMobileAction(GameAction action)
    {
        ref int count = ref mobilePendingActions[(int)action];
        if (count < 255) count++;
    }

    private bool TakeMobileAction(GameAction action)
    {
        ref int count = ref mobilePendingActions[(int)action];
        if (count == 0) return false;
        count--;
        return true;
    }

    internal bool TakeMobileTownPrevious() => mobileOverlay == MobileOverlayKind.Navigation && mobileDirectionsAreMenu
        && (TakeMobileAction(GameAction.MenuLeft) || TakeMobileAction(GameAction.MenuUp));

    internal bool TakeMobileTownNext() => mobileOverlay == MobileOverlayKind.Navigation && mobileDirectionsAreMenu
        && (TakeMobileAction(GameAction.MenuRight) || TakeMobileAction(GameAction.MenuDown));

    private int TouchSector(short x, short y)
    {
        long lx = x, ly = y, deadZone = Profile.DeadZone;
        if (lx * lx + ly * ly < deadZone * deadZone) return -1;
        double degrees = Math.Atan2(x, y) * 180 / Math.PI;
        if (degrees < 0) degrees += 360;
        return degrees < 45 || degrees >= 315 ? 0 : degrees < 135 ? 1 : degrees < 225 ? 2 : 3;
    }

    private bool TouchMovement(GameAction action)
    {
        if (mobileAxisBlocked || mobileOverlay != MobileOverlayKind.Combat || (mobileTouch.Buttons & MobileTouchButtons.B) != 0) return false;
        return action switch
        {
            GameAction.Left => mobileTouch.LeftX < -Profile.DeadZone,
            GameAction.Right => mobileTouch.LeftX > Profile.DeadZone,
            GameAction.Up => mobileTouch.LeftY > Profile.DeadZone,
            GameAction.Down => mobileTouch.LeftY < -Profile.DeadZone,
            _ => false
        };
    }

    private bool TouchHeld(int player, GameAction action)
    {
        if (player != 0 || mobileOverlay != MobileOverlayKind.Combat) return false;
        MobileTouchButtons button = action switch
        {
            GameAction.Attack => MobileTouchButtons.X,
            GameAction.Jump => MobileTouchButtons.A,
            _ => MobileTouchButtons.None
        };
        return button != MobileTouchButtons.None && (mobileTouch.Buttons & button) != 0 && (mobileBlockedButtons & button) == 0;
    }

    private bool? TouchRun(int player)
    {
        if (player != 0 || mobileAxisBlocked || mobileOverlay != MobileOverlayKind.Combat ||
            (mobileTouch.Buttons & MobileTouchButtons.B) != 0 ||
            Math.Abs((int)mobileTouch.LeftX) < Profile.DeadZone && Math.Abs((int)mobileTouch.LeftY) < Profile.DeadZone)
            return null;
        long x = mobileTouch.LeftX, y = mobileTouch.LeftY;
        return x * x + y * y >= (long)TouchRunThreshold * TouchRunThreshold;
    }

    internal bool? Run(int player)
    {
        bool? physicalRun = Profile.Players[player].Classic ? null : Held(player, GameAction.Run);
        bool? touchRun = TouchRun(player);
        if (!touchRun.HasValue) return physicalRun;
        return touchRun.Value || physicalRun == true;
    }

    private bool TakeTouchQuick(int player, int slot)
    {
        if (player != 0 || mobileOverlay != MobileOverlayKind.Combat || mobilePendingQuick[slot] == 0) return false;
        mobilePendingQuick[slot]--;
        return true;
    }

    public bool Connected(int slot) => slot >= 0 && samples[slot].Connected;
    internal bool ProfileMenuPressed() => Take(0x3c) || PadButtonPressed(3); // F2 / physical Y
    internal bool PadButtonPressed(int button, bool repeat = false)
    {
        for (int slot = 0; slot < PadCount; slot++)
            if (Take(PadTokenStart + slot * PadTokenStride + button, repeat)) return true;
        return false;
    }
    /// <summary>Samples the shared window, physical devices, and activity source.</summary>
    public void Poll(IGameWindow window)
    {
        bool mouseMoved = false;
        if (window.Focused)
        {
            var clientSize = window.ClientSize;
            mouseMoved = lastWindowMouseX != int.MinValue && clientSize == lastWindowSize && (window.MouseX != lastWindowMouseX || window.MouseY != lastWindowMouseY)
                || window.MouseWheelDelta != 0;
            lastWindowMouseX = window.MouseX;
            lastWindowMouseY = window.MouseY;
            lastWindowSize = clientSize;
        }
        else
        {
            lastWindowMouseX = lastWindowMouseY = int.MinValue;
        }
        InputActivity externalActivity = window is IInputActivitySource activitySource ? activitySource.LastInputActivity : default;

        var size = window.ClientSize;
        var v = Viewport.Fit(size.Width, size.Height);
        if (v.TryMapInput(window.MouseX, window.MouseY, out int x, out int y))
        {
            MouseX = x;
            MouseY = y;
        }
        else
        {
            MouseX = MouseY = -1;
        }

        Poll(
            window.ScanKeys,
            window.ScanPressed,
            [window.LeftButton, window.RightButton, window.MiddleButton],
            window.Focused,
            Environment.TickCount64,
            window.MousePressed,
            mouseMoved,
            externalActivity,
            window as IMobileUiChannel
        );
        if (window.Focused)
        {
            wheel += window.MouseWheelDelta;
        }

    }

    /// <summary>Samples keyboard, mouse, and physical pads at one timestamp.</summary>
    public void Poll(
        ReadOnlySpan<bool> keys,
        ReadOnlySpan<bool> pressed,
        ReadOnlySpan<bool> mouse,
        bool focused,
        long now,
        ReadOnlySpan<bool> mousePressed = default
    ) => Poll(keys, pressed, mouse, focused, now, mousePressed, false, default, null);

    private void Poll(
        ReadOnlySpan<bool> keys,
        ReadOnlySpan<bool> pressed,
        ReadOnlySpan<bool> mouse,
        bool focused,
        long now,
        ReadOnlySpan<bool> mousePressed,
        bool mouseMoved,
        InputActivity externalActivity,
        IMobileUiChannel? mobileChannel)
    {
        hasActivityCandidate = false;
        hasAuthoritativeActivity = false;
        bool resumed = !wasFocused && focused;
        if (focused && mouseMoved) RecordInputActivity(InputSourceKind.Mouse, 0);
        if (externalActivity.Sequence > externalActivitySequence)
        {
            externalActivitySequence = externalActivity.Sequence;
            if (focused && !resumed)
            {
                activityCandidate = externalActivity;
                hasActivityCandidate = hasAuthoritativeActivity = true;
            }
        }
        Now = now;
        Array.Copy(held, previous, held.Length);
        Array.Clear(held);
        if (wasFocused && !focused)
        {
            FocusPauseRequested = true;
            Clear();
            StopVibration();
        }

        wasFocused = focused;
        if (focused)
        {
            for (int i = 0; i < KeyCount; i++)
            {
                held[i] = keys[i];
            }

            for (int i = 0; i < MouseButtonCount; i++)
            {
                held[MouseTokenStart + i] = mouse[i];
            }
        }

        for (int slot = 0; slot < PadCount; slot++)
        {
            if (now >= rumbleEnd[slot] && rumbleEnd[slot] != 0)
            {
                pads.Vibrate(slot, 0, 0);
                rumbleEnd[slot] = 0;
            }

            if (!samples[slot].Connected && now < nextProbe[slot])
            {
                continue;
            }

            var before = samples[slot];
            var s = pads.Read(slot);
            samples[slot] = s;
            nextProbe[slot] = now + ReconnectPollMilliseconds;
            bool firstConnection = s.Connected && !connectedBefore[slot];
            if (s.Connected) connectedBefore[slot] = true;
            bool canReportActivity = focused && !resumed && s.Connected && (before.Connected || firstConnection);
            bool axisActivity = false;
            if (canReportActivity) axisActivity = HasIntentionalAxisMovement(ref axisActivityBaselines[slot], s);
            else axisActivityBaselines[slot] = s;
            if (canReportActivity && (HasGamepadActivity(before, s) || axisActivity))
                RecordInputActivity(InputSourceKind.Gamepad, slot);
            if (before.Connected && !s.Connected && Profile.Players.Any(p => p.PadSlot == slot))
            {
                FocusPauseRequested = true;
                Clear();
                StopVibration();
            }

            if (!focused || !s.Connected)
            {
                continue;
            }

            PollPadSample(PadTokenStart + slot * PadTokenStride, QuickChordTokenStart + slot * DpadDirectionCount, s);
        }

        if (focused && !resumed && mobileChannel is not null)
            while (mobileChannel.TryDequeueTouchState(out var touch)) ApplyMobileTouchState(touch);

        for (int i = 0; i < held.Length; i++)
        {
            if (blocked[i])
            {
                if (!held[i])
                {
                    blocked[i] = false;
                }

                pending[i] = false;
                continue;
            }

            bool tap = focused && (i < KeyCount && pressed[i] || i >= MouseTokenStart && i < KeyboardMouseTokenEnd && mousePressed.Length == MouseButtonCount && mousePressed[i - MouseTokenStart]);
            bool edge = held[i] && !previous[i] || tap;
            if (edge)
            {
                pending[i] = true;
                repeat[i] = now + InitialRepeatMilliseconds;
            }

            if (edge && !resumed)
            {
                ReportTokenSource(i);
            }

            if (!held[i])
            {
                repeat[i] = 0;
            }
        }

        if (hasActivityCandidate)
            SetInputSource(activityCandidate.Kind, activityCandidate.Identity);
    }

    private void ReportTokenSource(int token)
    {
        if ((uint)token < KeyCount)
            RecordInputActivity(InputSourceKind.Keyboard, 0);
        else if (token < KeyboardMouseTokenEnd)
            RecordInputActivity(InputSourceKind.Mouse, 0);
    }

    private void RecordInputActivity(InputSourceKind kind, int identity)
    {
        if (hasAuthoritativeActivity) return;
        // shortcut: same-poll native sources lack per-device timestamps, use timestamped events if exact chronology is needed.
        int priority = kind switch { InputSourceKind.Touch => 3, InputSourceKind.Gamepad => 2, InputSourceKind.Mouse => 1, _ => 0 };
        int currentPriority = activityCandidate.Kind switch { InputSourceKind.Touch => 3, InputSourceKind.Gamepad => 2, InputSourceKind.Mouse => 1, _ => 0 };
        if (!hasActivityCandidate || priority > currentPriority || priority == currentPriority && identity < activityCandidate.Identity)
        {
            activityCandidate = new(0, kind, identity);
            hasActivityCandidate = true;
        }
    }

    private bool HasIntentionalAxisMovement(ref PadSample baseline, PadSample after)
    {
        int minimum = Math.Max(AxisHysteresis, Profile.DeadZone / 4);
        bool active = MeaningfulAxisDelta(baseline.LX, after.LX, minimum) || MeaningfulAxisDelta(baseline.LY, after.LY, minimum)
            || MeaningfulAxisDelta(baseline.RX, after.RX, minimum) || MeaningfulAxisDelta(baseline.RY, after.RY, minimum);
        baseline = active ? after : baseline with
        {
            LX = Math.Abs((int)after.LX) < Profile.DeadZone ? after.LX : baseline.LX,
            LY = Math.Abs((int)after.LY) < Profile.DeadZone ? after.LY : baseline.LY,
            RX = Math.Abs((int)after.RX) < Profile.DeadZone ? after.RX : baseline.RX,
            RY = Math.Abs((int)after.RY) < Profile.DeadZone ? after.RY : baseline.RY
        };
        return active;
    }

    private bool MeaningfulAxisDelta(short before, short after, int minimum) =>
        Math.Abs((int)after) >= Profile.DeadZone &&
        (Math.Abs((int)before) < Profile.DeadZone || Math.Abs((int)after - before) >= minimum);

    private bool HasGamepadActivity(PadSample before, PadSample after)
    {
        for (int i = 0; i < buttonMasks.Length; i++)
            if (((GamepadButtons)after.Buttons & buttonMasks[i]) != 0 && ((GamepadButtons)before.Buttons & buttonMasks[i]) == 0)
                return true;
        return after.LeftTrigger >= Profile.TriggerThreshold && before.LeftTrigger < Profile.TriggerThreshold
            || after.RightTrigger >= Profile.TriggerThreshold && before.RightTrigger < Profile.TriggerThreshold;
    }

    private void SetInputSource(InputSourceKind kind, int identity)
    {
        if (CurrentInputSource.Kind == kind && CurrentInputSource.Identity == identity) return;
        CurrentInputSource = new(CurrentInputSource.Sequence + 1, kind, identity);
        InputSourceChanged?.Invoke(CurrentInputSource);
    }

    private void PollPadSample(int tokenStart, int quickChordStart, PadSample sample)
    {
        for (int b = 0; b < buttonMasks.Length; b++)
            held[tokenStart + b] = ((GamepadButtons)sample.Buttons & buttonMasks[b]) != 0;
        held[tokenStart + LeftTrigger] = sample.LeftTrigger >= Profile.TriggerThreshold;
        held[tokenStart + RightTrigger] = sample.RightTrigger >= Profile.TriggerThreshold;
        for (int d = 0; d < DpadDirectionCount; d++)
            held[quickChordStart + d] = held[tokenStart + RightTrigger] && held[tokenStart + DpadUp + d];
        Axis(tokenStart + LeftStickLeft, sample.LX, false);
        Axis(tokenStart + LeftStickUp, sample.LY, true);
        Axis(tokenStart + RightStickLeft, sample.RX, false);
        Axis(tokenStart + RightStickUp, sample.RY, true);
    }

    private void Axis(int at, int value, bool positiveFirst)
    {
        int negative = positiveFirst ? at + 1 : at, positive = positiveFirst ? at : at + 1;
        held[negative] = value < -(Profile.DeadZone - (previous[negative] ? AxisHysteresis : 0));
        held[positive] = value > Profile.DeadZone - (previous[positive] ? AxisHysteresis : 0);
    }

    private int Token(int player, InputBinding b)
    {
        var p = Profile.Players[player];
        return b.Kind switch
        {
            InputKind.Key when p.Keyboard => b.Code,
            InputKind.Mouse when p.Keyboard => MouseTokenStart + b.Code,
            InputKind.Pad when p.PadSlot >= 0 => PadTokenStart + p.PadSlot * PadTokenStride + b.Code,
            _ => -1
        };
    }

    internal bool Held(int player, GameAction action)
        => Held(player, Profile.Players[player].Bindings[(int)action]) || player == 0 && (TouchMovement(action) || TouchHeld(player, action));
    internal bool Held(int player, InputBinding[] row)
    {
        foreach (var b in row)
        {
            int t = Token(player, b);
            if (t >= 0 && held[t] && !blocked[t])
            {
                return true;
            }
        }

        return false;
    }

    public void DiscardPointerConfirmation()
    {
        for (int player = 0; player < Profile.Players.Length; player++)
        {
            foreach (var binding in Profile.Players[player].Bindings[(int)GameAction.Confirm])
            {
                if (binding.Kind == InputKind.Mouse)
                    Take(Token(player, binding));
            }
        }
    }

    internal bool Press(int player, GameAction action, bool allowRepeat = false)
        => Press(player, Profile.Players[player].Bindings[(int)action], allowRepeat) ||
           player == 0 && TakeMobileAction(action);
    internal bool PressQuick(int player, int slot, bool assigning = false)
    {
        if (!assigning && TakeTouchQuick(player, slot)) return true;
        foreach (var b in Profile.Players[player].Bindings[(int)GameAction.Quick1 + slot])
        {
            int token = Token(player, b);
            if (b.Kind != InputKind.Pad || b.Code is < DpadUp or > DpadLeft)
            {
                if (Take(token))
                {
                    return true;
                }

                continue;
            }

            if (token < 0 && player != 0)
            {
                continue;
            }

            int chord = QuickChordTokenStart + Profile.Players[player].PadSlot * DpadDirectionCount + b.Code - DpadUp;
            int rt = PadTokenStart + Profile.Players[player].PadSlot * PadTokenStride + RightTrigger;
            if (assigning)
            {
                if (token >= 0) Take(chord);
                if (Take(token))
                {
                    return true;
                }

                continue;
            }

            if (token >= 0)
            {
                Take(token);
                if (Take(chord))
                {
                    Take(rt);
                    return true;
                }
            }

        }

        return false;
    }

    public static string QuickBindingHint(InputBinding binding) => binding.Kind == InputKind.Pad && binding.Code is >= DpadUp and <= DpadLeft ? "RT+" + binding : binding.ToString();
    internal bool Press(int player, InputBinding[] row, bool allowRepeat = false)
    {
        foreach (var b in row)
        {
            int t = Token(player, b);
            if (Take(t, allowRepeat))
            {
                return true;
            }
        }

        return false;
    }

    internal bool Take(int token, bool allowRepeat = false)
    {
        if (token < 0 || blocked[token])
        {
            return false;
        }

        if (pending[token])
        {
            pending[token] = false;
            return true;
        }

        if (allowRepeat && held[token] && repeat[token] != 0 && Now >= repeat[token])
        {
            repeat[token] = Now + RepeatIntervalMilliseconds;
            return true;
        }

        return false;
    }

    internal bool RawHeld(int token) => held[token] && !blocked[token];
    internal bool PlayerMenu(int player, GameAction action, bool repeat = false)
    {
        if (Press(player, action, repeat))
        {
            return true;
        }

        int stick = action switch
        {
            GameAction.MenuLeft => LeftStickLeft,
            GameAction.MenuRight => LeftStickRight,
            GameAction.MenuUp => LeftStickUp,
            GameAction.MenuDown => LeftStickDown,
            _ => -1
        };
        int slot = Profile.Players[player].PadSlot;
        return stick >= 0 && slot >= 0 && Take(PadTokenStart + slot * PadTokenStride + stick, repeat);
    }

    internal bool Menu(GameAction action, bool repeat = false)
    {
        for (int p = 0; p < 2; p++)
        {
            if (Press(p, action, repeat))
            {
                return true;
            }
        }

        int safe = action switch
        {
            GameAction.MenuLeft => 203,
            GameAction.MenuRight => 205,
            GameAction.MenuUp => 200,
            GameAction.MenuDown => 208,
            GameAction.Confirm => 28,
            GameAction.Back => 1,
            _ => -1
        };
        if (Take(safe, repeat))
        {
            return true;
        }

        int stick = action switch
        {
            GameAction.MenuLeft => LeftStickLeft,
            GameAction.MenuRight => LeftStickRight,
            GameAction.MenuUp => LeftStickUp,
            GameAction.MenuDown => LeftStickDown,
            _ => -1
        };
        if (stick >= 0)
        {
            for (int p = 0; p < 2; p++)
            {
                int slot = Profile.Players[p].PadSlot;
                if (slot >= 0 && Take(PadTokenStart + slot * PadTokenStride + stick, repeat))
                {
                    return true;
                }
            }

        }

        return false;
    }

    internal bool QueryLegacyMenuScan(byte scan) => LegacyMenuScan(scan, true);
    internal bool HeldLegacyMenuScan(byte scan) => LegacyMenuScan(scan, false);

    private bool LegacyMenuScan(byte scan, bool query)
    {
        for (int p = 0; p < 2; p++)
        {
            for (int a = 0; a < 8; a++)
            {
                if (scan == legacy[p, a])
                {
                    GameAction action = a switch
                    {
                        0 => GameAction.MenuLeft,
                        1 => GameAction.MenuRight,
                        2 => GameAction.MenuUp,
                        3 => GameAction.MenuDown,
                        4 => GameAction.Confirm,
                        5 => GameAction.Back,
                        7 => GameAction.Switch,
                        _ => GameAction.Execute
                    };
                    if (query)
                    {
                        return ControlProfile.IsMenu(action) ? Menu(action, a < 4) : Press(p, action);
                    }

                    if (p == 0 && mobileOverlay == MobileOverlayKind.Navigation && IsNavigationAction(action))
                        return TakeMobileAction(action);
                    return Held(p, action);
                }
            }
        }

        return query ? Take(scan) : RawHeld(scan);
    }

    internal bool QueryLegacyWorldScan(byte scan) => LegacyWorldScan(scan, true);
    internal bool HeldLegacyWorldScan(byte scan) => LegacyWorldScan(scan, false);

    private bool LegacyWorldScan(byte scan, bool query)
    {
        for (int player = 0; player < 2; player++)
        {
            for (int direction = 0; direction < 4; direction++)
            {
                if (scan != legacy[player, direction]) continue;
                GameAction movement = direction switch
                {
                    0 => GameAction.Left,
                    1 => GameAction.Right,
                    2 => GameAction.Up,
                    _ => GameAction.Down
                };
                if (query) return Press(player, movement, true);
                if (player == 0 && mobileOverlay == MobileOverlayKind.Navigation && TakeMobileAction(movement)) return true;
                return Held(player, movement);
            }
        }

        return LegacyMenuScan(scan, query);
    }

    public void Clear()
    {
        contextVersions[0]++;
        contextVersions[1]++;
        wheel = 0;
        Array.Clear(pending);
        for (int i = 0; i < held.Length; i++)
        {
            blocked[i] = held[i];
        }
        ResetMobileTouch(blockHeld: true);
    }

    public void ClearPlayer(int player)
    {
        contextVersions[player]++;
        void ClearRow(InputBinding[] row)
        {
            foreach (var binding in row)
            {
                int t = Token(player, binding);
                if (t >= 0)
                {
                    pending[t] = false;
                    blocked[t] = held[t];
                }
            }
        }

        foreach (var row in Profile.Players[player].Bindings)
        {
            ClearRow(row);
        }

        foreach (var row in Profile.Players[player].Direct.Values)
        {
            ClearRow(row);
        }

        int slot = Profile.Players[player].PadSlot;
        if (slot >= 0)
        {
            for (int d = 0; d < DpadDirectionCount; d++)
            {
                int t = QuickChordTokenStart + slot * DpadDirectionCount + d;
                pending[t] = false;
                blocked[t] = held[t];
            }
        }

        if (player == 0)
        {
            ResetMobileTouch(blockHeld: true);
        }
    }

    public InputBinding Capture(int player, InputKind kind, int? assignedPad = null)
    {
        int pad = assignedPad ?? Profile.Players[player].PadSlot;
        if (kind == InputKind.Pad && pad < 0 && player != 0)
        {
            return default;
        }

        int start = kind == InputKind.Pad ? PadTokenStart + pad * PadTokenStride : 0;
        int end = kind == InputKind.Pad ? start + PadControlCount : KeyboardMouseTokenEnd;
        if (kind != InputKind.Pad || pad >= 0)
        {
            for (int i = start; i < end; i++)
            {
                if (Take(i))
                {
                    return i >= PadTokenStart ? new(InputKind.Pad, i - start) : i >= MouseTokenStart ? new(InputKind.Mouse, i - MouseTokenStart) : new(InputKind.Key, i);
                }
            }
        }

        return default;
    }

    internal string Hint(int player, GameAction a)
    {
        if (CurrentInputSource.Kind == InputSourceKind.Touch) return TouchHint(a);
        var row = Profile.Players[player].Bindings[(int)a];
        InputKind preferred = CurrentInputSource.Kind switch
        {
            InputSourceKind.Mouse => InputKind.Mouse,
            InputSourceKind.Gamepad => InputKind.Pad,
            _ => InputKind.Key
        };
        var binding = row.FirstOrDefault(b => b.Kind == preferred);
        if (binding.Kind == InputKind.None)
            binding = row.FirstOrDefault(b => b.Kind is InputKind.Key or InputKind.Mouse or InputKind.Pad);
        return a is >= GameAction.Quick1 and <= GameAction.Quick4 ? QuickBindingHint(binding) : binding.ToString();
    }

    /// <summary>Gets the button used to assign a quick slot, without its execution chord.</summary>
    internal string QuickAssignHint(int player, int slot)
    {
        if (CurrentInputSource.Kind == InputSourceKind.Touch) return TouchHint((GameAction)((int)GameAction.Quick1 + slot));
        var row = Profile.Players[player].Bindings[(int)GameAction.Quick1 + slot];
        InputKind preferred = CurrentInputSource.Kind == InputSourceKind.Gamepad ? InputKind.Pad : InputKind.Key;
        var binding = row.FirstOrDefault(b => b.Kind == preferred);
        if (binding.Kind == InputKind.None) binding = row.FirstOrDefault(b => b.Kind is InputKind.Key or InputKind.Pad or InputKind.Mouse);
        return binding.ToString();
    }

    private static string TouchHint(GameAction action) => action switch
    {
        GameAction.Left or GameAction.MenuLeft => "←",
        GameAction.Right or GameAction.MenuRight => "→",
        GameAction.Up or GameAction.MenuUp => "↑",
        GameAction.Down or GameAction.MenuDown => "↓",
        GameAction.Confirm or GameAction.Jump => "A",
        GameAction.Back => "B",
        GameAction.Attack => "X",
        GameAction.Quick1 => "B+↑",
        GameAction.Quick2 => "B+→",
        GameAction.Quick3 => "B+↓",
        GameAction.Quick4 => "B+←",
        GameAction.Run => GameText.Get("ui.controls.movementStick"),
        GameAction.Branch => "B",
        _ => GameText.Get("ui.controls.touch")
    };

    public void Rumble(int player)
    {
        int slot = Profile.Players[player].PadSlot;
        if (Profile.Vibration && slot >= 0 && Connected(slot))
        {
            pads.Vibrate(slot, RumbleLowFrequencyStrength, RumbleHighFrequencyStrength);
            rumbleEnd[slot] = Now + RumbleMilliseconds;
        }
    }

    public void StopVibration()
    {
        for (int s = 0; s < PadCount; s++)
        {
            if (samples[s].Connected)
            {
                pads.Vibrate(s, 0, 0);
                rumbleEnd[s] = 0;
            }
        }
    }

    public void Dispose() => StopVibration();
}
