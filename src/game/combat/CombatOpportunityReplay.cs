namespace Atria.Game.Combat;

public enum CombatRecordedOpportunityKind { Outer,FrozenRender,CachedPresentation }
public readonly record struct CombatRecordedOpportunity(CombatRecordedOpportunityKind Kind,uint Milliseconds);
public readonly record struct CombatReplaySummary(int Consumed,int OuterOpportunities,int FrozenRenders,int CachedPresentations,
    bool Terminal,CombatFrameResult? Outcome,ulong LogicalFrames);

/// <summary>Bounded serial trace replay. It does not produce time, choose a frequency or synthesize missed opportunities.</summary>
public static class CombatOpportunityReplay
{
    public static CombatReplaySummary Run(CombatSerialCoordinator coordinator,IEnumerable<CombatRecordedOpportunity> recording,
        int maximumOpportunities,Action<int,CombatRecordedOpportunity>? beforeOpportunity=null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);ArgumentNullException.ThrowIfNull(recording);
        if(maximumOpportunities<1)throw new ArgumentOutOfRangeException(nameof(maximumOpportunities));
        int consumed=0,outer=0,frozen=0,present=0;uint replayTime=0;
        Func<uint> replayClock=()=>replayTime;
        if(coordinator.Completed)return new(0,0,0,0,true,coordinator.TerminalResult,coordinator.FrameSerial);
        foreach(var entry in recording)
        {
            if(coordinator.Completed)break;
            if(consumed>=maximumOpportunities)throw new InvalidDataException("Combat recording exceeds its explicit opportunity bound.");
            beforeOpportunity?.Invoke(consumed,entry);consumed++;
            switch(entry.Kind)
            {
                case CombatRecordedOpportunityKind.Outer:
                    replayTime=entry.Milliseconds;coordinator.OuterOpportunity(replayClock);outer++;break;
                case CombatRecordedOpportunityKind.FrozenRender:
                    if(entry.Milliseconds!=coordinator.LastSampledMilliseconds)throw new InvalidDataException("Frozen render must retain the original outer clock sample.");
                    coordinator.FrozenRenderOpportunity();frozen++;break;
                case CombatRecordedOpportunityKind.CachedPresentation:
                    if(coordinator.TryPresentCached())present++;break;
                default:throw new InvalidDataException("Unknown recorded combat opportunity.");
            }
            if(coordinator.Completed)break;
        }
        return new(consumed,outer,frozen,present,coordinator.Completed,coordinator.TerminalResult,coordinator.FrameSerial);
    }
}
