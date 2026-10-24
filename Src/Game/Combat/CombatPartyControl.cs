using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>404930 controlled actor cycling and404910 visual selection flash.</summary>
public static class CombatPartyControl
{
    //42e3b0 initializes native4cc3b0 to RGB565ffff.
    public static int Cycle(IReadOnlyList<CombatActorImage> actors, int partyStart, int currentIndex, ushort flashColor = 0xffff)
    {
        if (actors.Count - partyStart != 6 || currentIndex < partyStart || currentIndex >= actors.Count)
        {
            throw new ArgumentException("Combat control requires the actual six-slot party tail.");
        }

        CombatActorImage current = actors[currentIndex];
        CombatActorState source = current.State;
        if (source.ControlRole != 1)
        {
            for (int i = partyStart; i < actors.Count; i++)
            {
                actors[i].State.ControlRole = 0;
            }

            source.ControlRole = 1;
            return currentIndex;
        }

        byte control = source.ControlledBy;
        int player = current.ControlPlayer;
        if (control == 0 || player < 0)
        {
            throw new InvalidOperationException("Selected combat actor has no actual control binding.");
        }

        source.ControlRole = 0;
        int first = partyStart;
        while (first < actors.Count && actors[first].State.ControlledBy != control)
        {
            first++;
        }

        current.ControlPlayer = -1;
        source.ControlledBy = 0;
        int selected = first + 1;
        while (selected < actors.Count
&& !(actors[selected].State.Active != 0 && actors[selected].State.ControlledBy == 0 && actors[selected].State.Health > 0))
        {
            selected++;
        }

        if (selected >= actors.Count)
        {
            selected = partyStart;
            while (selected < actors.Count && !(actors[selected].State.Active != 0 && actors[selected].State.ControlledBy == 0))
            {
                selected++;
            }
        }

        if (selected == actors.Count)
        {
            throw new InvalidDataException("Native combat control cycling has no bounded active destination.");
        }

        CombatActorImage destination = actors[selected];
        destination.State.ControlledBy = control;
        destination.ControlPlayer = player;
        destination.State.ControlRole = 1;
        Flash(destination.State, flashColor);
        return selected;
    }

    public static void Flash(CombatActorState actor, ushort color)
    {
        actor.FlashTicks = 60;
        actor.FlashColor = unchecked((int)(color));
    }

}
