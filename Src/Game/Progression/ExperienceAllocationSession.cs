using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Progression;

public enum ExperienceInput
{
    CycleMode,
    PreviousMember,
    NextMember,
    GiveFromSelectedHeld,
    TakeFromOthersHeld,
    Finish
}

/// <summary>424d40 allocation phase. Finish is the configured cancel/X rising edge, not confirm/C.</summary>
public sealed class ExperienceAllocationSession
{
    internal GameState Working { get; } = new();

    private readonly int[] allocation = new int[6];
    public IReadOnlyList<PartyMember> Party => Working.Party;
    public ReadOnlySpan<int> Allocation => allocation;
    public int Award
    {
        get;
    }
    public int ActiveCount
    {
        get;
    }
    public int SelectedMember
    {
        get; private set;
    }
    public int Mode
    {
        get; private set;
    }
    public bool Finished
    {
        get; private set;
    }

    private bool redraw = true;
    public bool RedrawPending => redraw;

    private uint nextRedraw;
    //424d40 aliases its redraw deadline with45ae24, consumed by battle backdrop40a820(0).
    public uint NativeRedrawDeadline => nextRedraw;

    internal int ConsumeAllocation(int actor)
    {
        int value = allocation[actor];
        allocation[actor] = 0;
        return value;
    }

    //424d40 redraws on control activity or strict tick > deadline, not on an assumed fixed UI tick.
    public bool TakeRedraw(uint milliseconds)
    {
        if (!redraw && milliseconds <= nextRedraw)
        {
            return false;
        }

        nextRedraw = unchecked(milliseconds + 5000);
        redraw = false;
        return true;
    }

    public ExperienceAllocationSession(ReadOnlySpan<byte> partyRecords, int award)
    {
        if (partyRecords.Length != GameState.PartyCount * GameState.PartyRecordSize)
        {
            throw new ArgumentException("Expected ten packed party records.");
        }

        PartyCodec.Decode(partyRecords, Working.Party);
        Award = award;
        for (int i = 0; i < 6; i++)
        {
            if (Working.GetMemberStatus(i) == 0)
            {
                continue;
            }

            ActiveCount++;
            ProgressionRules.ValidateRecord(Working, i);
        }

        if (ActiveCount == 0)
        {
            throw new InvalidDataException("XP allocation has no active party; native navigation would not terminate.");
        }

        for (int i = 0; i < 6; i++)
        {
            if (Working.GetMemberStatus(i) == 1)
            {
                allocation[i] = Award / ActiveCount;
                Working.Party[i].PendingAction = 0;
            }
        }
    }

    // Both press-edge and held queries are represented explicitly by their enum suffix.
    // Lazy query order matches424d40 and preserves consumed-edge priority.
    public void ApplyInput(Func<ExperienceInput, bool> query)
    {
        if (Finished)
        {
            throw new InvalidOperationException("XP allocation has finished.");
        }

        if (query(ExperienceInput.CycleMode) && Award > 0)
        {
            int nextMode = (Mode + 1) % 3;
            int[] next = new int[6];
            AllocateMode(nextMode, next);
            Mode = nextMode;
            next.CopyTo(allocation, 0);
            redraw = true;
        }
        else if (query(ExperienceInput.PreviousMember))
        {
            Navigate(-1);
            redraw = true;
        }
        else if (query(ExperienceInput.NextMember))
        {
            Navigate(1);
            redraw = true;
        }
        else if (query(ExperienceInput.GiveFromSelectedHeld))
        {
            GiveFromSelected();
            redraw = true;
        }
        else if (query(ExperienceInput.TakeFromOthersHeld))
        {
            TakeFromOthers();
            redraw = true;
        }

        if (query(ExperienceInput.Finish))
        {
            Finished = true;
        }
    }

    private void Navigate(int direction)
    {
        for (int n = 0; n < 6; n++)
        {
            SelectedMember = (SelectedMember + direction + 6) % 6;
            if (Working.GetMemberStatus(SelectedMember) != 0)
            {
                return;
            }
        }

        throw new InvalidDataException("No active XP recipient.");
    }

    private void AllocateMode(int mode, int[] result)
    {
        if (mode == 0)
        {
            for (int i = 0; i < 6; i++)
            {
                if (Working.GetMemberStatus(i) == 1)
                {
                    result[i] = Award / ActiveCount;
                }
            }
        }
        else if (mode == 2)
        {
            result[SelectedMember] = Award;
        }
        else
        {
            // Exact stable-index minimum-XP waterfill, batched by ties/next minimum rather
            // than looping Award times. Native sentinel999999999 is not an XP cap.
            long remaining = Award;
            int previousRecipient = -1;
            Span<int> tied = stackalloc int[6];
            while (remaining > 0)
            {
                long minimum = 999999999, next = 999999999;
                int count = 0;
                for (int i = 0; i < 6; i++)
                {
                    if (Working.GetMemberStatus(i) == 0)
                    {
                        continue;
                    }

                    long value = (long)Working.Party[i].Experience + result[i];
                    if (value >= 999999999)
                    {
                        continue;
                    }

                    if (value < minimum)
                    {
                        next = minimum;
                        minimum = value;
                        count = 0;
                        tied[count++] = i;
                    }
                    else if (value == minimum)
                    {
                        tied[count++] = i;
                    }
                    else if (value < next)
                    {
                        next = value;
                    }
                }

                if (count == 0)
                {
                    if (previousRecipient < 0)
                    {
                        throw new InvalidDataException("Native XP minimum search has no initial recipient; its allocation pointer is undefined.");
                    }
                    // EBX retains the previous valid recipient when every total reaches the sentinel.
                    // All remaining points stay with that actor, including a later signed total wrap.
                    result[previousRecipient] = checked(result[previousRecipient] + (int)remaining);
                    break;
                }

                long batch = Math.Min(remaining, (next - minimum) * count);
                if (batch == 0)
                {
                    throw new InvalidDataException("XP allocation has no bounded next minimum.");
                }

                long cycles = batch / count, extra = batch % count;
                for (int i = 0; i < count; i++)
                {
                    result[tied[i]] = checked(result[tied[i]] + (int)cycles + (i < extra ? 1 : 0));
                }

                previousRecipient = tied[(int)((batch - 1) % count)];
                remaining -= batch;
            }
        }
    }

    private void GiveFromSelected()
    {
        int amount = Math.Min(allocation[SelectedMember], ActiveCount * 5 - 5);
        allocation[SelectedMember] = unchecked(allocation[SelectedMember] - amount);
        while (amount > 0)
        {
            for (int i = 0; i < 6 && amount > 0; i++)
            {
                if (i != SelectedMember && Working.GetMemberStatus(i) != 0)
                {
                    allocation[i] = unchecked(allocation[i] + 1);
                    amount--;
                }
            }
        }
    }

    private void TakeFromOthers()
    {
        int amount = 0;
        for (int i = 0; i < 6; i++)
        {
            if (i == SelectedMember || Working.GetMemberStatus(i) == 0)
            {
                continue;
            }

            int part = Math.Min(allocation[i], 5);
            allocation[i] = unchecked(allocation[i] - part);
            amount = unchecked(amount + part);
        }

        allocation[SelectedMember] = unchecked(allocation[SelectedMember] + amount);
    }
}
