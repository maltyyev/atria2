using System.Buffers.Binary;
using Atria.Game.State;

namespace Atria.Game.Combat;

public interface ICombatActorActions
{
    void Process(int actorIndex,uint milliseconds);
}
public interface ICombatFrameEffects
{
    void PlaySound(int id);
    void SpawnActorParticle(int actorIndex,int id,uint milliseconds);
    void ApplyFrameEffect(int actorIndex,int id,int argument,uint milliseconds);
    void ShowConsumedItemNotice(int noticeIndex,uint milliseconds);
}
public readonly record struct CombatFrameResult(int ReturnCode,bool RewardFlag,int FriendlyCount,int EnemyCount,int PendingCount,int AlliedEnemyCount);

/// <summary>407e10 actual actor-order update, pending wave counters and native result/reward distinction.</summary>
public sealed class CombatFrameRuntime
{
    private readonly CombatSceneRuntime runtime;
    private readonly GameState state;
    private readonly CombatCameraBounds camera;
    private readonly ICombatActorActions actions;
    private readonly ICombatFrameEffects effects;
    private readonly Func<int> nextRandom;
    private readonly Action<int> spawnParticle,consumedNotice,playSound;
    private readonly Action<int,int> frameEffect;
    private readonly Action cycle;
    private int currentActor;
    private uint milliseconds;
    public CombatCameraShake Shake { get; }=new();
    public int EncounterCounter { get; private set; }
    public int EmptyPassCounter { get; private set; }
    public int PreviousEnemyCount { get; private set; }=100; //413930
    public bool RewardFlag => state.Globals[61]==1;
    public int VisualDetail { get; set; }
    public CombatFrameRuntime(CombatSceneRuntime runtime,GameState state,CombatCameraBounds camera,
        ICombatActorActions actions,ICombatFrameEffects effects,Func<int> nextNativeRandom,bool? initialRewardFlag=null)
    {
        this.runtime=runtime;this.state=state;this.camera=camera;this.actions=actions;this.effects=effects;nextRandom=nextNativeRandom;
        if(initialRewardFlag.HasValue) state.Globals[61]=(short)(initialRewardFlag.Value?1:0);spawnParticle=SpawnParticle;frameEffect=ApplyFrameEffect;consumedNotice=ConsumedNotice;playSound=effects.PlaySound;cycle=Cycle;
    }
    public CombatFrameResult Step(uint milliseconds)=>Step(milliseconds,state.Globals[60]==1);
    public CombatFrameResult Step(uint milliseconds,bool returnOnNoFriendly)
    {
        this.milliseconds=milliseconds;int friendly=0,enemies=0,pending=0,allied=0;bool spawned=false;
        state.Globals[68]=0; //407e33 WORD reset, adjacent global69 is independent.
        for(int i=0;i<runtime.Actors.Count;i++)
        {
            currentActor=i;CombatActorImage image=runtime.Actors[i];byte[] actor=image.Fields;
            if(I(actor,0x60)==0)
            {
                if(I(actor,0x224)==0) continue;
                if(image.Binding is null || image.Animation is null) throw new InvalidOperationException("Active combat actor lacks actual bindings.");
                CombatActorMaintenance.RegenerateAndUpdateDisplay(actor,milliseconds);
                if(I(actor,0x2ac)!=0) W(actor,0xc1,CombatActorMaintenance.ModifyActionInterval(actor,80));
                if(image.Binding.Info.Type<8) friendly++;
                else if(unchecked((sbyte)actor[0xa6])<100) enemies++;
                else { allied++;state.Globals[68]=unchecked((short)(state.Globals[68]+1)); }
                if((I(actor,0xad)&0x200)==0)
                {
                    actions.Process(i,milliseconds);image.Animation.RebindFromFrameCounter();
                    CombatSpriteProjection.Update(actor,image.Binding.Info.Type,image.Animation.CurrentFrame,image.Animation.CurrentSprite);
                }
                camera.Update(i);
                CombatAnimationRuntime.Advance(image,state,milliseconds,VisualDetail,nextRandom,playSound,spawnParticle,frameEffect,consumedNotice,cycle,Shake);
                CombatSpriteProjection.UpdateTrackingX(actor,image.Animation.CurrentSprite);
            }
            else
            {
                CombatSpawnController.Update(actor,EncounterCounter,PreviousEnemyCount,-80,camera.Primary.Width+80,
                    runtime.Scene.Header73,runtime.Scene.Header77,milliseconds,nextRandom);
                if(I(actor,0x60)==0) { spawned=true;enemies++; }
                pending++;
            }
        }
        PreviousEnemyCount=enemies;
        state.Globals[51]=unchecked((short)(enemies+pending));
        state.Globals[52]=unchecked((short)friendly);
        int code=0;
        if(friendly==0)
        {
            code=returnOnNoFriendly?1:2;if(returnOnNoFriendly) state.Globals[61]=0;
        }
        else if(enemies==0 && pending==0) { state.Globals[61]=1;code=1; }
        else
        {
            if(spawned) EncounterCounter=unchecked(EncounterCounter+1);
            if(enemies==0 && pending!=0 && !spawned)
            {
                if(EmptyPassCounter>3) { EncounterCounter=unchecked(EncounterCounter+1);EmptyPassCounter=0; }
                else EmptyPassCounter=unchecked(EmptyPassCounter+1);
            }
        }
        return new(code,RewardFlag,friendly,enemies,pending,allied);
    }
    private void SpawnParticle(int id)=>effects.SpawnActorParticle(currentActor,id,milliseconds);
    private void ApplyFrameEffect(int id,int argument)=>effects.ApplyFrameEffect(currentActor,id,argument,milliseconds);
    private void ConsumedNotice(int id)=>effects.ShowConsumedItemNotice(id,milliseconds);
    private void Cycle()=>CombatPartyControl.Cycle(runtime.Actors,runtime.Scene.PartyStartIndex,currentActor);
    private static int I(byte[] actor,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset,4));
    private static void W(byte[] actor,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset,4),value);
}
