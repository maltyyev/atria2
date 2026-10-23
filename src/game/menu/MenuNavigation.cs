using Atria.Resources;

namespace Atria.Game.Menu;

/// <summary>Vertical list navigation, with the original horizontal bindings retained as aliases.</summary>
public sealed class MenuNavigation(ControlBindings bindings,Func<byte,bool> query)
{
    public bool Previous()=>query(bindings.Up)||query(bindings.Previous);
    public bool Next()=>query(bindings.Down)||query(bindings.Next);
}
