using Atria.Core.Game.ActionMenu;
using Atria.Core.Game.State;
using Atria.Core.Game.Town;
using Atria.Core.Presentation.ActionMenu;
using Atria.Core.Presentation.Town;

namespace Atria.Core.Game;

/// <summary>Discard previous-playthrough scene context before entering the new game's first town.</summary>
public static class NewGameSceneReset
{
    public static void Apply(GameState state, TownController town, ActionMenuController actions)
    {
        town.SetSavedSelection(0);
        actions.CloseAll();
        state.ScriptActive = 0;
        state.People = Array.Empty<ScriptPerson>();
    }
}
