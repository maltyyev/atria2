namespace Atria.Game.Controls;

/// <summary>Identifies the gameplay scene whose action set is active.</summary>
public enum InputScene
{
    Menu,
    World,
    Town
}

/// <summary>A non-allocating view that permits only actions meaningful to one scene.</summary>
public readonly struct SceneInput
{
    private readonly InputRouter router;
    private readonly InputScene scene;

    internal SceneInput(InputRouter router, InputScene scene)
    {
        this.router = router;
        this.scene = scene;
    }

    /// <summary>Consumes a pressed edge only when the action belongs to this scene.</summary>
    public bool Press(int player, GameAction action, bool allowRepeat = false) =>
        Allows(action) && router.Press(player, action, allowRepeat);

    /// <summary>Reads held state only when the action belongs to this scene.</summary>
    public bool Held(int player, GameAction action) => Allows(action) && router.Held(player, action);

    /// <summary>Queries the legacy scan adapter with this scene's action meanings.</summary>
    public bool QueryLegacyScan(byte scan) => router.QuerySceneScan(scene, scan);

    /// <summary>Reads held state through this scene's legacy scan adapter.</summary>
    public bool HeldLegacyScan(byte scan) => router.HeldSceneScan(scene, scan);

    /// <summary>Consumes menu navigation, confirmation, or cancellation for menu and town scenes.</summary>
    public bool Menu(GameAction action, bool allowRepeat = false) =>
        scene is InputScene.Menu or InputScene.Town && ControlProfile.IsMenu(action)
            && router.Menu(action, allowRepeat);

    /// <summary>Consumes one player's menu action in a scene menu.</summary>
    public bool PlayerMenu(int player, GameAction action, bool allowRepeat = false) =>
        scene is InputScene.Menu or InputScene.Town && ControlProfile.IsMenu(action)
            && router.PlayerMenu(player, action, allowRepeat);

    private bool Allows(GameAction action) => scene switch
    {
        InputScene.Menu => ControlProfile.IsMenu(action) || action == GameAction.Reference,
        InputScene.Town => action is >= GameAction.MenuLeft and <= GameAction.Back or GameAction.Pause or GameAction.Reference or GameAction.Status,
        InputScene.World => action is >= GameAction.Left and <= GameAction.Down or GameAction.Confirm or GameAction.Back
            or GameAction.Pause or GameAction.Reference or GameAction.Status,
        _ => false
    };
}

/// <summary>A non-allocating combat view over shared device samples and edge latches.</summary>
public readonly struct CombatInput
{
    private readonly InputRouter router;
    internal CombatInput(InputRouter router) => this.router = router;

    /// <summary>Gets the combat profile used for player ownership and movement rules.</summary>
    public ControlProfile Profile => router.Profile;
    /// <summary>Gets or sets the most recent combat action feedback.</summary>
    public string Notice { get => router.Notice; set => router.Notice = value; }
    /// <summary>Consumes a combat action edge.</summary>
    public bool Press(int player, GameAction action, bool allowRepeat = false) => Allows(action) && router.Press(player, action, allowRepeat);
    /// <summary>Consumes a configured direct move binding.</summary>
    public bool Press(int player, InputBinding[] bindings, bool allowRepeat = false) => router.Press(player, bindings, allowRepeat);
    /// <summary>Reads held movement or attack state in combat.</summary>
    public bool Held(int player, GameAction action) => Allows(action) && router.Held(player, action);
    /// <summary>Consumes a configured quick action, including trigger and direction chords.</summary>
    public bool PressQuick(int player, int slot, bool assigning = false) => router.PressQuick(player, slot, assigning);
    /// <summary>Clears pending actions for one player after a combat ownership change.</summary>
    public void ClearPlayer(int player) => router.ClearPlayer(player);
    /// <summary>Requests vibration for a physical controller when available.</summary>
    public void Rumble(int player) => router.Rumble(player);

    private static bool Allows(GameAction action) => action is GameAction.Attack or GameAction.Jump or GameAction.Execute or GameAction.Switch
        or GameAction.Run or GameAction.Branch or GameAction.Selector or GameAction.Left or GameAction.Right or GameAction.Up or GameAction.Down
        or >= GameAction.Quick1 and <= GameAction.Quick4
        or >= GameAction.Pause and <= GameAction.Automation
        or >= GameAction.BackDown and <= GameAction.ForwardUp;
}
