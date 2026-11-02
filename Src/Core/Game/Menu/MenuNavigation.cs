using Atria.Core.Resources;
using Atria.Core.Runtime.Input;

namespace Atria.Core.Game.Menu;

/// <summary>Vertical list navigation, with the original horizontal bindings retained as aliases.</summary>
public sealed class MenuNavigation(ControlBindings bindings, MenuInputView input)
{
    public bool Previous() => input.QueryLegacyMenuScan(bindings.Up) || input.QueryLegacyMenuScan(bindings.Previous);
    public bool Next() => input.QueryLegacyMenuScan(bindings.Down) || input.QueryLegacyMenuScan(bindings.Next);
}
