using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Progression;

public readonly record struct LevelUpNoticeFrame(int Actor, int Level, int TextX, int TextY, int ShadowX, int ShadowY);
/// <summary>Native per-actor four-second levelup.wav notice; confirm is HELD, not rising-edge.</summary>
public sealed class LevelUpNotice
{
    public int Actor
    {
        get;
    }
    public int Level
    {
        get;
    }
    public uint Deadline
    {
        get; private set;
    }
    public bool Completed
    {
        get; private set;
    }

    private int y;
    public LevelUpNoticeFrame Frame => new(Actor, Level, 132, y, 133, y - 1);

    public LevelUpNotice(int actor, int level, uint milliseconds)
    {
        if (actor < 0 || level is < 1 or > 13)
        {
            throw new ArgumentOutOfRangeException(nameof(actor));
        }

        Actor = actor;
        Level = level;
        y = 129 + Math.Min(actor, ExperienceAllocationRaster.VisibleRows - 1) * 22;
        Deadline = unchecked(milliseconds + 4000);
    }

    // Invoke after rendering/presenting Frame; native always draws once even if confirm is already held.
    public void AfterRenderedFrame(uint milliseconds, bool confirmHeld)
    {
        if (Completed)
        {
            throw new InvalidOperationException("Level-up notice has finished.");
        }

        y = Math.Max(10, y - 1);
        Completed = confirmHeld || milliseconds >= Deadline; //4250a2 unsigned direct deadline comparison.
    }

    public void PostponeForFocusPause(uint milliseconds) => Deadline = unchecked(Deadline + milliseconds);
}

public sealed class AwardFinishRelease
{
    public bool Released
    {
        get; private set;
    }

    public void Observe(bool finishHeld)
    {
        if (!finishHeld)
        {
            Released = true;
        }
    }
}

public interface IExperienceModalEffects
{
    uint Milliseconds
    {
        get;
    }

    /// <summary>Render real allocation UI and query configured input until Finished (cancel/X).</summary>
    void Allocate(ExperienceAllocationSession session);
    /// <summary>Play actual levelup.wav, render at least one Frame, then poll held confirm/time until Completed.</summary>
    void ShowLevelUp(LevelUpNotice notice, IReadOnlyList<PartyMember> awardedParty);
    /// <summary>Poll input until cancel/X is released, then stop the native notice sound and release UI assets.</summary>
    void Finish(AwardFinishRelease release);
}

/// <summary>421980 ->424d40 ->403ec0. Interactive outcomes are required, never successful stubs.</summary>
public sealed class PendingExperienceRuntime(ItemCatalog items, IExperienceModalEffects effects)
{
    private bool active;
    public bool ProcessPending(GameState state)
    {
        if (state.ExperiencePending == 0)
        {
            return false;
        }

        if (state.NewCharacterPending != 0)
        {
            throw new InvalidOperationException("New-character processing precedes pending XP in4219b0 and requires its own recovered handler.");
        }

        if (active)
        {
            throw new InvalidOperationException("XP allocation uses singleton native UI state and cannot reenter itself.");
        }

        byte[] originalParty = (byte[])PartyCodec.Encode(state.Party).Clone();
        int award = state.PendingExperience, pendingFlag = state.ExperiencePending;
        var session = new ExperienceAllocationSession(originalParty, award, state.HeroCatalog);
        active = true;
        try
        {
            effects.Allocate(session);
            if (!session.Finished)
            {
                throw new InvalidOperationException("XP modal returned without actual finish input.");
            }

            ProgressionRules.ApplyAward(session);
            for (int i = 0; i < session.Party.Count; i++)
            {
                if (session.Working.Heroes[i].MemberStatus == 0 || session.Working.Heroes[i].PendingAction != 1)
                {
                    continue;
                }

                var notice = new LevelUpNotice(i, session.Working.Heroes[i].Level, effects.Milliseconds);
                effects.ShowLevelUp(notice, session.Party);
                if (!notice.Completed)
                {
                    throw new InvalidOperationException("Level-up UI returned before its real time/input completion.");
                }
            }

            var release = new AwardFinishRelease();
            effects.Finish(release);
            if (!release.Released)
            {
                throw new InvalidOperationException("XP finish key must be released before script execution resumes.");
            }

            ProgressionRules.FinalizeStats(session.Working, state, items);
            if (!PartyCodec.Encode(state.Party).AsSpan().SequenceEqual(originalParty) || state.PendingExperience != award || state.ExperiencePending != pendingFlag)
            {
                throw new InvalidOperationException("Party/pending XP changed during its detached modal transaction.");
            }

            PartyCodec.Copy(session.Working.Party, state.Party);
            state.PendingExperience = 0;
            state.ExperiencePending = 0;
            return true;
        }
        finally
        {
            active = false;
        }
    }
}
