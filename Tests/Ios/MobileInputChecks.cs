using Atria.Core.Contracts.Host;
using Atria.Core.Persistence.Controls;
using Atria.Core.Resources;
using Atria.Core.Runtime.Input;
using Atria.Platforms.Ios;

namespace Tests.Ios;

public static class MobileInputChecks
{
    public static string RunContextHandoff()
    {
        var bindings = ControlBindings.Parse(new byte[64]);
        var window = new MobileWindow();
        using var router = new InputRouter(ControlProfile.Defaults(bindings), bindings, window);

        router.SetMobileContext(1, MobileOverlayKind.Navigation, directionsAreMenu: true);
        window.PublishUiSnapshot(Snapshot(1, 1));
        window.SubmitTouchState(new(1, 0, 0, MobileTouchButtons.A));
        router.Poll(window);
        Check(router.MenuInput.Menu(MenuAction.Confirm), "Initial-context A did not reach Core.");

        // Match GameSession: Core switches context before publishing the new snapshot.
        router.SetMobileContext(2, MobileOverlayKind.Navigation, directionsAreMenu: true);
        window.PublishUiSnapshot(Snapshot(2, 2));
        window.SubmitTouchState(new(2, 0, 0, MobileTouchButtons.None)); // Swift's reset sample.
        router.Poll(window);

        window.SubmitTouchState(new(2, 0, 0, MobileTouchButtons.A));
        window.SubmitTouchState(new(2, 0, 0, MobileTouchButtons.None));
        router.Poll(window);
        Check(router.MenuInput.Menu(MenuAction.Confirm), "First fresh A press after a context change was lost.");

        window.SubmitTouchState(new(2, 0, 0, MobileTouchButtons.A));
        router.Poll(window);
        Check(router.MenuInput.Menu(MenuAction.Confirm), "A did not rearm after a release.");
        router.Poll(window);
        Check(!router.MenuInput.Menu(MenuAction.Confirm), "A repeated while the contact stayed held.");

        router.SetMobileContext(3, MobileOverlayKind.Navigation, directionsAreMenu: false);
        window.PublishUiSnapshot(Snapshot(3, 3));
        window.SubmitTouchState(new(3, 0, 0, MobileTouchButtons.Right));
        window.SubmitTouchState(new(3, 0, 0, MobileTouchButtons.None));
        window.SubmitTouchState(new(3, 0, 0, MobileTouchButtons.Right));
        window.SubmitTouchState(new(3, 0, 0, MobileTouchButtons.None));
        router.Poll(window);
        Check(router.WorldInput.Press(0, WorldAction.Right)
            && router.WorldInput.Press(0, WorldAction.Right)
            && !router.WorldInput.Press(0, WorldAction.Right),
            "Two rapid world-direction taps were not preserved as separate one-shot actions.");

        return "mobile context handoff: fresh press delivered, held press does not repeat";
    }

    private static MobileUiSnapshot Snapshot(ulong context, ulong revision) => new(
        context,
        revision,
        "dialogue",
        MobileOverlayKind.Navigation,
        false,
        new("up", "down", "left", "right", "confirm", "back", "attack", "jump", "modifier", "language", "movement", "profiles"));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
