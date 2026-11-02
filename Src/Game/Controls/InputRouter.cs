using Atria.Platform.Input;
using Atria.Platform;
using Atria.Render;
using Atria.Resources;

namespace Atria.Game.Controls;

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
    private const int VirtualPadTokenStart = 448;
    private const int VirtualQuickChordTokenStart = VirtualPadTokenStart + PadControlCount;
    private const int InputTokenCapacity = VirtualQuickChordTokenStart + DpadDirectionCount;
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
    private readonly bool[] connectedBefore = new bool[PadCount];
    private bool wasFocused = true;
    private long externalActivitySequence;
    private int lastWindowMouseX = int.MinValue, lastWindowMouseY = int.MinValue;
    private (int Width, int Height) lastWindowSize;
    private bool hasActivityCandidate;
    private bool hasAuthoritativeActivity;
    private PadSample previousVirtualPad;
    private InputActivity activityCandidate;
    private readonly int[] contextVersions = new int[2];
    public int ContextVersion(int player) => contextVersions[player];
    /// <summary>Gets a non-allocating menu action view over the shared input state.</summary>
    public SceneInput MenuInput => new(this, InputScene.Menu);
    /// <summary>Gets a non-allocating world action view over the shared input state.</summary>
    public SceneInput WorldInput => new(this, InputScene.World);
    /// <summary>Gets a non-allocating town action view over the shared input state.</summary>
    public SceneInput TownInput => new(this, InputScene.Town);
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
    public string ProfileMenuHint => CurrentInputSource.Kind is InputSourceKind.Gamepad or InputSourceKind.Touch
        ? ControlNames.Binding(InputBinding.Pad(3)) : ControlNames.Binding(InputBinding.Key(0x3c));
    /// <summary>Gets the profile rename binding appropriate to the latest active source.</summary>
    public string ProfileRenameHint => CurrentInputSource.Kind is InputSourceKind.Gamepad or InputSourceKind.Touch
        ? ControlNames.Binding(InputBinding.Pad(2)) : ControlNames.Binding(InputBinding.Key(0x3d));
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

    public bool Connected(int slot) => slot >= 0 && samples[slot].Connected;
    internal bool ProfileMenuPressed() => Take(0x3c) || PadButtonPressed(3); // F2 / physical Y
    internal bool PadButtonPressed(int button, bool repeat = false)
    {
        for (int slot = 0; slot < PadCount; slot++)
            if (Take(PadTokenStart + slot * PadTokenStride + button, repeat)) return true;
        return Take(VirtualPadTokenStart + button, repeat);
    }
    /// <summary>Samples the shared window, physical devices, optional virtual pad, and activity source.</summary>
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
            window is IVirtualGamepadSource virtualPad ? virtualPad.ReadVirtualGamepad() : default,
            mouseMoved,
            externalActivity
        );
        if (window.Focused)
        {
            wheel += window.MouseWheelDelta;
        }

    }

    /// <summary>Samples keyboard, mouse, physical pads, and an optional virtual touch pad at one timestamp.</summary>
    public void Poll(
        ReadOnlySpan<bool> keys,
        ReadOnlySpan<bool> pressed,
        ReadOnlySpan<bool> mouse,
        bool focused,
        long now,
        ReadOnlySpan<bool> mousePressed = default,
        PadSample virtualPad = default
    ) => Poll(keys, pressed, mouse, focused, now, mousePressed, virtualPad, false, default);

    private void Poll(
        ReadOnlySpan<bool> keys,
        ReadOnlySpan<bool> pressed,
        ReadOnlySpan<bool> mouse,
        bool focused,
        long now,
        ReadOnlySpan<bool> mousePressed,
        PadSample virtualPad,
        bool mouseMoved,
        InputActivity externalActivity)
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
            if (focused && s.Connected && (before.Connected || firstConnection) && HasGamepadActivity(before, s))
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

        if (focused && virtualPad.Connected)
        {
            if (previousVirtualPad.Connected && HasIntentionalAxisMovement(previousVirtualPad, virtualPad))
                RecordInputActivity(InputSourceKind.Touch, hasActivityCandidate && activityCandidate.Kind == InputSourceKind.Touch ? activityCandidate.Identity : 0);
            PollPadSample(VirtualPadTokenStart, VirtualQuickChordTokenStart, virtualPad);
        }
        previousVirtualPad = virtualPad;

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
        else if (token >= VirtualPadTokenStart && token < InputTokenCapacity)
            RecordInputActivity(InputSourceKind.Touch, hasActivityCandidate && activityCandidate.Kind == InputSourceKind.Touch ? activityCandidate.Identity : 0);
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

    private bool HasIntentionalAxisMovement(PadSample before, PadSample after)
    {
        int minimum = Math.Max(AxisHysteresis, Profile.DeadZone / 4);
        return MeaningfulAxisDelta(before.LX, after.LX, minimum) || MeaningfulAxisDelta(before.LY, after.LY, minimum)
            || MeaningfulAxisDelta(before.RX, after.RX, minimum) || MeaningfulAxisDelta(before.RY, after.RY, minimum);
    }

    private bool MeaningfulAxisDelta(short before, short after, int minimum) =>
        Math.Abs((int)after) >= Profile.DeadZone && Math.Abs((int)after - before) >= minimum;

    private bool HasGamepadActivity(PadSample before, PadSample after)
    {
        for (int i = 0; i < buttonMasks.Length; i++)
            if (((GamepadButtons)after.Buttons & buttonMasks[i]) != 0 && ((GamepadButtons)before.Buttons & buttonMasks[i]) == 0)
                return true;
        return after.LeftTrigger >= Profile.TriggerThreshold && before.LeftTrigger < Profile.TriggerThreshold
            || after.RightTrigger >= Profile.TriggerThreshold && before.RightTrigger < Profile.TriggerThreshold
            || HasIntentionalAxisMovement(before, after);
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

    public bool Held(int player, GameAction action) => Held(player, Profile.Players[player].Bindings[(int)action]);
    public bool Held(int player, InputBinding[] row)
    {
        foreach (var b in row)
        {
            int t = Token(player, b);
            if (t >= 0 && held[t] && !blocked[t]
                || player == 0 && b.Kind == InputKind.Pad && held[VirtualPadTokenStart + b.Code] && !blocked[VirtualPadTokenStart + b.Code])
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

    public bool Press(int player, GameAction action, bool allowRepeat = false) => Press(player, Profile.Players[player].Bindings[(int)action], allowRepeat);
    public bool PressQuick(int player, int slot, bool assigning = false)
    {
        foreach (var b in Profile.Players[player].Bindings[(int)GameAction.Quick1 + slot])
        {
            int token = Token(player, b);
            if (b.Kind != InputKind.Pad || b.Code is < DpadUp or > DpadLeft)
            {
                if (Take(token) || player == 0 && b.Kind == InputKind.Pad && Take(VirtualPadTokenStart + b.Code))
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
            int virtualDirection = b.Code - DpadUp;
            int virtualChord = VirtualQuickChordTokenStart + virtualDirection;
            int virtualTrigger = VirtualPadTokenStart + RightTrigger;
            if (assigning)
            {
                if (token >= 0) Take(chord);
                if (player == 0) Take(virtualChord);
                if (Take(token) || player == 0 && Take(VirtualPadTokenStart + b.Code))
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

            if (player == 0)
            {
                Take(VirtualPadTokenStart + b.Code);
                if (!Take(virtualChord)) continue;
                Take(virtualTrigger);
                return true;
            }
        }

        return false;
    }

    public static string QuickBindingHint(InputBinding binding) => binding.Kind == InputKind.Pad && binding.Code is >= DpadUp and <= DpadLeft ? "RT+" + binding : binding.ToString();
    public bool Press(int player, InputBinding[] row, bool allowRepeat = false)
    {
        foreach (var b in row)
        {
            int t = Token(player, b);
            if (Take(t, allowRepeat)
                || player == 0 && b.Kind == InputKind.Pad && Take(VirtualPadTokenStart + b.Code, allowRepeat))
            {
                return true;
            }
        }

        return false;
    }

    public bool Take(int token, bool allowRepeat = false)
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

    public bool RawHeld(int token) => held[token] && !blocked[token];
    public bool PlayerMenu(int player, GameAction action, bool repeat = false)
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
        return stick >= 0 && (slot >= 0 && Take(PadTokenStart + slot * PadTokenStride + stick, repeat)
            || player == 0 && Take(VirtualPadTokenStart + stick, repeat));
    }

    public bool Menu(GameAction action, bool repeat = false)
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

            if (Take(VirtualPadTokenStart + stick, repeat)) return true;
        }

        return false;
    }

    public bool LegacyMenu(byte scan, bool query)
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

                    return Held(p, action);
                }
            }
        }

        return query ? Take(scan) : RawHeld(scan);
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
            for (int i = VirtualPadTokenStart; i < InputTokenCapacity; i++)
            {
                pending[i] = false;
                blocked[i] = held[i];
            }
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

        if (kind == InputKind.Pad && player == 0)
        {
            for (int i = VirtualPadTokenStart; i < VirtualPadTokenStart + PadControlCount; i++)
            {
                if (Take(i))
                {
                    return new(InputKind.Pad, i - VirtualPadTokenStart);
                }
            }
        }

        return default;
    }

    public string Hint(int player, GameAction a)
    {
        var row = Profile.Players[player].Bindings[(int)a];
        InputKind preferred = CurrentInputSource.Kind switch
        {
            InputSourceKind.Mouse => InputKind.Mouse,
            InputSourceKind.Gamepad or InputSourceKind.Touch => InputKind.Pad,
            _ => InputKind.Key
        };
        var binding = row.FirstOrDefault(b => b.Kind == preferred);
        if (binding.Kind == InputKind.None)
            binding = row.FirstOrDefault(b => b.Kind is InputKind.Key or InputKind.Mouse or InputKind.Pad);
        return a is >= GameAction.Quick1 and <= GameAction.Quick4 ? QuickBindingHint(binding) : binding.ToString();
    }

    /// <summary>Gets the button used to assign a quick slot, without its execution chord.</summary>
    public string QuickAssignHint(int player, int slot)
    {
        var row = Profile.Players[player].Bindings[(int)GameAction.Quick1 + slot];
        InputKind preferred = CurrentInputSource.Kind is InputSourceKind.Gamepad or InputSourceKind.Touch ? InputKind.Pad : InputKind.Key;
        var binding = row.FirstOrDefault(b => b.Kind == preferred);
        if (binding.Kind == InputKind.None) binding = row.FirstOrDefault(b => b.Kind is InputKind.Key or InputKind.Pad or InputKind.Mouse);
        return binding.ToString();
    }

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
