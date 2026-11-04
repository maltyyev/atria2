using Atria.Core.Game.Progression;
using Atria.Core.Resources;

namespace Atria.Core.Runtime.Input;

/// <summary>Actions available while navigating menus and modal UI.</summary>
public enum MenuAction
{
    /// <summary>Moves the current menu selection left.</summary>
    Left,
    /// <summary>Moves the current menu selection right.</summary>
    Right,
    /// <summary>Moves the current menu selection up.</summary>
    Up,
    /// <summary>Moves the current menu selection down.</summary>
    Down,
    /// <summary>Activates the selected menu item.</summary>
    Confirm,
    /// <summary>Leaves the current menu or modal.</summary>
    Back,
    /// <summary>Opens the control reference.</summary>
    Reference
}

/// <summary>Movement and system actions available in the world and dungeons.</summary>
public enum WorldAction
{
    /// <summary>Moves the player left.</summary>
    Left,
    /// <summary>Moves the player right.</summary>
    Right,
    /// <summary>Moves the player up.</summary>
    Up,
    /// <summary>Moves the player down.</summary>
    Down,
    /// <summary>Interacts with the object or location in front of the player.</summary>
    Confirm,
    /// <summary>Cancels the current world interaction.</summary>
    Back,
    /// <summary>Opens the pause menu.</summary>
    Pause,
    /// <summary>Opens the control reference.</summary>
    Reference,
    /// <summary>Opens the party status screen.</summary>
    Status
}

/// <summary>Navigation and system actions available in towns and interiors.</summary>
public enum TownAction
{
    /// <summary>Moves the current selection left.</summary>
    Left,
    /// <summary>Moves the current selection right.</summary>
    Right,
    /// <summary>Moves the current selection up.</summary>
    Up,
    /// <summary>Moves the current selection down.</summary>
    Down,
    /// <summary>Confirms a town or interior interaction.</summary>
    Confirm,
    /// <summary>Cancels a town or interior interaction.</summary>
    Back,
    /// <summary>Opens the pause menu.</summary>
    Pause,
    /// <summary>Opens the control reference.</summary>
    Reference,
    /// <summary>Opens the party status screen.</summary>
    Status
}

/// <summary>Actor, movement, quick-slot and system actions available during combat.</summary>
public enum CombatAction
{
    /// <summary>Performs the basic attack.</summary>
    Attack,
    /// <summary>Jumps and may begin an attack or traversal move.</summary>
    Jump,
    /// <summary>Executes the selected command.</summary>
    Execute,
    /// <summary>Changes the controlled actor.</summary>
    Switch,
    /// <summary>Runs while movement is held.</summary>
    Run,
    /// <summary>Branches from the current move.</summary>
    Branch,
    /// <summary>Opens the move selector.</summary>
    Selector,
    /// <summary>Uses quick slot one.</summary>
    Quick1,
    /// <summary>Uses quick slot two.</summary>
    Quick2,
    /// <summary>Uses quick slot three.</summary>
    Quick3,
    /// <summary>Uses quick slot four.</summary>
    Quick4,
    /// <summary>Opens the pause menu.</summary>
    Pause,
    /// <summary>Opens the control reference.</summary>
    Reference,
    /// <summary>Toggles combat automation.</summary>
    Automation,
    /// <summary>Moves the actor left.</summary>
    Left,
    /// <summary>Moves the actor right.</summary>
    Right,
    /// <summary>Moves the actor up.</summary>
    Up,
    /// <summary>Moves the actor down.</summary>
    Down,
    /// <summary>Performs the back-down move direction.</summary>
    BackDown,
    /// <summary>Performs the branch-down move direction.</summary>
    BranchDown,
    /// <summary>Performs the forward-down move direction.</summary>
    ForwardDown,
    /// <summary>Performs the branch-back move direction.</summary>
    BranchBack,
    /// <summary>Performs the branch-forward move direction.</summary>
    BranchForward,
    /// <summary>Performs the back-up move direction.</summary>
    BackUp,
    /// <summary>Performs the branch-up move direction.</summary>
    BranchUp,
    /// <summary>Performs the forward-up move direction.</summary>
    ForwardUp
}

/// <summary>Menu-only actions over the router's shared edge state.</summary>
public readonly struct MenuInputView
{
    private readonly InputRouter router;
    internal MenuInputView(InputRouter router) => this.router = router;

    /// <summary>Consumes an action edge for one player.</summary>
    public bool Press(int player, MenuAction action, bool allowRepeat = false) => router.Press(player, Map(action), allowRepeat);
    /// <summary>Reads an action's held state for one player.</summary>
    public bool Held(int player, MenuAction action) => router.Held(player, Map(action));
    /// <summary>Consumes the first matching action from either player, including menu fallbacks.</summary>
    public bool Menu(MenuAction action, bool allowRepeat = false) => router.Menu(Map(action), allowRepeat);
    /// <summary>Consumes one player's menu action, including that player's stick navigation.</summary>
    public bool PlayerMenu(int player, MenuAction action, bool allowRepeat = false) => router.PlayerMenu(player, Map(action), allowRepeat);
    internal bool QueryLegacyMenuScan(byte scan) => router.QueryLegacyMenuScan(scan);
    internal bool HeldLegacyMenuScan(byte scan) => router.HeldLegacyMenuScan(scan);

    private static GameAction Map(MenuAction action) => action switch
    {
        MenuAction.Left => GameAction.MenuLeft,
        MenuAction.Right => GameAction.MenuRight,
        MenuAction.Up => GameAction.MenuUp,
        MenuAction.Down => GameAction.MenuDown,
        MenuAction.Confirm => GameAction.Confirm,
        MenuAction.Back => GameAction.Back,
        MenuAction.Reference => GameAction.Reference,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}

/// <summary>World and dungeon movement/actions, separate from menu navigation.</summary>
public readonly struct WorldInputView
{
    private readonly InputRouter router;
    internal WorldInputView(InputRouter router) => this.router = router;

    /// <summary>Consumes a world action edge for one player.</summary>
    public bool Press(int player, WorldAction action, bool allowRepeat = false) => router.Press(player, Map(action), allowRepeat);
    /// <summary>Reads held movement or action state for one player.</summary>
    public bool Held(int player, WorldAction action) => router.Held(player, Map(action));
    internal bool QueryLegacyWorldScan(byte scan) => router.QueryLegacyWorldScan(scan);
    internal bool HeldLegacyWorldScan(byte scan) => router.HeldLegacyWorldScan(scan);

    private static GameAction Map(WorldAction action) => action switch
    {
        WorldAction.Left => GameAction.Left,
        WorldAction.Right => GameAction.Right,
        WorldAction.Up => GameAction.Up,
        WorldAction.Down => GameAction.Down,
        WorldAction.Confirm => GameAction.Confirm,
        WorldAction.Back => GameAction.Back,
        WorldAction.Pause => GameAction.Pause,
        WorldAction.Reference => GameAction.Reference,
        WorldAction.Status => GameAction.Status,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}

/// <summary>Town-scoped actions over the shared edge state.</summary>
public readonly struct TownInputView
{
    private readonly InputRouter router;
    internal TownInputView(InputRouter router) => this.router = router;

    /// <summary>Consumes a town action edge for one player.</summary>
    public bool Press(int player, TownAction action, bool allowRepeat = false) => router.Press(player, Map(action), allowRepeat);
    /// <summary>Consumes one touch-only edge that selects the previous town object.</summary>
    public bool TakeMobilePrevious() => router.TakeMobileTownPrevious();
    /// <summary>Consumes one touch-only edge that selects the next town object.</summary>
    public bool TakeMobileNext() => router.TakeMobileTownNext();
    /// <summary>Reads held action state for one player.</summary>
    public bool Held(int player, TownAction action) => router.Held(player, Map(action));
    /// <summary>Consumes the first matching menu action from either player.</summary>
    public bool Menu(TownAction action, bool allowRepeat = false) => router.Menu(Map(action), allowRepeat);
    /// <summary>Consumes one player's town menu action.</summary>
    public bool PlayerMenu(int player, TownAction action, bool allowRepeat = false) => router.PlayerMenu(player, Map(action), allowRepeat);
    internal bool QueryLegacyTownScan(byte scan) => router.QueryLegacyMenuScan(scan);
    internal bool HeldLegacyTownScan(byte scan) => router.HeldLegacyMenuScan(scan);

    private static GameAction Map(TownAction action) => action switch
    {
        TownAction.Left => GameAction.MenuLeft,
        TownAction.Right => GameAction.MenuRight,
        TownAction.Up => GameAction.MenuUp,
        TownAction.Down => GameAction.MenuDown,
        TownAction.Confirm => GameAction.Confirm,
        TownAction.Back => GameAction.Back,
        TownAction.Pause => GameAction.Pause,
        TownAction.Reference => GameAction.Reference,
        TownAction.Status => GameAction.Status,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}

/// <summary>A combat-scoped view over shared device samples and edge latches.</summary>
public readonly struct CombatInput
{
    private readonly InputRouter router;
    internal CombatInput(InputRouter router) => this.router = router;

    /// <summary>Gets the combat profile used for player ownership and movement rules.</summary>
    public ControlProfile Profile => router.Profile;
    /// <summary>Gets or sets the latest combat action feedback.</summary>
    public string Notice { get => router.Notice; set => router.Notice = value; }
    /// <summary>Consumes a combat action edge for one player.</summary>
    public bool Press(int player, CombatAction action, bool allowRepeat = false) => router.Press(player, Map(action), allowRepeat);
    /// <summary>Consumes a configured direct move binding.</summary>
    public bool PressDirect(int player, InputBinding[] bindings, bool allowRepeat = false) => router.Press(player, bindings, allowRepeat);
    /// <summary>Reads held movement or action state for one player.</summary>
    public bool Held(int player, CombatAction action) => router.Held(player, Map(action));
    /// <summary>Gets semantic run state without changing Classic physical bindings.</summary>
    public bool? Run(int player) => router.Run(player);
    /// <summary>Consumes a configured quick action, including trigger and direction chords.</summary>
    public bool PressQuick(int player, int slot, bool assigning = false) => router.PressQuick(player, slot, assigning);
    /// <summary>Clears pending actions for one player after a combat ownership change.</summary>
    public void ClearPlayer(int player) => router.ClearPlayer(player);
    /// <summary>Requests vibration for a physical controller when available.</summary>
    public void Rumble(int player) => router.Rumble(player);

    private static GameAction Map(CombatAction action) => action switch
    {
        CombatAction.Attack => GameAction.Attack,
        CombatAction.Jump => GameAction.Jump,
        CombatAction.Execute => GameAction.Execute,
        CombatAction.Switch => GameAction.Switch,
        CombatAction.Run => GameAction.Run,
        CombatAction.Branch => GameAction.Branch,
        CombatAction.Selector => GameAction.Selector,
        CombatAction.Quick1 => GameAction.Quick1,
        CombatAction.Quick2 => GameAction.Quick2,
        CombatAction.Quick3 => GameAction.Quick3,
        CombatAction.Quick4 => GameAction.Quick4,
        CombatAction.Pause => GameAction.Pause,
        CombatAction.Reference => GameAction.Reference,
        CombatAction.Automation => GameAction.Automation,
        CombatAction.Left => GameAction.Left,
        CombatAction.Right => GameAction.Right,
        CombatAction.Up => GameAction.Up,
        CombatAction.Down => GameAction.Down,
        CombatAction.BackDown => GameAction.BackDown,
        CombatAction.BranchDown => GameAction.BranchDown,
        CombatAction.ForwardDown => GameAction.ForwardDown,
        CombatAction.BranchBack => GameAction.BranchBack,
        CombatAction.BranchForward => GameAction.BranchForward,
        CombatAction.BackUp => GameAction.BackUp,
        CombatAction.BranchUp => GameAction.BranchUp,
        CombatAction.ForwardUp => GameAction.ForwardUp,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
