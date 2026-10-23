using Atria.Game.State;
using System.Buffers.Binary;

namespace Atria.Game.Combat;

public readonly record struct CombatOutcomeInput(int ReturnCode,bool RewardEligible,int BaseAward,int ComboTotal,int Variant,int RegionIndex);
public readonly record struct CombatOutcomeReward(int FullValue,short SignedAward,int MoneyDelta);
public enum CombatOutcomeEffectKind { AwardExperience,ReleaseBattleMenu,ReleaseCombatResources,ResetDisplayAndClear }
public readonly record struct CombatOutcomeEffect(CombatOutcomeEffectKind Kind,int Value=0);

/// <summary>408570 after actual4083d0 returns. Never decides victory or fabricates combat.</summary>
public static class CombatOutcomeRewards
{
    public static CombatOutcomeReward Calculate(int baseAward,int comboTotal,int variant,int region)
    {
        // User-approved balance policy: ignore combo, apply original encounter adjustments,
        // then double the resulting baseline. Hit/combo mechanics themselves are unchanged.
        _=comboTotal;
        if(baseAward<0 || (uint)region>9)throw new ArgumentOutOfRangeException(nameof(baseAward),"Invalid reward base or region.");
        long value=baseAward;
        switch(unchecked((short)variant))
        {
            case 0:value+=region>=5?region*510:region==4?region*410:region==3?region*310:region>1?region*110:0;break;
            case 1:value+=value/2;value+=region>=4?region*510:region==3?region*250:0;break;
            case 2:value/=2;value+=region>=5?region*250:region==4?region*200:region==3?region*150:region>1?region*50:0;break;
        }
        if(value==0)value=10;
        value*=2;
        // The existing script/XP channel is a signed WORD. Never wrap a large reward negative.
        short award=(short)Math.Min(value,short.MaxValue);
        return new((int)Math.Min(value,int.MaxValue),award,award/10);
    }

    /// <summary>Historical reconstruction, retained for evidence tests only; not the active reward policy.</summary>
    public static CombatOutcomeReward CalculateOriginal(int baseAward,int comboTotal,int variant,int region)
    {
        int value=unchecked(baseAward+(comboTotal>>3));
        unchecked
        {
            switch(unchecked((short)variant))
            {
                case 0:value+=region>=5?region*510:region==4?region*410:region==3?region*310:region>1?region*110:0;break;
                case 1:value+=value/2;value+=region>=4?region*510:region==3?region*250:0;break;
                case 2:value/=2;value+=region>=5?region*250:region==4?region*200:region==3?region*150:region>1?region*50:0;break;
            }
        }
        if(value==0)value=10;
        short award=unchecked((short)value);return new(value,award,award/10);
    }

    public static void CopyEquipmentBack(GameState state,IReadOnlyList<CombatActorImage> actors,int partyStart)
    {
        if(partyStart<0||partyStart>actors.Count-6)throw new InvalidDataException("Missing six native party actor records.");
        for(int party=0;party<6;party++)
        {
            byte[] actor=actors[partyStart+party].Fields;
            if(actor.Length<0x228)throw new InvalidDataException("Truncated combat actor.");
            if(I(actor,0x160)==0||I(actor,0x224)==0)continue;
            int count=state.GetPartyInt(party,252);
            if((uint)count>6)throw new InvalidDataException("Equipment copy count exceeds six native slots.");
            actor.AsSpan(0x208,count*4).CopyTo(state.PartyRecords.AsSpan(party*280+228,count*4));
            for(int slot=0;slot<count;slot++)
            {
                int item=I(actor,0x208+slot*4);
                if(item is <58 or >60)continue;
                // The equipped unit was consumed in this battle. A spare reserves the next unit;
                // without a spare the slot clears, but ownership must still lose that last unit.
                if(state.AvailableItems[item]<1)state.SetPartyInt(party,228+slot*4,-1);
                else state.AvailableItems[item]--;
                state.OwnedItems[item]=Math.Max(0,state.OwnedItems[item]-1);
            }
        }
        InventoryMaintenance.ReconcileBattleSupplies(state);
    }

    // emit is synchronous: AwardExperience must run the genuine424d40 player-choice modal before returning.
    public static CombatOutcomeReward? Complete(GameState state,IReadOnlyList<CombatActorImage> actors,int partyStart,
        CombatOutcomeInput input,Action<CombatOutcomeEffect> emit)
    {
        if(input.ReturnCode is not (1 or 2))throw new InvalidDataException("Combat has not supplied a native terminal result.");
        CopyEquipmentBack(state,actors,partyStart);
        CombatOutcomeReward? reward=null;
        if(input.RewardEligible)
        {
            var actual=Calculate(input.BaseAward,input.ComboTotal,input.Variant,input.RegionIndex);reward=actual;
            state.Globals[80]=actual.SignedAward;
            if(actual.SignedAward!=0)state.Money=unchecked(state.Money+actual.MoneyDelta);
            emit(new(CombatOutcomeEffectKind.AwardExperience,actual.SignedAward));
        }
        emit(new(CombatOutcomeEffectKind.ReleaseBattleMenu));emit(new(CombatOutcomeEffectKind.ReleaseCombatResources));
        state.Globals[60]=0;state.Globals[80]=0;
        emit(new(CombatOutcomeEffectKind.ResetDisplayAndClear));return reward;
    }
    private static int I(byte[] bytes,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset,4));
}
