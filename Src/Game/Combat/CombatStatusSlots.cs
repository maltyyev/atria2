namespace Atria.Game.Combat;

/// <summary>40b850 exact refresh/expiry/overwrite selection over3or10 native24-byte status slots.</summary>
public static class CombatStatusSlots
{
    public const int PersonalCount = 3;
    public const int GlobalCount = 10;
    public static int Insert(Span<CombatStatusSlot> slots, int code, int bankFlag, int teamSide, int deadline, int sourceType, int milliseconds)
    {
        int count = bankFlag == 0 ? PersonalCount : GlobalCount;
        if (slots.Length < count)
        {
            throw new ArgumentException("Incomplete native status-slot bank.");
        }

        int storedCode = unchecked(code - 1), selected = -1;
        for (int i = 0; i < count; i++)
        {
            if (slots[i].Code == storedCode)
            {
                selected = i;
                break;
            }
        }

        if (selected < 0)
        {
            for (int i = 0; i < count; i++)
            {
                if (slots[i].Deadline < milliseconds)
                {
                    selected = i;
                    break;
                }
            }
        }

        if (selected < 0)
        {
            selected = 0;
        }

        ref var slot = ref slots[selected];
        slot.Deadline = deadline;
        slot.Code = storedCode;
        slot.BankFlag = bankFlag;
        slot.TeamSide = sourceType < CombatCharacterTypes.FirstEnemy ? teamSide : teamSide == 0 ? 1 : 0;
        slot.BlinkDeadline = 0;
        // Original resets the first slot's blink state even when refreshing another slot.
        slots[0].BlinkVisible = 0;
        return selected;
    }

}
