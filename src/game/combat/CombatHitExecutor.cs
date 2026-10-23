using Atria.Game.State;

namespace Atria.Game.Combat;

/// <summary>Battle-owned reusable binding of406d40 to actual normal/lethal reactions, without per-hit closures.</summary>
public sealed class CombatHitExecutor
{
    private readonly CombatSceneRuntime runtime;
    private readonly GameState state;
    private readonly Func<int> nextRandom;
    private readonly Action<CombatHitParticle> particle;
    private readonly Action<int> sound;
    private readonly Action declaredActorDeathEffect;
    private readonly Action normalReaction;
    private readonly Action<CombatDeathReaction> deathReaction;
    private int attackerIndex, targetIndex;
    private uint milliseconds;
    private bool executing;
    public CombatHitState Hits { get; }

    public CombatHitExecutor(CombatSceneRuntime runtime, GameState state, Func<int> nextNativeRandom,
        Action<CombatHitParticle> spawnParticle, Action<int> playSound)
    {
        this.runtime=runtime; this.state=state; nextRandom=nextNativeRandom; particle=spawnParticle;
        sound=playSound; declaredActorDeathEffect=KillDeclaredMinions; Hits=new(runtime.Actors.Count);
        normalReaction=PrepareNormal; deathReaction=LaunchDeath;
    }
    public CombatHitResult Execute(int attackerIndex,int targetIndex,uint milliseconds,bool allowBackGuard=true)
    {
        if(executing) throw new InvalidOperationException("Native combat hit handling is not reentrant.");
        executing=true; this.attackerIndex=attackerIndex; this.targetIndex=targetIndex; this.milliseconds=milliseconds;
        try
        {
            return CombatHitRuntime.Apply(runtime,Hits,attackerIndex,targetIndex,state,milliseconds,allowBackGuard,
                nextRandom,particle,sound,normalReaction,deathReaction);
        }
        finally { executing=false; }
    }
    public CombatDamageResult ApplyRawDamage(int attackerIndex,int targetIndex,int amount,uint milliseconds)
    {
        if(executing) throw new InvalidOperationException("Native combat hit handling is not reentrant.");
        executing=true; this.attackerIndex=attackerIndex; this.targetIndex=targetIndex; this.milliseconds=milliseconds;
        try
        {
            CombatActorImage attacker=runtime.Actors[attackerIndex],target=runtime.Actors[targetIndex];
            return CombatDamageApplication.Apply(attacker.Fields,attacker.Binding!.Info.Type,target.Fields,target.Binding!.Info.Type,amount,deathReaction);
        }
        finally { executing=false; }
    }
    public void LaunchReaction(int attackerIndex,int targetIndex,CombatDeathReaction force,uint milliseconds)
    {
        if(executing) throw new InvalidOperationException("Native combat hit handling is not reentrant.");
        executing=true; this.attackerIndex=attackerIndex; this.targetIndex=targetIndex; this.milliseconds=milliseconds;
        try { LaunchDeath(force); }
        finally { executing=false; }
    }
    private void PrepareNormal()=>CombatHitReaction.Normal(runtime.Actors[attackerIndex],runtime.Actors[targetIndex],milliseconds,sound,declaredActorDeathEffect);
    private void LaunchDeath(CombatDeathReaction force)=>CombatHitReaction.Launch(runtime.Actors[attackerIndex],runtime.Actors[targetIndex],force,milliseconds,sound,declaredActorDeathEffect);
    //42e2d0: a flagged boss death launches every living unflagged declared actor; party tail is excluded.
    private void KillDeclaredMinions()
    {
        for(int i=0;i<runtime.Scene.PartyStartIndex;i++)
        {
            CombatActorImage image=runtime.Actors[i]; byte[] fields=image.Fields;
            if(I(fields,0x1ec)<=0 || I(fields,0x170)<=0 || fields[0x11b]!=0) continue;
            W(fields,0x170,0); W(fields,0xc1,unchecked(I(fields,0xc1)<<2)); W(fields,4,0);
            CombatHitReaction.Launch(runtime.Actors[attackerIndex],image,new(10,10,10,2,unchecked((sbyte)fields[0xfa])),milliseconds,sound,declaredActorDeathEffect);
        }
    }
    private static int I(byte[] bytes,int offset)=>System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset,4));
    private static void W(byte[] bytes,int offset,int value)=>System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset,4),value);
}
