using Atria.Game.Progression;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Game.Combat;

/// <summary>408570's direct424d40(...,0) call. This does not queue421980 pending XP or run403ec0 early.</summary>
public sealed class CombatExperienceAward(IExperienceModalEffects effects)
{
    private bool active;
    public void Run(GameState state,int signedAward)
    {
        if(signedAward!=unchecked((short)signedAward)) throw new ArgumentOutOfRangeException(nameof(signedAward),"Combat XP argument is a sign-extended native WORD.");
        if(active) throw new InvalidOperationException("Native XP modal cannot reenter itself.");
        byte[] originalParty=state.PartyRecords.ToArray();
        var session=new ExperienceAllocationSession(originalParty,signedAward);active=true;
        try
        {
            effects.Allocate(session);
            if(!session.Finished) throw new InvalidOperationException("Combat XP modal requires actual player finish input.");
            ProgressionRules.ApplyAward(session);
            for(int i=0;i<6;i++)
            {
                if(session.Working.GetMemberStatus(i)==0 || session.Working.GetPartyInt(i,256)!=1) continue;
                var notice=new LevelUpNotice(i,session.Working.GetPartyInt(i,200),effects.Milliseconds);
                effects.ShowLevelUp(notice,session.Party);
                if(!notice.Completed) throw new InvalidOperationException("Combat level-up notice returned without time/input completion.");
            }
            var release=new AwardFinishRelease();effects.Finish(release);
            if(!release.Released) throw new InvalidOperationException("Combat XP finish key must be released before battle cleanup.");
            if(!state.PartyRecords.AsSpan().SequenceEqual(originalParty))
                throw new InvalidOperationException("Party changed during its detached combat XP modal.");
            session.Working.PartyRecords.CopyTo(state.PartyRecords,0);
            //Leave per-member+256 level flags pending.404170/4043a0 call403ec0 AFTER platform restoration.
            //PendingExperience/ExperiencePending belong to the separate421980 scenario path and are untouched.
        }
        finally { active=false; }
    }
    /// <summary>403ec0, after restored exploration resources and before branching on terminal return1/2.</summary>
    public static void FinalizeReturnedStats(GameState state,ItemCatalog items)
        =>ProgressionRules.FinalizeStats(state,state,items);
}
