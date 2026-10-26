using Atria.Resources;

namespace Atria.Game.Combat;

/// <summary>407fe0 scheduled global UI and4045f0/404640 shared sliding notice.</summary>
public sealed class CombatGlobalUiRuntime(CombatRenderSchedule schedule, BattleMessages messages)
{
    private const int Hidden = 0, Entering = 1, Holding = 2, Leaving = 3;
    private const int NoticeEntryX = 410, NoticeTop = 70, NoticeExitX = 10;
    private const int SlideIntervalMilliseconds = 30, SlidePixelsPerTick = 10;
    private const int NoticeLineCharacters = 80, HalfGlyphWidth = 4;
    private const uint NoticeHoldMilliseconds = 3_000;
    private const int ManaRecoveryMilliseconds = 1_000;
    public CombatRenderSchedule Schedule { get; } = schedule;
    public int NoticePhase
    {
        get; private set;
    }
    public int NoticeX
    {
        get; private set;
    }
    public int NoticeY
    {
        get; private set;
    }
    public uint NoticeDeadline
    {
        get; private set;
    }
    public int NoticeInterval
    {
        get; private set;
    }
    public ReadOnlyMemory<byte> NoticeText
    {
        get
        {
            ReadOnlyMemory<byte> text = noticeLabels is null ? ReadOnlyMemory<byte>.Empty : noticeLabels[noticeIndex];
            int end = text.Span.IndexOf((byte)0);
            return end < 0 ? text : text[..end];
        }
    }
    private IReadOnlyList<ReadOnlyMemory<byte>>? noticeLabels;
    private int noticeIndex;
    public uint SampledNow
    {
        get; private set;
    }
    public int VisualDetail
    {
        get; private set;
    }
    public bool SettingsNoticeVisible => unchecked((int)SampledNow) < unchecked((int)Schedule.SettingsNoticeDeadline);

    public bool ShowRankNotice(int rank) => Show(rank, messages.Ranks);
    public bool ShowConsumedItemNotice(int index) => Show(index, messages.Items);
    private bool Show(int index, IReadOnlyList<ReadOnlyMemory<byte>> labels)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Negative notice would address outside the native label bank.");
        }

        if (index >= labels.Count)
        {
            return false; //Actual4045f0/404640 upper bound leaves an existing notice unchanged.
        }

        noticeLabels = labels;
        noticeIndex = index;
        NoticeDeadline = 0;
        NoticeX = NoticeEntryX;
        NoticeY = NoticeTop;
        NoticeInterval = SlideIntervalMilliseconds;
        NoticePhase = Entering;
        return true;
    }

    /// <summary>Call AFTER40a820 and HUD state/draw. New detail applies to the following world raster.</summary>
    public int Advance(Func<byte, bool> query, uint sampledNow, int visualDetail, CombatActorState currentActor)
    {
        if ((uint)visualDetail > 3)
        {
            throw new ArgumentException("Global combat UI requires actual detail and current actor image.");
        }

        SampledNow = sampledNow;
        VisualDetail = visualDetail;
        Schedule.PollSpeedKeys(query, sampledNow); //E has lazy priority over R.
        if (query(0x10))
        {
            if (VisualDetail > 0)
            {
                VisualDetail--;
            }

            Schedule.RefreshSettingsNotice(sampledNow);
        }
        else if (query(0x11))
        {
            if (VisualDetail < 3)
            {
                VisualDetail++;
            }

            Schedule.RefreshSettingsNotice(sampledNow);
        }

        AdvanceNotice();
        RegenerateMagic(currentActor, sampledNow);
        return VisualDetail;
    }

    private void AdvanceNotice()
    {
        int now = unchecked((int)SampledNow);
        if (NoticePhase == Holding)
        {
            if (unchecked((int)NoticeDeadline) < now)
            {
                NoticePhase = Leaving;
                NoticeInterval = SlideIntervalMilliseconds;
            }

            return; //Native transition to exiting does not move in this call.
        }

        if (NoticePhase is not (Entering or Leaving) || unchecked((int)NoticeDeadline) >= now)
        {
            return;
        }

        NoticeDeadline = unchecked(SampledNow + (uint)NoticeInterval);
        NoticeX = unchecked(NoticeX - SlidePixelsPerTick);
        if (NoticePhase == Leaving)
        {
            if (NoticeX < NoticeExitX)
            {
                NoticePhase = Hidden;
            }
        }
        else if (NoticeX <= unchecked((NoticeLineCharacters - NoticeText.Length) * HalfGlyphWidth))
        {
            NoticeDeadline = unchecked(SampledNow + NoticeHoldMilliseconds);
            NoticePhase = Holding;
        }
    }

    private static void RegenerateMagic(CombatActorState actor, uint now)
    {
        bool equipped = false;
        for (int i = 0; i < actor.Equipment.Length; i++)
        {
            if (actor.Equipment[i] == ItemIds.ManaRing)
            {
                equipped = true;
                break;
            }
        }

        if (!equipped || actor.ManaRecoveryDeadline >= unchecked((int)now))
        {
            return;
        }

        int current = actor.Mana;
        if (current < actor.MaximumMana)
        {
            actor.Mana = unchecked(current + 1);
        }

        actor.ManaRecoveryDeadline = unchecked((int)now + ManaRecoveryMilliseconds); //Refresh even when already full.
    }

}
