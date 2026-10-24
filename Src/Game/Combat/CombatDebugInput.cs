using Atria.Game.State;

namespace Atria.Game.Combat;

/// <summary>4083d0 held scan3+scan9 shortcut, before the timed visual stage.</summary>
public static class CombatDebugInput
{
    //Root46dc10+0x8c=party46dc9c. Root+200 -> party+60; root+0x14c -> party+192.
    public static int ApplyHeldShortcut(GameState state, CombatScanInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        if (!input.IsHeld(3) || !input.IsHeld(9))
        {
            return 0;
        }

        int changed = 0;
        for (int actor = 0; actor < 6; actor++)
        {
            if (state.GetMemberStatus(actor) != 1)
            {
                continue;
            }

            state.Party[actor].Experience = unchecked(state.Party[actor].Experience + 10);
            changed++;
        }

        return changed;
    }
}
