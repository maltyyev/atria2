using Atria.Game.Combat;
using Atria.Game.Progression;
using Atria.Game.State;
using Atria.Resources;

namespace Atria.Tests.Combat;

public static class CombatSessionBridgeTests
{
    public static string Run(string root)
    {
        var items=ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root,"ATITEM.DAT")));
        var state=Party();state.SetPartyInt(0,192,999);
        var effects=new InputTrace();var award=new CombatExperienceAward(effects);
        award.Run(state,1);
        Check(effects.Trace.SequenceEqual(new[]{"allocate","notice:0:2","finish-release"}),"Direct combat XP waits for player allocation, real notice completion and finish release.");
        Check(state.GetPartyInt(0,192)==1000 && state.GetPartyInt(0,200)==2 && state.GetPartyInt(0,256)==1 && state.GetPartyInt(0,100)==10,
            "424d40 commits XP/promotion but leaves403cc0 stat gain pending until outer restoration.");
        Check(state.PendingExperience==777 && state.ExperiencePending==9 && state.EndingResetFlag==1 && state.Mode==400,
            "Direct battle award does not route through scenario pending XP or clear caller transition/mode.");
        CombatExperienceAward.FinalizeReturnedStats(state,items);
        Check(state.GetPartyInt(0,256)==0 && state.GetPartyInt(0,100)==11 && state.GetPartyInt(0,108)==11,
            "Separate403ec0 boundary applies actual class0 rank2 gains and clears pending promotion.");
        var noAward=Party();noAward.SetPartyInt(0,252,99);
        CombatExperienceAward.FinalizeReturnedStats(noAward,items);
        Check(noAward.GetPartyInt(0,252)==InitialGameState.EquipmentCapacity(0,1),"Outer finalization updates equipment capacity even without an award/promotion.");
        var negative=Party();var negativeEffects=new InputTrace();new CombatExperienceAward(negativeEffects).Run(negative,-10);
        Check(negative.GetPartyInt(0,192)==-10 && negative.GetPartyInt(0,200)==1 && negativeEffects.Trace.SequenceEqual(new[]{"allocate","finish-release"}),
            "Signed native negative award still invokes allocation and finish; no fabricated clamp or promotion.");
        var incomplete=Party();byte[] before=incomplete.PartyRecords.ToArray();
        try { new CombatExperienceAward(new Unfinished()).Run(incomplete,1);throw new Exception("Incomplete player allocation accepted."); }
        catch(InvalidOperationException) {}
        Check(incomplete.PartyRecords.SequenceEqual(before) && incomplete.EndingResetFlag==1,"Unfinished modal cannot commit a fake battle award.");
        return "Combat session bridge: direct signed battle XP, real input/release boundary, deferred post-restoration stat gain and pending-state preservation passed.";
    }
    private static GameState Party()
    {
        var s=new GameState { PendingExperience=777,ExperiencePending=9,EndingResetFlag=1,Mode=400 };
        s.SetMemberStatus(0,1);s.SetPartyInt(0,200,1);s.SetPartyInt(0,196,1000);
        for(int at=100;at<=120;at+=4) s.SetPartyInt(0,at,10);
        for(int at=228;at<=248;at+=4) s.SetPartyInt(0,at,-1);
        return s;
    }
    //Explicit unit-test player/time trace, never a production default or a supplied battle result.
    private sealed class InputTrace : IExperienceModalEffects
    {
        public List<string> Trace { get; }=[];
        public uint Milliseconds=>100;
        public void Allocate(ExperienceAllocationSession session)
        { Trace.Add("allocate");session.ApplyInput(input=>input==ExperienceInput.Finish); }
        public void ShowLevelUp(LevelUpNotice notice,ReadOnlyMemory<byte> party)
        { Trace.Add($"notice:{notice.Actor}:{notice.Level}");notice.AfterRenderedFrame(4100,false); }
        public void Finish(AwardFinishRelease release) { Trace.Add("finish-release");release.Observe(false); }
    }
    private sealed class Unfinished : IExperienceModalEffects
    {
        public uint Milliseconds=>0;
        public void Allocate(ExperienceAllocationSession session) {}
        public void ShowLevelUp(LevelUpNotice notice,ReadOnlyMemory<byte> party)=>throw new Exception("Unexpected notice.");
        public void Finish(AwardFinishRelease release)=>throw new Exception("Unexpected finish.");
    }
    private static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
}
