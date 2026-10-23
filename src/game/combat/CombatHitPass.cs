using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>407520 actor-order hit pass, including one outer DD latch gate and per-target updates.</summary>
public sealed class CombatHitPass(CombatSceneRuntime runtime, CombatHitExecutor executor)
{
    public void Run(uint milliseconds)
    {
        for(int a=0;a<runtime.Actors.Count;a++)
        {
            CombatActorImage attacker=runtime.Actors[a]; byte[] record=attacker.Fields;
            if(I(record,0x224)==0 || record[0xdd]==0 || (I(record,0xad)&0x8000)!=0 ||
                BinaryPrimitives.ReadInt16LittleEndian(record.AsSpan(0xff,2))==BinaryPrimitives.ReadInt16LittleEndian(record.AsSpan(0x103,2))) continue;
            if(attacker.Animation is null || attacker.Binding is null) throw new InvalidOperationException("Active hit actor has no actual bindings.");
            for(int d=0;d<runtime.Actors.Count;d++)
            {
                // Original clears DD on every target iteration; it does not recheck DD inside this pass.
                record[0xdd]=0;
                CombatActorImage target=runtime.Actors[d];
                if(d==a || I(target.Fields,0x170)<=0) continue;
                if(target.Animation is null)
                {
                    // The native unused slot has degenerate zero rectangles and cannot be hit.
                    if(BinaryPrimitives.ReadInt16LittleEndian(target.Fields.AsSpan(0x10b,2))==BinaryPrimitives.ReadInt16LittleEndian(target.Fields.AsSpan(0x107,2))) continue;
                    throw new InvalidOperationException("Potential combat target has no actual animation binding.");
                }
                if(!CombatHitEligibility.CanHit(a,d,record,attacker.Binding.Info.Type,attacker.Animation.CurrentFrame,
                    target.Fields,target.Animation.CurrentFrame,false)) continue;
                W(record,0x341,1); W(target.Fields,0xb9,0);
                if(record[0xa4]!=0) { runtime.TargetIndices[a]=d; W(record,0xa9,unchecked((int)milliseconds+7000)); }
                executor.Execute(a,d,milliseconds);
                if(I(target.Fields,0xb1)!=14)
                {
                    int interval=attacker.Animation.CurrentFrame.Words[15]; if(interval==0) interval=80;
                    W(target.Fields,0xc1,CombatActorMaintenance.ModifyActionInterval(target.Fields,interval));
                }
                CombatActorMaintenance.CaptureSecondaryAction(record,attacker.Animation.CurrentFrame);
            }
        }
    }
    private static int I(byte[] actor,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset,4));
    private static void W(byte[] actor,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset,4),value);
}
