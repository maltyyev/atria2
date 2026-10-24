using Atria.Game.Dialogue;

namespace Atria.Game.Combat;

/// <summary>Normalized DirectInput joystick sample consumed by4046d0, before keyboard fallback.</summary>
public readonly record struct CombatJoystickState(int X, int Y, bool Button0, bool Button1, bool Button2, bool Button3);
/// <summary>4046d0 held input and4048c0's per-query persistent edge latch.</summary>
public sealed class CombatScanInput
{
    public Atria.Game.Controls.InputRouter? Modern
    {
        get; set;
    }

    private readonly Func<byte, bool> keyboardHeld, readHeld;
    private readonly Func<byte, Func<byte, bool>, bool> query;
    private readonly ScanCodeEdges localEdges = new();
    public CombatScanInput(Func<byte, bool> keyboardHeld, Func<byte, Func<byte, bool>, bool>? sharedQuery = null)
    {
        this.keyboardHeld = keyboardHeld;
        readHeld = IsHeld;
        query = sharedQuery ?? QueryLocal;
    }

    private bool QueryLocal(byte scan, Func<byte, bool> held) => localEdges.Query(held, scan);
    public int ConfiguredPlayers { get; set; } = 1;
    public CombatJoystickState Joystick
    {
        get; set;
    }

    public bool IsHeld(byte scan)
    {
        CombatJoystickState joy = Joystick;
        bool joystick = ConfiguredPlayers == 2 ? scan switch
        {
            0x16 => joy.Y < -500,
            0x17 => joy.Button2,
            0x18 => joy.Button3,
            0x23 => joy.X < -500,
            0x24 => joy.X > 500,
            0x25 => joy.Button1,
            0x26 => joy.Button0,
            0x31 => joy.Y > 500,
            _ => false
        } : scan switch
        {
            0x1f => joy.Button3,
            0x2c => joy.Button2,
            0x2d => joy.Button1,
            0x2e => joy.Button0,
            200 => joy.Y < -500,
            203 => joy.X < -500,
            205 => joy.X > 500,
            208 => joy.Y > 500,
            _ => false
        };
        return joystick || keyboardHeld(scan);
    }

    public bool Query(byte scan) => Modern is null && query(scan, readHeld);
}
