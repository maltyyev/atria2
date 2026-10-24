namespace Atria.Game.Combat;

/// <summary>0x4083d0/0x407fe0 logical-render clock. It is not an actor simulation frequency.</summary>
public sealed class CombatRenderSchedule
{
    public const int MinimumPeriod = 25, MaximumPeriod = 225, PeriodStep = 20;
    public uint Deadline
    {
        get; private set;
    }
    public int Period
    {
        get; private set;
    }
    public uint SettingsNoticeDeadline
    {
        get; private set;
    }

    public CombatRenderSchedule(uint initialSample, int initialPeriod = MinimumPeriod)
    {
        if (initialPeriod < MinimumPeriod || initialPeriod > MaximumPeriod || (initialPeriod - MinimumPeriod) % PeriodStep != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialPeriod), "Period is outside the recovered25+20n sequence.");
        }

        Deadline = initialSample;
        Period = initialPeriod;
    }

    //0x40849c/0x40849e: original signed CMP/JL, including the original signed-tick boundary behavior.
    public bool IsDue(uint sampledNow) => unchecked((int)sampledNow) >= unchecked((int)Deadline);
    /// <summary>Invoke after stateful render, global UI and native presentation. No replay of missed renders.</summary>
    public void CompleteLogicalRender(uint sampledNow) => Deadline = unchecked(sampledNow + (uint)Period);
    //0x45ace8 is shared with the detail keys; their actual input handler may call this same refresh.
    public void RefreshSettingsNotice(uint sampledNow) => SettingsNoticeDeadline = unchecked(sampledNow + 5000);
    /// <summary>0x407fe0: E/0x12 takes priority over R/0x13; even a bounded key refreshes the notice.</summary>
    public bool PollSpeedKeys(Func<byte, bool> query, uint sampledNow)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query(0x12))
        {
            if (Period > MinimumPeriod)
            {
                Period -= PeriodStep;
            }

            RefreshSettingsNotice(sampledNow);
            return true;
        }

        if (query(0x13))
        {
            if (Period < MaximumPeriod)
            {
                Period += PeriodStep;
            }

            RefreshSettingsNotice(sampledNow);
            return true;
        }

        return false;
    }
}
