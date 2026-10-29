using Atria.Resources;
using Atria.Game.State;

namespace Atria.Game.Controls;

public enum GameAction
{
    Left,
    Right,
    Up,
    Down,
    Attack,
    Jump,
    Execute,
    Switch,
    Run,
    Branch,
    Selector,
    Quick1,
    Quick2,
    Quick3,
    Quick4,
    Pause,
    Reference,
    Automation,
    MenuLeft,
    MenuRight,
    MenuUp,
    MenuDown,
    Confirm,
    Back,
    Status,
    BackDown,
    BranchDown,
    ForwardDown,
    BranchBack,
    BranchForward,
    BackUp,
    BranchUp,
    ForwardUp,
    Count
}

public enum InputKind
{
    None,
    Key,
    Mouse,
    Pad
}

public readonly record struct InputBinding(InputKind Kind, int Code)
{
    public static InputBinding Key(int scan) => new(InputKind.Key, scan);
    public static InputBinding Pad(int code) => new(InputKind.Pad, code);
    public override string ToString() => ControlNames.Binding(this);
}

public sealed class PlayerControls
{
    // -1 keyboard only; 0..3 XInput slot; keyboard may also be enabled alongside a pad.
    public int PadSlot { get; set; } = -1;
    public bool Keyboard { get; set; } = true;
    public bool Classic
    {
        get; set;
    }
    public string Preset { get; set; } = "Modern";
    public InputBinding[][] Bindings { get; } = Enumerable.Range(0, (int)GameAction.Count).Select(_ => new InputBinding[3]).ToArray();
    public Dictionary<string, InputBinding[]> Direct { get; } = new(StringComparer.Ordinal);
    public string[][] Quick { get; private set; } = Enumerable.Range(0, HeroCatalog.Default.Count).Select(_ => new[] { "v:4", "v:8", "v:6", "v:2" }).ToArray();

    public void EnsureHeroCount(int count)
    {
        if (count <= Quick.Length) return;
        Quick = Quick.Concat(Enumerable.Range(Quick.Length, count - Quick.Length).Select(_ => new[] { "v:4", "v:8", "v:6", "v:2" })).ToArray();
    }

    public void Set(GameAction action, InputBinding primary, InputBinding secondary = default, InputBinding pad = default) => Bindings[(int)action] = [primary, secondary, pad];
}

public sealed class ControlProfile
{
    public PlayerControls[] Players { get; } = [new(), new()];
    public int DeadZone { get; set; } = 8000;
    public int TriggerThreshold { get; set; } = 40;
    public bool Vibration { get; set; } = true;
    public bool Hints { get; set; } = true;
    public int AutosaveLimit { get; set; } = 3;

    public static ControlProfile Defaults(ControlBindings legacy)
    {
        var p = new ControlProfile();
        p.ResetPlayer(0, false, legacy);
        p.ResetPlayer(1, false, legacy);
        return p;
    }

    public void ResetPlayer(int player, bool classic, ControlBindings legacy)
    {
        var p = new PlayerControls
        {
            Classic = classic,
            Preset = classic ? "Classic" : "Modern",
            PadSlot = player == 0 ? 0 : 1,
            Keyboard = player == 0
        };
        Players[player] = p;
        int[] keys = player == 0 ? [30, 32, 17, 31, 36, 57, 37, 15] : [203, 205, 200, 208, 79, 82, 80, 81];
        for (int a = 0; a < 8; a++)
        {
            p.Set((GameAction)a, InputBinding.Key(classic ? legacy[player, a] : keys[a]));
        }

        p.Bindings[(int)GameAction.Left][2] = InputBinding.Pad(16);
        p.Bindings[(int)GameAction.Right][2] = InputBinding.Pad(17);
        p.Bindings[(int)GameAction.Up][2] = InputBinding.Pad(18);
        p.Bindings[(int)GameAction.Down][2] = InputBinding.Pad(19);
        p.Bindings[(int)GameAction.Attack][2] = InputBinding.Pad(2);
        p.Bindings[(int)GameAction.Jump][2] = InputBinding.Pad(0);
        p.Bindings[(int)GameAction.Execute][2] = InputBinding.Pad(3);
        p.Bindings[(int)GameAction.Switch][2] = InputBinding.Pad(5);
        if (player == 0 && !classic)
        {
            p.Bindings[(int)GameAction.Attack][1] = new(InputKind.Mouse, 0);
            p.Bindings[(int)GameAction.Execute][1] = new(InputKind.Mouse, 1);
        }

        p.Set(GameAction.Run, InputBinding.Key(player == 0 ? 42 : 54), pad: InputBinding.Pad(14));
        p.Set(GameAction.Branch, InputBinding.Key(player == 0 ? 18 : 76), pad: InputBinding.Pad(1));
        p.Set(GameAction.Selector, InputBinding.Key(player == 0 ? 16 : 71), pad: InputBinding.Pad(4));
        for (int i = 0; i < 4; i++)
        {
            p.Set(GameAction.Quick1 + i, InputBinding.Key(player == 0 ? 2 + i : 63 + i), pad: InputBinding.Pad(10 + i));
        }

        p.Set(GameAction.Pause, InputBinding.Key(1), pad: InputBinding.Pad(7));
        p.Set(GameAction.Reference, InputBinding.Key(59), pad: InputBinding.Pad(6));
        p.Set(GameAction.Automation, InputBinding.Key(60));
        p.Set(GameAction.Status, InputBinding.Key(23));
        int[] menus = [203, 205, 200, 208];
        int[] aliases = [30, 32, 17, 31];
        int[] pads = [13, 11, 10, 12];
        for (int i = 0; i < 4; i++)
        {
            p.Set(GameAction.MenuLeft + i, InputBinding.Key(menus[i]), InputBinding.Key(aliases[i]), InputBinding.Pad(pads[i]));
        }

        p.Set(GameAction.Confirm, InputBinding.Key(28), new(InputKind.Mouse, 0), InputBinding.Pad(0));
        p.Set(GameAction.Back, InputBinding.Key(1), new(InputKind.Mouse, 1), InputBinding.Pad(1));
    }

    public ControlProfile Clone() => ControlProfileStore.Decode(ControlProfileStore.Encode(this));
    public static bool SameContext(GameAction a, GameAction b) => IsMenu(a) == IsMenu(b);
    public static bool IsMenu(GameAction a) => a is >= GameAction.MenuLeft and <= GameAction.Back;
}

public static class ControlNames
{
    private static readonly string[] names =
    [
        "ui.controls.left",
        "ui.controls.right",
        "ui.controls.up",
        "ui.controls.down",
        "ui.controls.attack",
        "ui.controls.jump",
        "ui.controls.ability",
        "ui.controls.nextHero",
        "ui.controls.run",
        "ui.controls.directMove",
        "ui.controls.selector",
        "ui.controls.slot1",
        "ui.controls.slot2",
        "ui.controls.slot3",
        "ui.controls.slot4",
        "ui.pause.title",
        "ui.controls.reference",
        "ui.controls.automation",
        "ui.controls.menuLeft",
        "ui.controls.menuRight",
        "ui.controls.menuUp",
        "ui.controls.menuDown",
        "ui.controls.confirm",
        "ui.controls.back",
        "ui.controls.status",
        "ui.controls.moveDownBack",
        "ui.controls.moveDown",
        "ui.controls.moveDownForward",
        "ui.controls.moveBack",
        "ui.controls.moveForward",
        "ui.controls.moveUpBack",
        "ui.controls.moveUp",
        "ui.controls.moveUpForward",
    ];
    private static readonly string[] padKeys =
    [
        "ui.keys.A",
        "ui.keys.B",
        "ui.keys.X",
        "ui.keys.Y",
        "ui.keys.LB",
        "ui.keys.RB",
        "ui.keys.View",
        "ui.keys.Menu",
        "ui.keys.LS",
        "ui.keys.RS",
        "ui.keys.D-Up",
        "ui.keys.D-Right",
        "ui.keys.D-Down",
        "ui.keys.D-Left",
        "ui.keys.LT",
        "ui.keys.RT",
        "ui.keys.LS-Left",
        "ui.keys.LS-Right",
        "ui.keys.LS-Up",
        "ui.keys.LS-Down",
        "ui.keys.RS-Left",
        "ui.keys.RS-Right",
        "ui.keys.RS-Up",
        "ui.keys.RS-Down",
    ];
    public static string Action(GameAction a) => GameText.Get(names[(int)a]);
    public static string Binding(InputBinding b) => b.Kind switch
    {
        InputKind.None => "--",
        InputKind.Mouse => b.Code == 0 ? GameText.Get("ui.controls.mouseLeft") : b.Code == 1 ? GameText.Get("ui.controls.mouseRight") : GameText.Get("ui.controls.mouseMiddle"),
        InputKind.Pad => (uint)b.Code < padKeys.Length ? GameText.Get(padKeys[b.Code]) : "?",
        _ => b.Code switch
        {
            1 => GameText.Get("ui.keys.Esc"),
            28 => GameText.Get("ui.keys.Enter"),
            57 => GameText.Get("ui.keys.Space"),
            15 => GameText.Get("ui.keys.Tab"),
            42 => GameText.Get("ui.keys.Shift"),
            54 => GameText.Get("ui.keys.RShift"),
            29 => GameText.Get("ui.keys.Ctrl"),
            56 => GameText.Get("ui.keys.Alt"),
            14 => GameText.Get("ui.keys.Backspace"),
            200 => GameText.Get("ui.keys.Up"),
            208 => GameText.Get("ui.keys.Down"),
            203 => GameText.Get("ui.keys.Left"),
            205 => GameText.Get("ui.keys.Right"),
            59 => GameText.Get("ui.keys.F1"),
            60 => GameText.Get("ui.keys.F2"),
            >= 2 and <= 11 => (b.Code == 11 ? 0 : b.Code - 1).ToString(),
            >= 16 and <= 25 => "QWERTYUIOP"[b.Code - 16].ToString(),
            >= 30 and <= 38 => "ASDFGHJKL"[b.Code - 30].ToString(),
            >= 44 and <= 50 => "ZXCVBNM"[b.Code - 44].ToString(),
            _ => GameText.Format("ui.keys.unknown", ("code", b.Code))
        }
    };
}
