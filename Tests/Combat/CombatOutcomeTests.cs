using System.Buffers.Binary;
using Atria.Game.Combat;
using Atria.Game.State;

namespace Atria.Tests.Combat;

public static class CombatOutcomeTests
{
    public static string Run(string root)
    {
        _ = root;
        // Actual0.1.2 first-bar log: base105,combo229,variant0,region0 -> old133XP/13gold.
        Check(CombatOutcomeRewards.Calculate(105, 229, 0, 0) == new CombatOutcomeReward(210, 210, 21), "approved first-bar payout210XP/21gold");
        foreach (int variant in new[]
        {
            0,
            1,
            2
        }
        )
        {
            foreach (int region in Enumerable.Range(0, 10))
            {
                var expected = CombatOutcomeRewards.Calculate(105, 0, variant, region);
                foreach (int combo in new[]
                {
                    int.MinValue,
                    -1,
                    1,
                    229,
                    100000,
                    int.MaxValue
                }
                )
                {
                    Check(CombatOutcomeRewards.Calculate(105, combo, variant, region) == expected, "payout independent of all combo history");
                }
            }
        }

        Check(CombatOutcomeRewards.Calculate(100, 64, 0, 3) == new CombatOutcomeReward(2060, 2060, 206), "double after type0 region adjustment");
        Check(CombatOutcomeRewards.Calculate(11, 16, 1, 3) == new CombatOutcomeReward(1532, 1532, 153), "type1 rounds baseline half before doubling");
        Check(CombatOutcomeRewards.Calculate(101, 7, 2, 4) == new CombatOutcomeReward(1700, 1700, 170), "type2 halves baseline before region and doubling");
        Check(CombatOutcomeRewards.Calculate(0, 999, 0, 0) == new CombatOutcomeReward(20, 20, 2), "baseline zero fallback then double");
        Check(CombatOutcomeRewards.Calculate(16384, 0, 0, 0) == new CombatOutcomeReward(32768, 32767, 3276), "signed reward channel saturates instead of becoming negative");
        Check(CombatOutcomeRewards.Calculate(int.MaxValue, 0, 1, 9).SignedAward == 32767, "wide intermediate prevents arithmetic wrap");
        Check(CombatOutcomeRewards.CalculateOriginal(100, 64, 0, 3) == new CombatOutcomeReward(1038, 1038, 103), "type0 area3 reward");
        Check(CombatOutcomeRewards.CalculateOriginal(10, 16, 1, 3) == new CombatOutcomeReward(768, 768, 76), "type1 signed half then area3 bonus");
        Check(CombatOutcomeRewards.CalculateOriginal(101, 7, 2, 4) == new CombatOutcomeReward(850, 850, 85), "type2 truncating half then area4 bonus");
        Check(CombatOutcomeRewards.CalculateOriginal(0, 0, 0, 0).SignedAward == 10, "zero fullaward becomes ten");
        Check(CombatOutcomeRewards.CalculateOriginal(65536, 0, 0, 0).SignedAward == 0, "word truncation happens after zero fallback");
        Check(CombatOutcomeRewards.CalculateOriginal(-17, -1, 2, 0).SignedAward == -9, "arithmeticshift combo and signed division");
        Check(CombatOutcomeRewards.CalculateOriginal(int.MaxValue, 8, 99, 0).FullValue == int.MinValue, "native wrapping award addition");
        var state = new GameState
        {
            Money = 100
        };
        var actors = Enumerable.Range(0, 2 + state.Heroes.Length).Select(_ => new CombatActorImage()).ToArray();
        void Put(int actor, int offset, int value) => LegacyActorFixture.WriteInt32LittleEndian(actors[actor].State.AsSpan(offset, 4), value);
        for (int party = 0; party < state.Heroes.Length; party++)
        {
            state.SetPartyInt(party, 252, 6);
            state.SetPartyInt(party, 72, 999);
            for (int slot = 0; slot < 6; slot++)
            {
                state.SetPartyInt(party, 228 + slot * 4, 90);
                Put(2 + party, 0x208 + slot * 4, -1);
            }
        }

        Put(2, 0x160, 1);
        Put(2, 0x224, 1);
        Put(2, 0x16c, 1);
        Put(2, 0x208, 58);
        Put(2, 0x20c, 58);
        Put(2, 0x210, 59);
        Put(2, 0x214, 60);
        state.SetMemberStatus(0, 1);
        state.AvailableItems[58] = 1;
        state.OwnedItems[58] = 3;
        state.AvailableItems[59] = 0;
        state.OwnedItems[59] = 1;
        state.AvailableItems[60] = 1;
        state.OwnedItems[60] = 2;
        Put(3, 0x160, 1); //224zero: do not copy this member.
        state.Globals[60] = 1;
        List<CombatOutcomeEffect> effects = [];
        var reward = CombatOutcomeRewards.Complete(state, actors, 2, new(1, true, 100, 64, 0, 3), effect =>
        {
            if (effect.Kind == CombatOutcomeEffectKind.AwardExperience)
            {
                Check(state.Globals[80] == 2060 && state.Money == 306 && state.Globals[60] == 1, "award modal sees committed money and transient globals before cleanup");
            }

            effects.Add(effect);
        });
        Check(reward!.Value.SignedAward == 2060 && state.GetPartyInt(0, 72) == 999, "no fullparty HP copyback");
        Check(state.GetPartyInt(0, 228) == 58 && state.GetPartyInt(0, 232) == -1 && state.AvailableItems[58] == 0 && state.OwnedItems[58] == 1, "each equipped supply is consumed; one spare refills only one of two slots");
        Check(state.GetPartyInt(0, 236) == -1 && state.OwnedItems[59] == 0, "last supply clears slot and ownership");
        Check(state.OwnedItems[60] == 1 && state.AvailableItems[60] == 0 && state.GetPartyInt(0, 240) == 60 && state.GetPartyInt(1, 228) == 90, "spare becomes equipped while inactive battle actor is retained");
        Check(effects.Select(e => e.Kind).SequenceEqual(new[] { CombatOutcomeEffectKind.AwardExperience, CombatOutcomeEffectKind.ReleaseBattleMenu, CombatOutcomeEffectKind.ReleaseCombatResources, CombatOutcomeEffectKind.ResetDisplayAndClear })
&& state.Globals[60] == 0 && state.Globals[80] == 0, "ordered cleanup after synchronous award");
        effects.Clear();
        CombatOutcomeRewards.Complete(state, actors, 2, new(1, false, 999, 0, 0, 0), effects.Add);
        Check(!effects.Any(e => e.Kind == CombatOutcomeEffectKind.AwardExperience) && state.Money == 306, "return1 without award creates neither XP nor gold");
        int counter = 0;
        List<CombatOutcomeReturnEffect> returns = [];
        state.Mode = 430;
        state.EndingResetFlag = 1;
        CombatOutcomeReturn.Apply(state, 1, ref counter, returns.Add);
        Check(state.EndingResetFlag == 0 && counter == 1 && returns.Count == 1, "430 native return excludes town background reload");
        state.Mode = 330;
        returns.Clear();
        CombatOutcomeReturn.Apply(state, 1, ref counter, returns.Add);
        Check(counter == 0 && state.ResourceCounterDc88 == 1 && returns[0].Kind == CombatOutcomeReturnEffectKind.ReloadWorldBackground, "two battle returns increment resource and reload dungeonworld background");
        state.ScriptActive = 1;
        state.EndingResetFlag = 1;
        returns.Clear();
        CombatOutcomeReturn.Apply(state, 2, ref counter, returns.Add);
        Check(state.Mode == 1100 && state.ModeStackDepth == -1 && state.ScriptActive == 0 && state.EndingResetFlag == 0 && counter == 0
&& returns.Single().Kind == CombatOutcomeReturnEffectKind.ResetStackAndPushGameOver, "defeat state before native stack push");
        return "Combat outcome: native reward arithmetic, equipment/inventory copyback, synchronous award ordering, distinct return1/noaward and defeat";
    }

    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
