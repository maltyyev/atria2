using Atria.Game.State;

namespace Atria.Game.Combat;

public enum CombatOutcomeReturnEffectKind { ReloadWorldBackground,ReloadTownBackground,ResetStackAndPushGameOver,ResetMovement46027c }
public readonly record struct CombatOutcomeReturnEffect(CombatOutcomeReturnEffectKind Kind,int Value=0);

/// <summary>4043a0 after battle/platform restoration; shared by deferred and synchronous script starts.</summary>
public static class CombatOutcomeReturn
{
    public static void Apply(GameState state,int returnCode,ref int retainedBattleCounter,Action<CombatOutcomeReturnEffect> emit)
    {
        if(returnCode is not (1 or 2))throw new InvalidDataException("Combat return requires an actual terminal result.");
        state.EndingResetFlag=0;
        if(returnCode==2)
        {
            state.ScriptActive=0;state.ModeStackDepth=-1;state.Mode=1100;
            emit(new(CombatOutcomeReturnEffectKind.ResetStackAndPushGameOver,1100));return;
        }
        if(state.Mode is 300 or 330)emit(new(CombatOutcomeReturnEffectKind.ReloadWorldBackground));
        else if(state.Mode is 400 or 410 or 420)emit(new(CombatOutcomeReturnEffectKind.ReloadTownBackground));
        retainedBattleCounter=unchecked(retainedBattleCounter+1);
        if(retainedBattleCounter>1)
        {retainedBattleCounter=0;state.ResourceCounterDc88=unchecked(state.ResourceCounterDc88+1);}
        emit(new(CombatOutcomeReturnEffectKind.ResetMovement46027c));
    }
}
