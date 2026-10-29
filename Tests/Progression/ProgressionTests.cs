using Atria.Game.Progression;
using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Progression;

public static class ProgressionTests
{
    public static string Run(string resourceRoot)
    {
        ItemCatalog items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(resourceRoot, "ATITEM.DAT")));
        for (int level = 1; level <= 12; level++)
        {
            for (int difference = -1; difference <= 1; difference++)
            {
                GameState state = Party(level, ProgressionRules.Thresholds[level - 1] + difference);
                var effects = new FixtureEffects();
                new PendingExperienceRuntime(items, effects).ProcessPending(state);
                bool promoted = difference >= 0;
                Check(state.GetPartyInt(0, 200) == level + (promoted ? 1 : 0), "Exact threshold boundary/single promotion.");
                Check(state.GetPartyInt(0, 196) == ProgressionRules.Thresholds[level - 1 + (promoted ? 1 : 0)], "Next numeric threshold.");
                Check(state.GetPartyInt(0, 88) == (state.GetPartyInt(0, 200) - 1) / 2 + 1 && state.GetPartyInt(0, 224) == state.GetPartyInt(0, 200) / 3 + 1, "Recovered derived progression fields.");
                Check(effects.Notices == (promoted ? 1 : 0) && state.GetPartyInt(0, 256) == 0 && state.ExperiencePending == 0, "Deferred level notices/stat flag clear.");
            }
        }

        foreach (int xp in new[]
        {
            999998,
            999999,
            1000000,
            int.MaxValue
        }
        )
        {
            GameState state = Party(13, xp);
            int attribute = state.GetPartyInt(0, 100);
            var effects = new FixtureEffects();
            new PendingExperienceRuntime(items, effects).ProcessPending(state);
            Check(state.GetPartyInt(0, 200) == 13 && state.GetPartyInt(0, 196) == 999999 && state.GetPartyInt(0, 100) == attribute && effects.Notices == 0, "Terminal-rank repair does not read strings or repeat rank awards.");
        }

        GameState huge = Party(1, 0, 100000);
        new PendingExperienceRuntime(items, new FixtureEffects()).ProcessPending(huge);
        Check(huge.GetPartyInt(0, 200) == 2 && huge.GetPartyInt(0, 196) == 2000 && huge.GetPartyInt(0, 100) == 11 && huge.GetPartyInt(0, 108) == 11, "Large award preserves one promotion and class0 level2 gains.");
        GameState wrap = Party(13, int.MaxValue, 1);
        new PendingExperienceRuntime(items, new FixtureEffects()).ProcessPending(wrap);
        Check(wrap.GetPartyInt(0, 192) == int.MinValue && wrap.GetPartyInt(0, 200) == 13, "Explicit original signed32 XP wrapping.");
        CheckAllocation();
        CheckRanks(items);
        CheckInvalid(items);
        CheckNotice();
        CheckAllocationRaster();
        ExperienceDriverTests.Run(items);
        CheckNewCharacter();
        return "progression: threshold1..13, allocation modes/manual/stable ties, signedXP, stat/rank gains, strict notice and invalid-state atomicity passed";
    }

    private static void CheckAllocation()
    {
        var party = Party(1, 30, 7);
        SetActor(party, 2, 1, 1, 5);
        SetActor(party, 5, 1, 1, 5);
        var equal = new ExperienceAllocationSession(PartyCodec.Encode(party.Party), 7);
        Check(equal.Allocation.SequenceEqual(new[] { 2, 0, 2, 0, 0, 2 }), "Equal mode truncates remainder.");
        for (int award = 1; award <= 120; award++)
        {
            var session = new ExperienceAllocationSession(PartyCodec.Encode(party.Party), award);
            session.ApplyInput(action => action == ExperienceInput.CycleMode);
            int[] expected = new int[6];
            int[] xp = [30, 0, 5, 0, 0, 5];
            for (int point = 0; point < award; point++)
            {
                int recipient = 0;
                foreach (int actor in new[]
                {
                    0,
                    2,
                    5
                }
                )
                {
                    if (xp[actor] + expected[actor] < xp[recipient] + expected[recipient])
                    {
                        recipient = actor;
                    }
                }

                expected[recipient]++;
            }

            Check(session.Allocation.SequenceEqual(expected), "Batched waterfill equals native pointwise stable-index minimum.");
        }

        var large = new ExperienceAllocationSession(PartyCodec.Encode(Party(1, 0).Party), 1000000000);
        large.ApplyInput(action => action == ExperienceInput.CycleMode);
        Check(large.Allocation[0] == 1000000000, "Large equalizer allocation preserves retained-recipient behavior past its sentinel.");
        GameState nearSentinel = Party(13, 999999998);
        SetActor(nearSentinel, 1, 1, 13, 999999998);
        var retained = new ExperienceAllocationSession(PartyCodec.Encode(nearSentinel.Party), int.MaxValue);
        retained.ApplyInput(action => action == ExperienceInput.CycleMode);
        Check(retained.Allocation[0] == 1 && retained.Allocation[1] == int.MaxValue - 1, "Stable last recipient survives sentinel exhaustion and signed total wrap.");
        var selected = new ExperienceAllocationSession(PartyCodec.Encode(party.Party), 31);
        selected.ApplyInput(action => action == ExperienceInput.CycleMode);
        selected.ApplyInput(action => action == ExperienceInput.CycleMode);
        Check(selected.Mode == 2 && selected.Allocation[0] == 31, "Selected-only mode.");
        selected.ApplyInput(action => action == ExperienceInput.GiveFromSelectedHeld);
        Check(selected.Allocation.SequenceEqual(new[] { 21, 0, 5, 0, 0, 5 }), "Held give redistributes five per other active actor.");
        selected.ApplyInput(action => action == ExperienceInput.TakeFromOthersHeld);
        Check(selected.Allocation[0] == 31 && selected.Allocation[2] == 0 && selected.Allocation[5] == 0, "Held take collects at most five from each other active actor.");
        selected.ApplyInput(action => action == ExperienceInput.PreviousMember);
        Check(selected.SelectedMember == 5, "Previous wraps/skips inactive actors.");
        selected.ApplyInput(action => action == ExperienceInput.NextMember);
        Check(selected.SelectedMember == 0, "Next wraps/skips inactive actors.");
        SetActor(party, 2, 2, 1, 5);
        var mixed = new ExperienceAllocationSession(PartyCodec.Encode(party.Party), 9);
        Check(mixed.ActiveCount == 3 && mixed.Allocation[0] == 3 && mixed.Allocation[2] == 0 && mixed.Allocation[5] == 3, "Initial allocation membership==1 versus denominator membership!=0.");
    }

    private static void CheckRanks(ItemCatalog items)
    {
        foreach (int conditions in new[]
        {
            0,
            1,
            2,
            3
        }
        )
        {
            var state = Party(12, 128000);
            if ((conditions & 1) != 0)
            {
                foreach (int item in ProgressionRules.RankQuestItemIds)
                {
                    state.SetQuestItemFlag(item, 1);
                }
            }

            if ((conditions & 2) != 0)
            {
                for (int i = 20; i < 40; i++)
                {
                    state.EventStatuses[i] = 4;
                }
            }

            new PendingExperienceRuntime(items, new FixtureEffects()).ProcessPending(state);
            int bonus = conditions == 3 ? 6 : conditions == 0 ? 0 : 2;
            Check(state.GetPartyInt(0, 100) == 10 + bonus && state.GetPartyInt(0, 120) == 10 + bonus, "Rank13 quest/item six-attribute bonuses.");
            Check(state.GetPartyInt(0, 184) == (conditions == 3 ? 7 : conditions == 0 ? 0 : 2)
&& state.GetPartyInt(0, 276) == (conditions == 3 ? 2 : conditions == 0 ? 0 : 1), "Rank extra field/title selection.");
            int old = state.GetPartyInt(0, 100);
            state.PendingExperience = 1000000;
            state.ExperiencePending = 1;
            new PendingExperienceRuntime(items, new FixtureEffects()).ProcessPending(state);
            Check(state.GetPartyInt(0, 100) == old && state.GetPartyInt(0, 200) == 13, "Terminal awards do not repeat achievements.");
        }

        var special = Party(11, 112000);
        special.SetPartyInt(0, 64, 3);
        new PendingExperienceRuntime(items, new FixtureEffects()).ProcessPending(special);
        Check(special.GetPartyInt(0, 116) == 11 && special.GetPartyInt(0, 108) == 11 && special.GetPartyInt(0, 184) == 1, "Class3 level12 special target23 gain.");
    }

    private static void CheckInvalid(ItemCatalog items)
    {
        foreach (int level in new[]
        {
            -1,
            0,
            14,
            15
        }
        )
        {
            GameState invalid = Party(1, 0);
            invalid.SetPartyInt(0, 200, level);
            byte[] before = (byte[])PartyCodec.Encode(invalid.Party).Clone();
            ExpectFailure(() => new PendingExperienceRuntime(items, new FixtureEffects()).ProcessPending(invalid));
            Check(PartyCodec.Encode(invalid.Party).AsSpan().SequenceEqual(before) && invalid.ExperiencePending == 1, "Invalid level rejects without partial state commit.");
        }

        var state = Party(1, 1000);
        byte[] original = (byte[])PartyCodec.Encode(state.Party).Clone();
        ExpectFailure(() => new PendingExperienceRuntime(items, new IncompleteEffects()).ProcessPending(state));
        Check(PartyCodec.Encode(state.Party).AsSpan().SequenceEqual(original) && state.ExperiencePending == 1, "Missing modal completion cannot award XP.");
        state.SetPartyInt(0, 196, 1234);
        ExpectFailure(() => new PendingExperienceRuntime(items, new FixtureEffects()).ProcessPending(state));
        var sentinel = new ExperienceAllocationSession(PartyCodec.Encode(Party(13, 999999999).Party), 1);
        ExpectFailure(() => sentinel.ApplyInput(action => action == ExperienceInput.CycleMode));
        Check(sentinel.Mode == 0 && sentinel.Allocation[0] == 1, "Undefined sentinel recipient diagnoses without replacing allocation.");
    }

    private static void CheckNotice()
    {
        var notice = new LevelUpNotice(2, 13, 100);
        Check(notice.Frame.TextY == 173 && notice.Frame.ShadowY == 172, "Notice actor-row start.");
        notice.AfterRenderedFrame(4099, false);
        Check(!notice.Completed && notice.Frame.TextY == 172, "Notice remains until its four-second deadline.");
        notice.AfterRenderedFrame(4100, false);
        Check(notice.Completed, "Notice direct deadline exit.");
        notice = new(0, 2, 0);
        notice.AfterRenderedFrame(0, true);
        Check(notice.Completed, "Held confirm skips only after one rendered frame.");
        byte[] fontBytes = new byte[4096];
        // Test placement independently of which language or word starts the notice.
        for (int glyph = 0; glyph < fontBytes.Length / 16; glyph++)
        {
            fontBytes[glyph * 16] = 0x80;
        }
        BitmapFont font = BitmapFont.DecodeEng(fontBytes, true);
        ushort[] pixels = new ushort[640 * 480];
        ProgressionNoticeRaster.Draw(new LevelUpNotice(2, 13, 0).Frame, font, pixels);
        Check(pixels[173 * 640 + 132] == 0xffff && pixels[172 * 640 + 133] == 0x0841, "Notice plain small text and exact palette shadow origin.");
    }

    private static void CheckAllocationRaster()
    {
        foreach (int threshold in ProgressionRules.Thresholds)
        {
            foreach (int xp in new[]
            {
                1,
                threshold - 1,
                threshold,
                threshold + 1,
                int.MaxValue,
                int.MinValue
            }
            )
            {
                foreach (int barWidth in new[] { 135, 155 })
                {
                    double quotient = (double)barWidth / threshold;
                    double product = quotient * xp;
                    Check(ExperienceBarMath.Ratio(barWidth, threshold, xp, X87Precision.Bits53) == (int)Math.Truncate(product), "XP bar explicit53-bit operations equal independently rounded division/multiplication.");
                    Check(ExperienceBarMath.Ratio(-barWidth, threshold, xp, X87Precision.Bits53) == -(int)Math.Truncate(product), "Award bar signed ratio.");
                }
            }
        }

        var blank = new SpriteFrame(0, 0, 0, [], [], 0, 0, []);
        var tiles = new SpriteFile(false, Enumerable.Repeat(blank, 8).ToArray());
        BitmapFont font = BitmapFont.DecodeEng(new byte[4096], true);
        ReadOnlyMemory<byte>[] labels = Enumerable.Repeat<ReadOnlyMemory<byte>>(new byte[] { (byte)'x' }, 54).ToArray();
        var raster = new ExperienceAllocationRaster(tiles, font, labels, X87Precision.Bits53);
        var state = Party(1, 500);
        ushort[] pixels = Enumerable.Repeat((ushort)0xffff, 640 * 480).ToArray();
        raster.Draw(state.Heroes, new[] { 250, 0, 0, 0, 0, 0 }, 0, 0, 0, pixels);
        Check(pixels[115 * 640 + 125] == 0x7bef && pixels[127 * 640 + 136] == 0x000f, "XP panelhalf-shade and selected actor rectangle.");
        Check(pixels[131 * 640 + 272] == 0x739f && pixels[131 * 640 + 338] == 0x739f, "Earned XP bar exactgradient/span.");
        Check(pixels[131 * 640 + 339] == 0xffee && pixels[131 * 640 + 371] == 0xffee, "Allocated XP bar exactgradient/span.");
        Check(pixels[131 * 640 + 372] == 0xfb8e && pixels[131 * 640 + 406] == 0xfb8e && pixels[131 * 640 + 407] == 0x7bef, "Remaining bar/end-exclusive horizontal raster.");
        Check(pixels[267 * 640 + 147] == 0x000f, "Allocation-mode label highlight origin.");
    }

    private static void CheckNewCharacter()
    {
        byte[] data = new byte[ItemCatalog.DescriptionOffset + ItemCatalog.Count * 310];
        void W(int offset, int value) => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
        W(58, 1);
        W(62, 0);
        W(74, 7); // item0: base attribute100 +7
        ItemCatalog items = ItemCatalog.Parse(data);
        GameState state = Party(1, 0);
        SetActor(state, 1, 0, 1, 0);
        state.SetPartyInt(0, 228, 0);
        state.SetPartyInt(1, 228, 0);
        state.NewCharacterId = 1;
        state.NewCharacterPending = 1;
        state.AvailableItems[0] = 9;
        state.SetPartyInt(0, 76, 1);
        Check(NewCharacterRuntime.ProcessPending(state, items, 0), "Pending character must process.");
        Check(state.GetMemberStatus(1) == 1 && state.OwnedItems[0] == 1 && state.AvailableItems[0] == 9, "Join changes only owned inventory for six equipment slots.");
        Check(state.GetPartyInt(0, 100) == 24 && state.GetPartyInt(1, 100) == 10, "415920 targets shared selected actor for both members' equipment.");
        Check(state.GetPartyInt(0, 76) == state.GetPartyInt(0, 72) && state.NewCharacterPending == 0 && state.NewCharacterId == 1, "Join recalculates HP and clears only pending flag.");
        Check(!NewCharacterRuntime.ProcessPending(state, items, 0), "Cleared pending must not reapply equipment effects.");
        state.NewCharacterPending = 1;
        state.SetPartyInt(1, 228, 99);
        byte[] before = (byte[])PartyCodec.Encode(state.Party).Clone();
        int owned = state.OwnedItems[0];
        ExpectFailure(() => NewCharacterRuntime.ProcessPending(state, items, 0));
        Check(before.AsSpan().SequenceEqual(PartyCodec.Encode(state.Party)) && state.OwnedItems[0] == owned && state.NewCharacterPending == 1, "Unsafe joining item rejects before any state commit.");
    }

    private static GameState Party(int level, int xp, int award = 0)
    {
        var state = new GameState
        {
            ExperiencePending = 1,
            PendingExperience = award
        };
        SetActor(state, 0, 1, level, xp);
        return state;
    }

    private static void SetActor(GameState state, int actor, int member, int level, int xp)
    {
        state.SetMemberStatus(actor, member);
        state.SetPartyInt(actor, 200, level);
        state.SetPartyInt(actor, 192, xp);
        state.SetPartyInt(actor, 196, ProgressionRules.Thresholds[level - 1]);
        for (int offset = 100; offset <= 120; offset += 4)
        {
            state.SetPartyInt(actor, offset, 10);
        }

        for (int offset = 228; offset <= 248; offset += 4)
        {
            state.SetPartyInt(actor, offset, -1);
        }
    }

    private sealed class FixtureEffects : IExperienceModalEffects
    {
        public uint Milliseconds => 0;
        public int Notices
        {
            get; private set;
        }

        public void Allocate(ExperienceAllocationSession session) => session.ApplyInput(action => action == ExperienceInput.Finish);
        public void ShowLevelUp(LevelUpNotice notice, IReadOnlyList<PartyMember> awardedParty)
        {
            Notices++;
            notice.AfterRenderedFrame(4000, false);
        }

        public void Finish(AwardFinishRelease release) => release.Observe(false);
    }

    private sealed class IncompleteEffects : IExperienceModalEffects
    {
        public uint Milliseconds => 0;

        public void Allocate(ExperienceAllocationSession session)
        {
        }

        public void ShowLevelUp(LevelUpNotice notice, IReadOnlyList<PartyMember> awardedParty)
        {
        }

        public void Finish(AwardFinishRelease release)
        {
        }
    }

    private static void ExpectFailure(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("Expected bounded progression failure.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
