using Atria.Game.Combat;
using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Combat;

public static class CombatTests
{
    public static string Run(string resourceRoot)
    {
        var shadowFrame = new SpriteFrame(0, 0, 0, [], [], 8, 8, [new(1, 0, 0, [1, 2], []), new(0, 1, 0, [3], []), new(2, 4, 3, [4], [])]);
        ushort[] shadowPixels = Enumerable.Repeat((ushort)0xffff, 256).ToArray();
        CombatShadowRaster.Draw(shadowFrame, shadowPixels, 16, 16, 16, 5, 0, false, new(0, 0, 16, 16));
        Check(shadowPixels[6 * 16 + 4] == 0 && shadowPixels[6 * 16 + 5] == 0 && shadowPixels[7 * 16 + 6] == 0 && shadowPixels[6 * 16 + 3] == 0xffff, "Native shadow samples every fourth source row, advances diagonally and ignores source colors.");
        Array.Fill(shadowPixels, (ushort)0xffff);
        CombatShadowRaster.Draw(shadowFrame, shadowPixels, 16, 16, 16, 5, 0, true, new(0, 0, 16, 16));
        Check(shadowPixels[6 * 16 + 10] == 0 && shadowPixels[6 * 16 + 9] == 0 && shadowPixels[7 * 16 + 10] == 0, "Mirrored shadow retains native width rather than width-minus-one origin.");
        var state = new GameState();
        state.Globals[71] = 123;
        var recorder = new EntryRecorder();
        var host = new ScriptStateHost(state, recorder);
        int Invoke(string name, int number) => host.Invoke(new ScriptCall("COMBAT_TEST", name, 0, new[] { new ScriptValue(number) }));
        Invoke("battle_start", 3);
        Check(recorder.Calls == 0 && state.EndingResetFlag == 1 && state.PendingBattle == 3 && state.BattleVariant == 0, "Deferred entry only writes actual transition request.");
        Invoke("battle_start2", 2);
        Check(recorder.Calls == 1 && recorder.Kind == ScriptEffectKind.BattleWithPostAction && recorder.Flag == 1 && recorder.Number == 2, "Immediate scripted callback observes native request writes before real effect boundary.");
        Invoke("battle_start3", 200);
        Check(recorder.Calls == 2 && recorder.Kind == ScriptEffectKind.ForcedBattleWithPostAction && state.BattleVariant == 1 && state.Globals[71] == 123, "Forced callback changes only native type WORD.");
        var scripted = new CombatEntryRequest(FixedEncounterKind.Scripted, 2);
        var scene = CombatSceneDefinition.Load(resourceRoot, scripted);
        Check(scripted.ResourceName == "SBATTL02"
&& scripted.SelectMusic(() => throw new InvalidOperationException("Scripted battle must not consume RNG.")) == 24, "Fixed scripted resource/music without random consumption.");
        int calls = 0;
        Check(new CombatEntryRequest(FixedEncounterKind.Forced, 200).ResourceName == "EBAT200" && new CombatEntryRequest(FixedEncounterKind.Forced, 200).SelectMusic(() =>
        {
            calls++;
            return 32767;
        }) == 23 && calls == 1, "Forced music consumes exactly one native random sample.");
        Check(scene.Source.BackgroundReference.Span.SequenceEqual("PUB01.bmp"u8) && scene.Header73 == 330 && scene.Header77 == 440, "Intro scene identity/header coordinates.");
        Check(scene.Source.Count == 8 && scene.Actors.Count == 7 && scene.RuntimeActorCount == 14 && scene.PartyStartIndex == 8, "Marker removal retains native unused actor slot7 and party tail8..13.");
        int[] x = [249, 283, 653, 671, 427, 493, 562];
        int[] y = [265, 348, 271, 344, 78, 80, 77];
        for (int i = 0; i < 7; i++)
        {
            CombatSceneActor actor = scene.Actors[i];
            Check(actor.RuntimeIndex == i && actor.X == x[i] && actor.Y == y[i] && actor.Type == 11 && actor.InfoResource == "11.inf", "Actual intro nonparty declaration/order.");
            Check(actor.Source.GetInteger(6) == 130 && actor.Source.GetInteger(13) == (i >= 4 ? 1 : 0) && actor.Source.GetInteger(14) == (i >= 4 ? 1 : 0), "Actual intro declaration attributes/flags retained without AI substitutions.");
        }

        for (int i = 0; i < 6; i++)
        {
            Check(scene.PartySpawns[i] == new PartySpawn(8 + i, i, 443, 340 + i * 20, 1), "Native marker spawn projection ignores markerY.");
        }

        var loadedSounds = new List<int>();
        CombatInfoDefinition info = CombatInfoDefinition.Load(resourceRoot, "11.inf"u8, loadedSounds.Add);
        Check(loadedSounds.SequenceEqual(new[] { 75, 76, 77, 78 }), "Actual X token audio load callback follows nonzero disk order.");
        Check(info.Characters.Count == 1 && info.TrailingBytes.IsEmpty, "Complete actual11 INF token stream.");
        CombatInfoCharacter bandit = info.Characters[0];
        Check(bandit.AnimationResource.Span.SequenceEqual("BANDIT1.ANI"u8) && bandit.Type == 9 && bandit.HeaderParameter == 0 && bandit.GuardFailureThreshold == 10
&& bandit.AttackDelay == 2000, "True intro enemy animation/type/header values.");
        Check(bandit.Motions.Count == 5 && bandit.BasicAttackCount == 2 && bandit.Reactions.Count == 19 && bandit.DeclaredSoundCount == 7, "Actual intro action/attack/damage/sound record counts.");
        Check(bandit.Attacks[0].Animation == 12 && bandit.Attacks[0].FrameInterval == 25 && bandit.Attacks[1].FrameInterval == 25 && bandit.HitSound == 75 && bandit.Sounds[3] == 78, "INF binary field placement and sound IDs.");
        CombatAnimationAssets assets = CombatAnimationAssets.Load(resourceRoot, bandit.AnimationResource.Span);
        Check(assets.Animation.SpriteReference.AsSpan().SequenceEqual("bandit1.SPR"u8) && assets.Animation.Sequences.Length == 26, "Actual BANDIT1 animation references native battle sprite and26 sequences.");
        CombatActorState idleRecord = new();
        var idle = new CombatAnimationCursor(idleRecord, bandit, assets);
        idle.BeginMotion(0, 1000);
        int IdleRead(int offset) => LegacyActorFixture.ReadInt32LittleEndian(idleRecord.AsSpan(offset, 4));
        Check(idle.SequenceIndex == 0 && idle.FrameIndex == 0 && IdleRead(0xad) == 0x40 && IdleRead(0xc1) == 150 && IdleRead(0xbd) == 1150
&& IdleRead(0x84) == idle.CurrentSprite.Height, "Actual408b40 idle sequence, sprite height and motion interval binding.");
        LegacyActorFixture.WriteInt32LittleEndian(idleRecord.AsSpan(0x2b0, 4), 1);
        idle.BeginMotion(0, 2000);
        Check(IdleRead(0xc1) == 75 && IdleRead(0xbd) == 2075, "Initial action binding applies actual actor interval status.");
        LegacyActorFixture.WriteInt16LittleEndian(idleRecord.AsSpan(0xd5, 2), 1);
        Check(idle.FrameIndex == 1 && idle.BoundFrameIndex == 0 && ReferenceEquals(idle.CurrentFrame, assets.Animation.Sequences[0].Frames[0]), "Animation frame-counter increment must not prematurely replace native bound collision frame.");
        idle.RebindFromFrameCounter();
        Check(idle.BoundFrameIndex == 1 && ReferenceEquals(idle.CurrentFrame, assets.Animation.Sequences[0].Frames[1]), "404ee0 explicitly resolves actual frame after AI phase.");
        CheckBanditDecision(bandit, assets);
        var shared = bandit with
        {
        };
        int award = 0, samples = 0;
        int Read(object bytes, int offset) => LegacyActorFixture.ReadInt32LittleEndian(bytes.FixtureSpan(offset, 4));
        for (int i = 0; i < 7; i++)
        {
            CombatActorState actor = new();
            actor.Team = 1;
            CombatEnemyAttributes.Initialize(actor, shared, 0, 0, () =>
            {
                samples++;
                return 1234;
            }, ref award);
            Check(Read(actor, 0x170) == 60 && Read(actor, 0x16c) == 60 && Read(actor, 0x1f4) == 24 && Read(actor, 0x1ec) == 1 && Read(actor, 0x180) == 900, "Actual introductory type9 final attributes differ from unscaled scene HP130.");
        }

        Check(samples == 7 && award == 105 && shared.AttackDelay == 2000, "Seven initial enemies consume seven RNG samples and award105 before encounter modifiers.");
        shared = bandit with
        {
        };
        award = 0;
        for (int i = 0; i < 4; i++)
        {
            CombatActorState actor = new();
            CombatEnemyAttributes.Initialize(actor, shared, 3, 0, () => 5, ref award);
            Check(Read(actor, 0x170) == 120 && Read(actor, 0x1f4) == 26 && Read(actor, 0x1ec) == 2, "Original per-region columns, signed integer scaling and RNG remainder.");
        }

        Check(award == 100 && shared.AttackDelay == 0, "Shared INF delay is reduced per actor, not copied per enemy.");
        CheckSelection();
        CombatSelectionModalTests.Run();
        CheckHits();
        CheckDamageApplication();
        CheckMaintenance();
        CheckAttackMath();
        CheckStatuses();
        CheckTargetSelection();
        CheckSpriteProjection();
        CheckSpawns();
        CheckRuntimeProjection(resourceRoot, scene);
        CheckFrameController(resourceRoot, scene);
        CheckAdditionalAi(resourceRoot);
        CheckFrameEffects();
        var effectSprite = new SpriteFrame(0, 0, 0, [], [], 2, 1, [new(0, 0, 0, [0xf800], []), new(1, 0, 3, [0x07e0], [])]);
        ushort[] effectPixels = [0x001f, 0x001f, 0x001f, 0x001f];
        CombatSpriteEffects.DrawTint(effectSprite, effectPixels, 4, 0, 0, 0x07e0, false, new(0, 0, 4, 1));
        Check(effectPixels[0] == 0x7be0 && effectPixels[1] == 0x07e0, "Native flash averages source and tint components without destination contribution.");
        Array.Fill(effectPixels, (ushort)0x001f);
        CombatSpriteEffects.DrawFade(effectSprite, effectPixels, 4, 0, 0, 2, false, new(0, 0, 4, 1));
        Check(effectPixels[0] == 0x380f && effectPixels[1] == 0x01ef, "Native death fade divides source components before averaging with destination.");
        CombatSpriteEffects.DrawPacketColor(effectSprite, effectPixels, 4, 0, 0, 0xf98c, true, new(0, 0, 4, 1));
        Check(effectPixels[2] == 0xf800 && effectPixels[1] == 0xf98c, "Mirrored packet color preserves opcode0 and replaces opcode3 at native width origin.");
        var alphaSprite = new SpriteFrame(0, 0, 0, [], [], 2, 1, [new(0, 0, 3, [0xf800], [50])]);
        Array.Fill(effectPixels, (ushort)0x001f);
        CombatSpriteEffects.DrawAlpha(alphaSprite, effectPixels, 4, 0, 0, true, new(0, 0, 4, 1));
        Check(effectPixels[1] == 0x780f && effectPixels[2] == 0x001f, "Mirrored alpha uses proven width-minus-one origin and separately truncated paired percentages.");
        bool held = false;
        int heldQueries = 0;
        var combatInput = new CombatScanInput(_ =>
        {
            heldQueries++;
            return held;
        });
        Check(!combatInput.Query(45), "Released combat input initializes its queried latch.");
        held = true;
        heldQueries = 0;
        Check(combatInput.Query(45) && heldQueries == 2 && !combatInput.Query(45), "Native first press queries held twice and remains latched.");
        held = false;
        held = true;
        Check(!combatInput.Query(45), "An unqueried release does not silently reset native combat latch.");
        held = false;
        combatInput.Query(45);
        held = true;
        Check(combatInput.Query(45), "Queried release permits next actual edge.");
        held = false;
        combatInput.Joystick = new(-500, -501, false, false, false, false);
        Check(combatInput.IsHeld(200) && !combatInput.IsHeld(203), "Joystick thresholds are strict500 and retain extended native scan codes.");
        combatInput.ConfiguredPlayers = 2;
        Check(combatInput.IsHeld(0x16) && !combatInput.IsHeld(200), "Two-player joystick aliases use native U-key mapping.");
        return "combat: actual SBATTL02 declaration, native reserved actor slots, marker spawns and fixed music/RNG contract passed";
    }

    private static void CheckAdditionalAi(string root)
    {
        static void W(object a, int at, int value) => LegacyActorFixture.WriteInt32LittleEndian(a.FixtureSpan(at, 4), value);
        static int I(object a, int at) => LegacyActorFixture.ReadInt32LittleEndian(a.FixtureSpan(at, 4));
        CombatInfoCharacter roland = CombatInfoDefinition.Load(root, "ROLAND1"u8).Characters[0];
        var rolandAssets = CombatAnimationAssets.Load(root, roland.AnimationResource.Span);
        CombatActorState actor = new(), target = new CombatActorState();
        var cursor = new CombatAnimationCursor(actor, roland, rolandAssets);
        cursor.BeginMotion(0, 0);
        actor.Facing = 1;
        W(actor, 0x7c, 10);
        W(actor, 0x98, 10);
        W(actor, 0x78, 350);
        int samples = 0;
        int[] escapeRolls = [1, 7];
        Check(!PartyAiDecision.Begin(actor, 0, target, cursor, 330, 440, 100, () => escapeRolls[samples++]) && samples == 2 && I(actor, 0xb1) == 2
&& I(actor, 0x9c) > 0 && I(actor, 0xa0) < 0, "Actual Roland edge escape consumes two samples, clears target and runs with native signed vertical displacement.");
        cursor.BeginMotion(1, 200);
        W(actor, 0xad, 1);
        W(actor, 0xc9, 2000);
        W(actor, 0x98, 200);
        W(actor, 0x78, 350);
        W(target, 0x98, 250);
        W(target, 0x78, 350);
        samples = 0;
        PartyAiDecision.Continue(actor, 0, target, cursor, roland, 1000, () =>
        {
            samples++;
            return 0;
        });
        Check(samples == 0 && I(actor, 0xb1) == 6 && LegacyActorFixture.ReadInt16LittleEndian(actor.AsSpan(0xdb, 2)) == 0, "Party close-range AD1 attack uses real normal A0 without bandit attack RNG/cooldown gate.");
        cursor.BeginMotion(1, 1100);
        W(actor, 0xad, 1);
        W(actor, 0xc9, 3000);
        W(actor, 0x98, 200);
        W(target, 0x98, 310);
        int first = roland.FirstVariation + 1, count = roland.VariationEnd - roland.FirstVariation - 4, chosen = -1;
        for (int i = first; i < first + count; i++)
        {
            if (roland.Attacks[i].Direction is not (1 or 3 or 7 or 9))
            {
                chosen = i;
                break;
            }
        }

        Check(chosen >= 0, "Actual Roland has a bounded non-diagonal AI variation candidate.");
        samples = 0;
        PartyAiDecision.Continue(actor, 0, target, cursor, roland, 1200, () =>
        {
            samples++;
            return chosen - first;
        });
        Check(samples == 1 && LegacyActorFixture.ReadInt16LittleEndian(actor.AsSpan(0xdb, 2)) == chosen, "Party medium-range AI selects actual non-diagonal V attack from native rejection range.");
        CombatInfoCharacter mist = CombatInfoDefinition.Load(root, "MIST1"u8).Characters[0];
        var mistAssets = CombatAnimationAssets.Load(root, mist.AnimationResource.Span);
        CombatActorState mistActor = new();
        W(mistActor, 0x7c, 100);
        var mistCursor = new CombatAnimationCursor(mistActor, mist, mistAssets);
        mistCursor.BeginMotion(0, 0);
        samples = 0;
        Check(PartyAiDecision.Begin(mistActor, 1, null, mistCursor, 330, 440, 100, () =>
        {
            samples++;
            return 0;
        }) && samples == 3 && I(mistActor, 0xb1) == 0 && LegacyActorFixture.ReadInt16LittleEndian(mistActor.AsSpan(0xde, 2)) == 20 && LegacyActorFixture.ReadInt16LittleEndian(mistActor.AsSpan(0xe0, 2)) == 10, "Actual Mist null-target branch still consumes three ordered samples and applies type1's20/10 step additions.");
        CombatInfoCharacter empire = CombatInfoDefinition.Load(root, "10.inf"u8).Characters[0];
        Check(empire.Type == 8, "Actual10 INF maps to enemy type8.");
        var empireAssets = CombatAnimationAssets.Load(root, empire.AnimationResource.Span);
        CombatActorState enemy = new();
        enemy.Facing = 1;
        W(enemy, 0x98, 100);
        W(enemy, 0x78, 350);
        W(target, 0x98, 220);
        var enemyCursor = new CombatAnimationCursor(enemy, empire, empireAssets);
        enemyCursor.BeginMotion(0, 0);
        samples = 0;
        EnemyType8AiDecision.Begin(enemy, target, enemyCursor, 330, 440, 100, () =>
        {
            samples++;
            return 5;
        });
        Check(samples == 1 && I(enemy, 0xb1) == 0, "Type8 default decision returns idle before extra horizontal/vertical random samples.");
        enemyCursor.BeginMotion(1, 200);
        W(enemy, 0xad, 1);
        W(enemy, 0xc9, 2000);
        W(enemy, 0x1ec, 2);
        samples = 0;
        EnemyType8AiDecision.Continue(enemy, target, enemyCursor, empire, 1000, () =>
        {
            samples++;
            return 1;
        });
        Check(samples == 1 && I(enemy, 0xb1) == 6 && LegacyActorFixture.ReadInt16LittleEndian(enemy.AsSpan(0xdb, 2)) == 2, "Type8 ranged fallback selects real A2 after first range miss with only one native sample.");
        CombatInfoCharacter minotaur = CombatInfoDefinition.Load(root, "12.inf"u8).Characters[0];
        var minotaurAssets = CombatAnimationAssets.Load(root, minotaur.AnimationResource.Span);
        CombatActorState minot = new();
        var minotCursor = new CombatAnimationCursor(minot, minotaur, minotaurAssets);
        minotCursor.BeginMotion(0, 0);
        W(minot, 0x98, 100);
        W(minot, 0x78, 350);
        W(target, 0x98, 200);
        samples = 0;
        CommonEnemyAiDecision.Begin(minot, 10, target, minotCursor, 330, 440, 100, () =>
        {
            samples++;
            return 5;
        });
        Check(samples == 1 && I(minot, 0xb1) == 0 && I(minot, 0x32d) == 1, "Actual minotaur modulo11 default preserves movement policy then idles without extra RNG.");
        CombatInfoCharacter goblin = CombatInfoDefinition.Load(root, "13.inf"u8).Characters[0];
        var goblinAssets = CombatAnimationAssets.Load(root, goblin.AnimationResource.Span);
        CombatActorState gob = new();
        var gobCursor = new CombatAnimationCursor(gob, goblin, goblinAssets);
        gobCursor.BeginMotion(0, 0);
        W(gob, 0x98, 100);
        W(gob, 0x78, 350);
        int[] gobRolls = [0, 7];
        samples = 0;
        CommonEnemyAiDecision.Begin(gob, 11, target, gobCursor, 330, 440, 100, () => gobRolls[samples++]);
        Check(samples == 2 && LegacyActorFixture.ReadInt16LittleEndian(gob.AsSpan(0xde, 2)) == 100 / goblin.Motions[1].StepX + 7, "Actual goblin direct pursuit adds one modulo60 sample after its preserved first decision.");
        CombatInfoCharacter cobolt = CombatInfoDefinition.Load(root, "17.inf"u8).Characters[0];
        var coboltAssets = CombatAnimationAssets.Load(root, cobolt.AnimationResource.Span);
        CombatActorState cob = new();
        var cobCursor = new CombatAnimationCursor(cob, cobolt, coboltAssets);
        cobCursor.BeginMotion(0, 0);
        W(cob, 0x98, 100);
        W(cob, 0x78, 350);
        int[] cobRolls = [0, 2, 3];
        samples = 0;
        CommonEnemyAiDecision.Begin(cob, 15, target, cobCursor, 330, 440, 100, () => cobRolls[samples++]);
        Check(samples == 3 && I(cob, 0xb1) == 2 && I(cob, 0x32d) == 1 && I(cob, 0x9c) > 0 && I(cob, 0xa0) < 0, "Actual cobolt near-target avoidance runs toward opposite side with exactly two additional samples.");
        cobCursor.BeginMotion(0, 0);
        W(cob, 0x98, 100);
        W(cob, 0x78, 350);
        W(target, 0x98, 320);
        samples = 0;
        CommonEnemyAiDecision.Begin(cob, 15, target, cobCursor, 330, 440, 100, () =>
        {
            samples++;
            return 0;
        });
        Check(samples == 1 && I(cob, 0xb1) == 1, "Cobolt distance equality/far branch walks scaled displacement without side-placement RNG.");
        CombatInfoCharacter tiger = CombatInfoDefinition.Load(root, "18.inf"u8).Characters[0];
        var tigerAssets = CombatAnimationAssets.Load(root, tiger.AnimationResource.Span);
        CombatActorState tig = new();
        var tigerCursor = new CombatAnimationCursor(tig, tiger, tigerAssets);
        tigerCursor.BeginMotion(0, 0);
        samples = 0;
        CommonEnemyAiDecision.Begin(tig, 16, null, tigerCursor, 330, 440, 100, () =>
        {
            samples++;
            return 0;
        });
        Check(samples == 2 && LegacyActorFixture.ReadInt16LittleEndian(tig.AsSpan(0xde, 2)) == 60
&& LegacyActorFixture.ReadInt16LittleEndian(tig.AsSpan(0xe0, 2)) == 50, "Actual tiger null-target decision preserves both horizontal and vertical native random passes.");
        CombatInfoCharacter zombie = CombatInfoDefinition.Load(root, "24.inf"u8).Characters[0];
        var zombieAssets = CombatAnimationAssets.Load(root, zombie.AnimationResource.Span);
        CombatActorState zom = new();
        var zombieCursor = new CombatAnimationCursor(zom, zombie, zombieAssets);
        zombieCursor.BeginMotion(1, 0);
        zom.Facing = 1;
        W(zom, 0xad, 1);
        W(zom, 0xc9, 2000);
        W(zom, 0x98, 100);
        W(zom, 0x78, 350);
        W(zom, 0x1ec, 1);
        W(target, 0x98, 150);
        W(target, 0x78, 350);
        samples = 0;
        CommonEnemyAiDecision.Continue(zom, 22, target, zombieCursor, zombie, 1000, () =>
        {
            samples++;
            return 0;
        });
        Check(samples == 0 && I(zom, 0xb1) == 6 && LegacyActorFixture.ReadInt16LittleEndian(zom.AsSpan(0xdb, 2)) == 0, "Actual zombie2 continuation falls through to type23 range40..60 and attacks without random samples.");
        CombatInfoCharacter goblin2 = CombatInfoDefinition.Load(root, "20.inf"u8).Characters[0];
        var goblin2Assets = CombatAnimationAssets.Load(root, goblin2.AnimationResource.Span);
        CombatActorState gob2 = new();
        var gob2Cursor = new CombatAnimationCursor(gob2, goblin2, goblin2Assets);
        gob2Cursor.BeginMotion(1, 0);
        gob2.Facing = 1;
        W(gob2, 0xad, 1);
        W(gob2, 0xc9, 2000);
        W(gob2, 0x98, 100);
        W(gob2, 0x78, 350);
        W(gob2, 0x1ec, 1);
        W(gob2, 0x170, 29);
        W(target, 0x98, 315);
        samples = 0;
        CommonEnemyAiDecision.Continue(gob2, 18, target, gob2Cursor, goblin2, 1000, () =>
        {
            samples++;
            return 0;
        });
        Check(samples == 1 && I(gob2, 0xb1) == 6 && LegacyActorFixture.ReadInt16LittleEndian(gob2.AsSpan(0xdb, 2)) == 1, "Actual goblin2 lowHP fallback consumes one conditional nibble test then binds real ranged A1.");
        CombatInfoCharacter bandKing = CombatInfoDefinition.Load(root, "26.inf"u8).Characters[0];
        var bandKingAssets = CombatAnimationAssets.Load(root, bandKing.AnimationResource.Span);
        CombatActorState king = new();
        var kingCursor = new CombatAnimationCursor(king, bandKing, bandKingAssets);
        kingCursor.BeginMotion(0, 0);
        king.Facing = 1;
        W(king, 0x98, 100);
        W(king, 0x78, 350);
        W(target, 0x98, 200);
        samples = 0;
        CommonEnemyAiDecision.Begin(king, 24, target, kingCursor, 330, 440, 100, () =>
        {
            samples++;
            return 5;
        });
        Check(samples == 1 && I(king, 0xb1) == 15 && I(king, 0xb5) == 1, "Actual band king modulo7 jump branch invokes real J0 startup with no invented idle fallback.");
        kingCursor.BeginMotion(0, 0);
        king.AsSpan(0xde, 4).Clear();
        samples = 0;
        CommonEnemyAiDecision.Begin(king, 33, null, kingCursor, 330, 440, 100, () =>
        {
            samples++;
            return 0;
        });
        Check(samples == 2 && I(king, 0x32d) == 1 && LegacyActorFixture.ReadInt16LittleEndian(king.AsSpan(0xde, 2)) == 100
&& LegacyActorFixture.ReadInt16LittleEndian(king.AsSpan(0xe0, 2)) == 15, "Type33 constant horizontal addition consumes only decision and vertical samples, including null target.");
        kingCursor.BeginMotion(0, 0);
        king.AsSpan(0xde, 4).Clear();
        samples = 0;
        CommonEnemyAiDecision.Begin(king, 27, null, kingCursor, 330, 440, 100, () =>
        {
            samples++;
            return 6;
        });
        Check(samples == 1 && I(king, 0xad) == 0x40 && I(king, 0xb1) == 0, "Type27 modulo7 default exits before either additional random sample.");
        kingCursor.BeginMotion(0, 0);
        king.AsSpan(0xde, 4).Clear();
        samples = 0;
        CommonEnemyAiDecision.Begin(king, 34, null, kingCursor, 330, 440, 100, () =>
        {
            samples++;
            return 0;
        });
        Check(samples == 1 && I(king, 0x32d) == 1 && LegacyActorFixture.ReadInt16LittleEndian(king.AsSpan(0xde, 2)) == 10, "Type34 keep-distance decision uses constant10 and has no vertical or horizontal extra RNG.");
        W(king, 0xad, 1);
        W(king, 0xc9, 2000);
        W(king, 0x351, 3);
        W(king, 0x355, 0);
        samples = 0;
        CommonEnemyAiDecision.Continue(king, 38, null, kingCursor, bandKing, 1000, () =>
        {
            samples++;
            return 1;
        });
        Check(samples == 1 && I(king, 0x351) == 0, "Type38 fallback consumes one attack sample and wraps index above2 even without a target.");
        W(king, 0xad, 1);
        W(king, 0xc9, 2000);
        W(king, 0x351, 3);
        samples = 0;
        CommonEnemyAiDecision.Continue(king, 39, null, kingCursor, bandKing, 1000, () =>
        {
            samples++;
            return 1;
        });
        Check(samples == 1 && I(king, 0x351) == 3, "Type39 fallback does not share other bosses' unconditional cycle-index wrap.");
    }

    private static void CheckFrameController(string root, CombatSceneDefinition scene)
    {
        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var state = InitialGameState.Create(items, LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat"))));
        var runtime = new CombatSceneRuntime(root, scene, 0);
        runtime.BindDeclaredAnimations(0);
        var selection = new CombatPartySelection(state, 1);
        var selectionSounds = new List<string>();
        selection.ApplyInput((_, input) => input is CombatSelectionInput.Toggle or CombatSelectionInput.Finish, selectionSounds.Add);
        runtime.PopulateSelectedParty(selection);
        runtime.BindAllAttributes(0, 0, 0, () => 1);
        runtime.BindPlayerRoles(selection);
        var effects = new FrameEffectsRecorder();
        var input = new CombatScanInput(_ => false);
        var bindings = ControlBindings.Parse(File.ReadAllBytes(Path.Combine(root, "BATTLE.CFG")));
        var actions = new CombatActorController(runtime, state, bindings, input, effects, () => 1);
        int translations = 0;
        var camera = new CombatCameraBounds(runtime, new(830, 480), (dx, dy) => translations++);
        state.Globals[61] = 7;
        state.Globals[68] = 23;
        state.Globals[69] = 19;
        var frames = new CombatFrameRuntime(runtime, state, camera, actions, effects, () => 1);
        Check(state.Globals[61] == 7 && !frames.RewardFlag, "Battle setup preserves non1 reward WORD and only exact1 enables native reward branch.");
        CombatFrameResult first = frames.Step(0, false);
        Check(first.ReturnCode == 0 && first.FriendlyCount == 1 && first.EnemyCount == 4 && first.PendingCount == 3 && frames.PreviousEnemyCount == 4
&& state.Globals[51] == 7 && state.Globals[52] == 1 && state.Globals[61] == 7 && state.Globals[68] == 0 && state.Globals[69] == 19 && camera.Primary.X == 2 && translations == 1, "Actual initial actor controller/frame pass preserves four active plus three pending, single leader camera scroll and no invented result.");
        CombatBattleAssets assets = CombatBattleAssets.Load(root, scene);
        var renderer = new CombatSceneRenderer(runtime, assets, camera, frames.Shake);
        renderer.PrepareActors(0);
        renderer.AppendParticles([]);
        ushort[] rendered = new ushort[640 * 480], secondPresent = new ushort[640 * 480];
        renderer.DrawWorldAndMenu(rendered);
        renderer.DrawWorldAndMenu(secondPresent);
        Check(rendered.AsSpan().SequenceEqual(secondPresent) && rendered[0] == assets.Background[2]
&& LegacyActorFixture.ReadInt32LittleEndian(runtime.Actors[8].State.AsSpan(0x23c, 4)) == 59, "Actual world camera crop/actor rendering is repeatable and GPU presents do not consume leader flash ticks.");
        var hud = new CombatHudRaster(runtime, assets, new CombatStatusSlot[CombatStatusSlots.GlobalCount]);
        hud.AdvanceVisualState(0, false);
        hud.Draw(rendered);
        int hp = LegacyActorFixture.ReadInt32LittleEndian(runtime.Actors[8].State.AsSpan(0x170, 4));
        Check(hp % 163 > 0 && rendered[448 * 640 + 153] == CombatHudPalette.HitPoints[0], "Actual leader HP uses native163-cycle bar and converted HP gradient.");
        runtime.TargetIndices[8] = 0;
        LegacyActorFixture.WriteInt32LittleEndian(runtime.Actors[8].State.AsSpan(0xa9, 4), 100);
        hud.AdvanceVisualState(99, false);
        Check(hud.CurrentActorIndex == 0, "PostHUD current actor is the visible unexpired target used by subsequent item53 processing.");
        hud.AdvanceVisualState(100, false);
        Check(hud.CurrentActorIndex == 8, "Target expiry equality restores leader as postHUD current actor.");
        LegacyActorFixture.WriteInt32LittleEndian(runtime.Actors[8].State.AsSpan(0x230, 4), 0);
        hud.AdvanceVisualState(100, false);
        Check(hud.CurrentActorIndex == runtime.Actors.Count - 1, "No-leader HUD leaves the final scanned party actor in native current actor global.");
        LegacyActorFixture.WriteInt32LittleEndian(runtime.Actors[8].State.AsSpan(0x230, 4), 1);
        runtime.TargetIndices[8] = -1;
        var blink = new CombatStatusSlot { Deadline = 10000 };
        Check(CombatStatusRuntime.AdvanceVisible(ref blink, 0) && CombatStatusRuntime.AdvanceVisible(ref blink, 1000)
&& !CombatStatusRuntime.AdvanceVisible(ref blink, 1001) && blink.BlinkDeadline == 1701, "Status blink retains strict deadline equality and700ms intermediate-duration toggle.");
        for (int i = 0; i < runtime.Scene.PartyStartIndex; i++)
        {
            LegacyActorFixture.WriteInt32LittleEndian(runtime.Actors[i].State.AsSpan(0x224, 4), 0);
            LegacyActorFixture.WriteInt32LittleEndian(runtime.Actors[i].State.AsSpan(0x60, 4), 0);
        }

        CombatFrameResult victory = frames.Step(1, false);
        Check(victory.ReturnCode == 1 && victory.RewardFlag && victory.FriendlyCount == 1 && victory.EnemyCount == 0 && victory.PendingCount == 0, "Native frame victory predicate requires actual absence of both active and pending enemies.");
    }

    private sealed class FrameEffectsRecorder : ICombatControlEffects
    {
        public readonly List<string> Events = [];
        public bool GlobalEffectBusy => false;

        public void PlaySound(int id) => Events.Add("sound" + id);
        public void SpawnActorParticle(int actor, int id, uint now) => Events.Add($"particle{actor}:{id}");
        public void ApplyFrameEffect(int actor, int id, int argument, uint now) => Events.Add($"effect{actor}:{id}:{argument}");
        public void ShowConsumedItemNotice(int id, uint now) => Events.Add("item" + id);
        public void ShowRankNotice(int rank, uint now) => Events.Add("rank" + rank);
    }

    private static void CheckFrameEffects()
    {
        CombatActorState actor = new();
        void W(int offset, int value) => LegacyActorFixture.WriteInt32LittleEndian(actor.AsSpan(offset, 4), value);
        int I(int offset) => LegacyActorFixture.ReadInt32LittleEndian(actor.AsSpan(offset, 4));
        W(0xb1, 14);
        W(0xf6, 14);
        W(0xea, 2);
        W(0xe6, 4);
        CombatAnimationFrameEffects.ApplyDisplacement(actor);
        Check(I(0x7c) == 7 && I(0x70) == 3 && I(0xe6) == 2 && I(0xea) == 1, "Reaction14 falls through into mode14 movement using its already decelerated velocity.");
        ushort[] words = new ushort[28];
        words[7] = 200;
        words[17] = 1;
        words[18] = 24;
        words[19] = 9;
        words[20] = 3;
        byte[] fields = new byte[Atria.Tests.Combat.LegacyCombatInfoFixture.ImageSize];
        LegacyActorFixture.WriteInt32LittleEndian(fields.AsSpan(0xad4, 4), 76);
        var info = Atria.Tests.Combat.LegacyCombatInfoFixture.DecodeImage(ReadOnlyMemory<byte>.Empty, fields);
        var events = new List<string>();
        int rolls = 0;
        W(0x170, 2);
        W(0x2b4, 1);
        W(0x240, 1);
        CombatAnimationFrameEffects.ApplyEvents(actor, info, new(words, 4, 0, 0), 0, () =>
        {
            rolls++;
            return 0;
        }, s => events.Add("sound" + s), p => events.Add("particle" + p), (effect, argument) => events.Add($"effect{effect}:{argument}"));
        Check(events.SequenceEqual(new[] { "sound200", "sound76", "particle24", "effect3:9" }) && actor.HitLatch == 1 && I(0x170) == 0 && I(0x224) == 0
&& I(0x240) == 0 && rolls == 1, "Bound-frame events retain audio/particle/deactivation/poison RNG/final effect ordering.");
    }

    private static void CheckRuntimeProjection(string root, CombatSceneDefinition scene)
    {
        var runtime = new CombatSceneRuntime(root, scene, 0);
        static int I(object b, int o) => LegacyActorFixture.ReadInt32LittleEndian(b.FixtureSpan(o, 4));
        Check(runtime.Actors.Count == 14 && runtime.InfoBindings.Count == 2 && runtime.Actors[0].Binding!.NativeIndex == 0
&& runtime.Actors[1].Binding!.NativeIndex == 1 && ReferenceEquals(runtime.Actors[1].Binding, runtime.Actors[6].Binding) && !ReferenceEquals(runtime.Actors[0].Binding, runtime.Actors[1].Binding), "Native zero-sentinel cache reload produces two distinct shared BANDIT1 info groups.");
        Check(I(runtime.Actors[0].State, 0x78) == 341 && I(runtime.Actors[4].State, 0x60) == 1 && I(runtime.Actors[4].State, 0x64) == 1
&& runtime.Actors[7].Binding is null && I(runtime.Actors[8].State, 0x70) == 443, "Exact declared actor Y table, pending wave fields, unused slot and party marker ownership.");
        runtime.BindDeclaredAnimations(0);
        byte[][] beforeFrozen = runtime.Actors.Select(a => a.State.ToArray()).ToArray();
        var frozenBackdrop = new CombatSelectionBackdrop(root, runtime);
        ushort[] frozenCopy = new ushort[640 * 480];
        frozenBackdrop.Draw(frozenCopy);
        Check(frozenCopy.AsSpan().SequenceEqual(frozenBackdrop.Pixels.Span) && frozenCopy[639] == 0
&& frozenCopy.AsSpan(400 * 640).ContainsAnyExcept((ushort)0), "Actual preselection background preserves exclusive639 world edge and original bottom battle menu.");
        //Projection computes screen rectangles once, while mutable animation/timer fields remain frozen.
        for (int i = 0; i < runtime.Actors.Count; i++)
        {
            Check(runtime.Actors[i].State.AsSpan(0xad, 0x4a).SequenceEqual(beforeFrozen[i].AsSpan(0xad, 0x4a)), "Generic frozen preselection renderer does not advance action/frame/timer counters.");
        }

        var items = ItemCatalog.Parse(File.ReadAllBytes(Path.Combine(root, "ATITEM.DAT")));
        var state = InitialGameState.Create(items, LegacyTextTables.ParseMessages(File.ReadAllBytes(Path.Combine(root, "TEXTmsg.dat"))));
        var selection = new CombatPartySelection(state, 1);
        var selectionSounds = new List<string>();
        selection.ApplyInput((_, input) => input is CombatSelectionInput.Toggle or CombatSelectionInput.Finish, selectionSounds.Add);
        Check(selection.Completed && selectionSounds.SequenceEqual(new[] { "click.wav", "start.wav" }), "Fixture explicitly selects Roland before actual native confirmation; initial260flags are zero.");
        runtime.PopulateSelectedParty(selection);
        int random = 0;
        runtime.BindAllAttributes(0, 0, 0, () =>
        {
            random++;
            return 1;
        });
        runtime.BindPlayerRoles(selection);
        Check(runtime.InfoBindings.Count == 3 && runtime.Actors[8].Binding!.Info.Type == 0 && random == 7 && runtime.AccumulatedAward == 105, "Actual ROLAND plus packed224 form selects native party INF, with seven ordered enemy attribute samples.");
        Check(I(runtime.Actors[0].State, 0x170) == 60 && runtime.Actors[8].ControlPlayer == 0 && runtime.Actors[8].State.ControlledBy == 1
&& I(runtime.Actors[8].State, 0x230) == 1 && I(runtime.Actors[8].State, 0x22c) == 3, "Complete actual selected actor projection and native player leader/control rank fields.");
        CombatActorImage roland = runtime.Actors[8];
        byte[] controls = [10, 11, 12, 13, 14, 15, 16, 17];
        var heldScans = new HashSet<byte>();
        var manualInput = new CombatScanInput(heldScans.Contains);
        heldScans.Add(11);
        CombatManualControl.Walk(roland.State, roland.Binding!.Info, roland.Animation!, controls, manualInput, 100);
        int initialX = I(roland.State, 0x70);
        int due = I(roland.State, 0xc9);
        CombatManualControl.Walk(roland.State, roland.Binding.Info, roland.Animation!, controls, manualInput, unchecked((uint)(due + 1)));
        Check(I(roland.State, 0xb1) == 1 && I(roland.State, 0x70) == initialX + unchecked((sbyte)roland.Binding.Info.Motions[1].StepX), "Actual Roland manual walking moves only after strict native movement deadline using signed byte INF step.");
        CombatManualControl.Attack(roland.State, roland.Binding.Info, roland.Animation!, 500);
        Check(I(roland.State, 0xb1) == 6 && LegacyActorFixture.ReadInt16LittleEndian(roland.State.AsSpan(0xdb, 2)) == 0, "Actual Roland manual attack selects first A sequence and native attack mode.");
        Check(CombatPartyControl.Cycle(runtime.Actors, 8, 8, 0x1234) == 8 && roland.ControlPlayer == 0 && roland.State.ControlledBy == 1
&& I(roland.State, 0x23c) == 60 && I(roland.State, 0x294) == 0x1234, "Single selected actor cycles through native fallback to itself and retains actual binding plus60-frame flash.");
        CombatActorImage bandit = runtime.Actors[0];
        int deathRolls = 0;
        var deathSounds = new List<int>();
        LegacyActorFixture.WriteInt32LittleEndian(bandit.State.AsSpan(0x170, 4), 0);
        LegacyActorFixture.WriteInt32LittleEndian(bandit.State.AsSpan(0xb1, 4), 10);
        Check(!CombatDownedRecovery.Complete(bandit.State, bandit.Binding!.Info, bandit.Animation!, state, () =>
        {
            deathRolls++;
            return 15;
        }, deathSounds.Add, _ => throw new InvalidOperationException("Bandit has no revival items."), () => throw new InvalidOperationException("Bandit is not player controlled.")) && deathRolls == 1 && deathSounds.SequenceEqual(new[] { 77 }) && I(bandit.State, 0x1ec) == 0 && (I(bandit.State, 0xad) & 0x8000) != 0 && I(bandit.State, 0xf6) == 17, "Actual bandit downed completion consumes one mode10 sample, plays real X2 death sound and binds death sequence.");
        LegacyActorFixture.WriteInt32LittleEndian(roland.State.AsSpan(0xb1, 4), 2);
        LegacyActorFixture.WriteInt32LittleEndian(roland.State.AsSpan(0x331, 4), 0);
        Check(CombatJumpRuntime.Begin(roland.State, roland.Binding.Info, roland.Animation!, 1000) && I(roland.State, 0xb9) == 16
&& I(roland.State, 0xb1) == 0 && I(roland.State, 0xe2) == -13 && I(roland.State, 0x33d) == 17, "Actual running Roland jumps through J0 directly to separately bound J1 airborne phase.");
        int jumpDue = I(roland.State, 0xc9), jumpX = I(roland.State, 0x70);
        CombatJumpRuntime.Step(roland.State, roland.Binding.Info, roland.Animation!, unchecked((uint)jumpDue));
        Check(I(roland.State, 0x70) == jumpX + 5 && I(roland.State, 0xe2) == -12 && I(roland.State, 0x33d) == 16, "Jump movement fires at inclusive deadline, with native running velocity and gravity/countdown order.");
        CombatActorImage idleBandit = runtime.Actors[1];
        int frameNow = I(idleBandit.State, 0xbd);
        var actualEvents = new List<string>();
        var shake = new CombatCameraShake();
        CombatAnimationRuntime.Advance(
            idleBandit,
            state,
            unchecked((uint)frameNow),
            0,
            () => throw new InvalidOperationException("Idle bandit must not draw poison/death RNG."),
            sound => actualEvents.Add("sound" + sound),
            particle => actualEvents.Add("particle" + particle),
            (effect, argument) => actualEvents.Add($"effect{effect}:{argument}"),
            notice => actualEvents.Add("notice" + notice),
            () => actualEvents.Add("cycle"),
            shake
        );
        Check(idleBandit.State.HitLatch == 1 && I(idleBandit.State, 0xbd) == frameNow + I(idleBandit.State, 0xc1)
&& idleBandit.Animation!.BoundFrameIndex == 0, "Actual idle frame advance updates events/deadline while retaining bound collision frame until projection.");
        shake.ApplyFrame(0xf00);
        Check(shake.Amplitude == 5 && shake.Remaining == 10 && shake.Phase == 0, "Native shake flags select first100bit, not largest magnitude.");
        int guardRolls = 0;
        Check(!CombatGuard.TryBlock(roland, runtime.Actors[2], 1200, () =>
        {
            guardRolls++;
            return 9;
        }, _ => throw new InvalidOperationException("Actual bandit defense10 must not block roll9."), _ => throw new InvalidOperationException("Unblocked hit must not play guard sound.")) && guardRolls == 1, "Guard consumes its native sample before testing actual INF defense10 against modulo10.");
        CombatActorImage hitTarget = runtime.Actors[3];
        LegacyActorFixture.WriteInt32LittleEndian(hitTarget.State.AsSpan(0x170, 4), 10000);
        roland.Animation!.BeginAttack(0, 1300);
        var hitState = new CombatHitState(runtime.Actors.Count);
        int reactionCalls = 0, hitRolls = 0;
        CombatHitResult hit = CombatHitRuntime.Apply(runtime, hitState, 8, 3, state, 1300, true, () =>
        {
            hitRolls++;
            return 9;
        }, particle => actualEvents.Add("hitparticle" + particle.Id), sound => actualEvents.Add("hitsound" + sound), () => reactionCalls++, _ => throw new InvalidOperationException("Bounded high-HP fixture must not die."));
        Check(hit == CombatHitResult.DamageApplied && reactionCalls == 1 && hitState.LastHitTargets[8] == 3 && I(hitTarget.State, 0x170) < 10000
&& I(hitTarget.State, 0x365) == 0 && hitState.ComboTotal == 0 && hitRolls == 2 + (roland.Animation.CurrentFrame.Words[23] > 6 ? 1 : 0), "Actual Roland/BANDIT1 hit flow preserves guard/base/critical RNG, reaction boundary and first zero combo.");
        var hitExecutor = new CombatHitExecutor(runtime, state, () => 9, particle => actualEvents.Add("actualparticle" + particle.Id), sound => actualEvents.Add("actualsound" + sound));
        Check(hitExecutor.Execute(8, 3, 1400) == CombatHitResult.DamageApplied && (I(hitTarget.State, 0xb1) is 7 or 8)
&& hitTarget.Animation!.BoundFrameIndex == 0 && hitExecutor.Hits.LastHitTargets[8] == 3, "Production hit executor binds actual BANDIT1 reaction sequence and preserves managed last-hit identity.");
    }

    private static void CheckSpawns()
    {
        CombatActorState actor = new();
        void W(int o, int v) => LegacyActorFixture.WriteInt32LittleEndian(actor.AsSpan(o, 4), v);
        int R(int o) => LegacyActorFixture.ReadInt32LittleEndian(actor.AsSpan(o, 4));
        W(0x60, 1);
        W(0x64, 1);
        W(0x68, 1);
        var result = CombatSpawnController.Update(actor, 1, 4, -80, 1000, 330, 440, 0, () => throw new InvalidOperationException());
        Check(result == new CombatSpawnResult(1, false) && R(0x60) == 1, "Native spawn return1 can mean waiting; four existing enemies block state1.");
        result = CombatSpawnController.Update(actor, 1, 3, -80, 1000, 330, 440, 0, () => 119);
        Check(result.Activated && R(0x60) == 0 && R(0x70) == -180 && R(0x7c) == -180 && R(0x78) == 339 && R(0x80) == 339, "Actual left wave spawn chooses native modulo Y and offscreen X.");
        W(0x60, 1);
        W(0x64, 2);
        W(0x7c, 77);
        W(0x80, 88);
        result = CombatSpawnController.Update(actor, 1, 3, -80, 1000, 330, 440, 0, () => 0);
        Check(result.Activated && R(0x70) == 1100 && R(0x78) == 330 && R(0x7c) == 77 && R(0x80) == 88, "Right-spawn branch preserves native omission of projected X/Y writes.");
        W(0x60, 7);
        W(0x64, 0);
        W(0x6c, 100);
        Check(!CombatSpawnController.Update(actor, 1, 0, -80, 1000, 330, 440, 6100, () => throw new InvalidOperationException()).Activated, "Spawn clock deadline equality waits.");
        Check(CombatSpawnController.Update(actor, 0, 0, -80, 1000, 330, 440, 6101, () => throw new InvalidOperationException()) == new CombatSpawnResult(0, false), "Encounter counter gates timed waves before clock test.");
        Check(CombatSpawnController.Update(actor, 1, 0, -80, 1000, 330, 440, 6101, () => throw new InvalidOperationException()).Activated, "Timed wave activates strictly after its6-second delay without unused placement RNG.");
    }

    private static void CheckSpriteProjection()
    {
        CombatActorState actor = new();
        void W(int o, int v) => LegacyActorFixture.WriteInt32LittleEndian(actor.AsSpan(o, 4), v);
        int R(int o) => LegacyActorFixture.ReadInt32LittleEndian(actor.AsSpan(o, 4));
        var sprite = new Atria.Render.Codecs.SpriteFrame(0, 12, 34, [], [], 100, 80, []);
        var words = new ushort[28];
        words[1] = 5;
        words[2] = 7;
        var frame = new Atria.Render.Codecs.AnimationFrame(words, 0, 5, 7);
        W(0x70, 200);
        W(0x78, 300);
        actor.Facing = 1;
        CombatSpriteProjection.Update(actor, 9, frame, sprite);
        CombatSpriteProjection.UpdateTrackingX(actor, sprite);
        Check(R(0x7c) == 205 && R(0x80) == 227 && R(0x90) == 227 && R(0x94) == 300 && R(0x98) == 239, "Actual first motion frame captures base Y and derives tracking X from sprite metadata.");
        LegacyActorFixture.WriteInt16LittleEndian(actor.AsSpan(0xd5, 2), 1);
        W(0x78, 305);
        words[2] = 10;
        CombatSpriteProjection.Update(actor, 9, frame, sprite);
        Check(R(0x80) == 242, "Later frame motion retains native baseY/ground delta formula.");
        W(0xb1, 4);
        W(0xad, 0x40);
        actor.Facing = 255;
        CombatSpriteProjection.Update(actor, 9, frame, sprite);
        Check(R(0x7c) == 95 && R(0x80) == 225, "Mirrored actor projection subtracts actual width and frame offset.");
        W(0xb9, 16);
        W(0x80, 300);
        W(0x7c, 123);
        CombatSpriteProjection.Update(actor, 9, frame, sprite);
        Check(R(0x80) == 225 && R(0x74) == 225 && R(0x7c) == 123, "Jump16 projection returns after native ground adjustment without rewriting X.");
    }

    private static void CheckTargetSelection()
    {
        CombatActorState[] actors = Enumerable.Range(0, 8).Select(_ => new CombatActorState()).ToArray();
        int[] targets = Enumerable.Repeat(-1, 8).ToArray();
        static void W(object b, int o, int v) => LegacyActorFixture.WriteInt32LittleEndian(b.FixtureSpan(o, 4), v);
        static int R(object b, int o) => LegacyActorFixture.ReadInt32LittleEndian(b.FixtureSpan(o, 4));
        actors[0].Team = 1;
        W(actors[0], 0x98, 100);
        W(actors[0], 0x78, 350);
        for (int i = 2; i < 4; i++)
        {
            actors[i].Team = 2;
            actors[i].CanTurn = 1;
            W(actors[i], 0x224, 1);
            W(actors[i], 0x170, 100);
            W(actors[i], 0x78, 350);
        }

        W(actors[2], 0x98, 110);
        W(actors[3], 0x98, 90);
        actors[3].ControlledBy = 1;
        int calls = 0;
        int Pick(uint now) => CombatTargetSelection.Select(actors, targets, 0, 0, 8, now, () =>
        {
            calls++;
            return 0;
        });
        Check(Pick(100) == 2 && calls == 1 && R(actors[0], 0x371) == 13100, "Initial target policy samples once and Manhattan ties choose first actual index.");
        Check(Pick(13100) == 2 && calls == 1, "Policy deadline equality does not rotate or consume RNG.");
        Check(Pick(13101) == 3 && calls == 1 && R(actors[0], 0x36d) == 1, "Expired policy rotates to first player-controlled target without random consumption.");
        W(actors[0], 0x371, 99999);
        W(actors[0], 0x36d, 2);
        W(actors[2], 0x1f4, 10);
        W(actors[3], 0x1f4, 20);
        Check(Pick(14000) == 3, "Policy2 selects strict maximum attack.");
        W(actors[0], 0x36d, 3);
        Check(Pick(14000) == 2, "Policy3 selects strict minimum attack.");
        W(actors[0], 0x36d, 4);
        targets[3] = 0;
        Check(Pick(14000) == 3, "Policy4 selects first eligible actor actually targeting this attacker.");
        W(actors[0], 0x36d, 8);
        W(actors[3], 0x170, 120);
        Check(Pick(14000) == 3, "Policy8 selects maximum currentHP.");
        W(actors[3], 0x208, 55);
        Check(Pick(14000) == -1, "Selected anti-target item55 clears target without a second fallback.");
        W(actors[3], 0x20c, 56);
        actors[3].CanTurn = 0;
        W(actors[3], 0x224, 0);
        W(actors[0], 0x371, 0);
        Check(CombatTargetSelection.Select(actors, targets, 0, 0, 8, 14000, () => 1) == 3 && R(actors[0], 0x371) == 0, "Item56 early selection precedes normal eligibility, item55 rejection and target-policy initialization.");
        actors[0].Team = 100;
        Check(CombatTargetSelection.Eligible(actors[0], actors[2]), "Signed team100 targets lower teams through native separate branch.");
        actors[2].CanTurn = 0;
        actors[0].Team = 99;
        Check(!CombatTargetSelection.Eligible(actors[0], actors[2]), "Ordinary hostile target requires target participation byteA7.");
    }

    private static void CheckBanditDecision(CombatInfoCharacter info, CombatAnimationAssets assets)
    {
        static void W(object b, int o, int v) => LegacyActorFixture.WriteInt32LittleEndian(b.FixtureSpan(o, 4), v);
        static int I(object b, int o) => LegacyActorFixture.ReadInt32LittleEndian(b.FixtureSpan(o, 4));
        static short S(object b, int o) => LegacyActorFixture.ReadInt16LittleEndian(b.FixtureSpan(o, 2));
        CombatActorState actor = new(), target = new CombatActorState();
        W(actor, 0x98, 100);
        W(actor, 0x78, 350);
        W(target, 0x98, 190);
        W(target, 0x78, 380);
        actor.Facing = 1;
        var animation = new CombatAnimationCursor(actor, info, assets);
        animation.BeginMotion(0, 0);
        var rolls = new Queue<int>(new[] { 1, 0, 2 });
        BanditAiDecision.Begin(actor, target, animation, 330, 440, 1000, () => rolls.Dequeue());
        Check(rolls.Count == 0 && animation.SequenceIndex == 1 && I(actor, 0xb1) == 1 && I(actor, 0xad) == 1 && S(actor, 0xde) == 80
&& S(actor, 0xe0) == 7 && I(actor, 0x9c) == 3 && I(actor, 0xa0) == 2 && I(actor, 0xc1) == 45 && I(actor, 0xc9) == 1030, "Actual bandit midrange motion, three ordered rolls, signed step modifiers and per-motion clocks.");
        rolls = new(new[] { 0, 6 });
        BanditAiDecision.Begin(actor, target, animation, 330, 440, 2000, () => rolls.Dequeue());
        Check(rolls.Count == 0 && animation.SequenceIndex == 0 && I(actor, 0xad) == 0x40 && I(actor, 0xc1) == 150 && I(actor, 0xbd) == 2045, "Bandit default branch returns before third RNG and idle keeps the prepared animation deadline.");
        W(actor, 0x98, 100);
        W(actor, 0x78, 350);
        W(target, 0x98, 100);
        W(target, 0x78, 350);
        W(actor, 0x70, 100);
        actor.Facing = 255;
        int oldWidth = animation.CurrentSprite.Width;
        rolls = new(new[] { 1, 5, 7, 0, 5 });
        BanditAiDecision.Begin(actor, target, animation, 330, 440, 3000, () => rolls.Dequeue());
        Check(rolls.Count == 0 && actor.Facing == 1 && I(actor, 0x70) == 100 - oldWidth && S(actor, 0xde) == 58 && S(actor, 0xe0) == 12
&& I(actor, 0xa0) == -2, "Close bandit decision consumes two extra arena rolls and preserves native facing-anchor shift.");
        W(actor, 0x98, 100);
        W(actor, 0x78, 350);
        W(target, 0x98, 150);
        W(target, 0x78, 350);
        W(actor, 0x1ec, 1);
        animation.BeginAutomaticMovement(1, 90, 30, 4000);
        W(actor, 0x32d, 0);
        W(actor, 0x329, 5000);
        W(actor, 0x120, 5000);
        int samples = 0;
        BanditAiDecision.Continue(actor, target, animation, info, 5000, () =>
        {
            samples++;
            return 0;
        });
        Check(samples == 1 && I(actor, 0xb1) == 1 && S(actor, 0xde) == 29 && S(actor, 0xe0) == 15, "Bandit advances horizontal-first movement but exact cooldown equality blocks attack.");
        BanditAiDecision.Continue(actor, target, animation, info, 5001, () =>
        {
            samples++;
            return 0;
        });
        Check(samples == 2 && animation.SequenceIndex == 12 && I(actor, 0xb1) == 6 && I(actor, 0xad) == 0x40 && I(actor, 0xbd) == 0
&& I(actor, 0xc1) == 25 && I(actor, 0xcd) == 5101 && I(actor, 0x329) == 7001, "Actual bandit ranged decision binds attack0 ANI and original cooldown/chain timing after strict boundary.");
        animation.BeginAutomaticMovement(2, 0, 0, 6000);
        BanditAiDecision.Continue(actor, target, animation, info, 6011, () => throw new InvalidOperationException("Run-stop gate must return before attack RNG."));
        Check(animation.SequenceIndex == 4 && I(actor, 0xb1) == 3 && I(actor, 0xad) == 0x40, "Finished running uses native motion3 before any attack-decision RNG.");
    }

    private static void CheckStatuses()
    {
        CombatStatusSlot[] slots = new CombatStatusSlot[CombatStatusSlots.GlobalCount];
        void W(int offset, int value)
        {
            byte[] encoded = CombatStatusCodec.Encode(slots);
            LegacyActorFixture.WriteInt32LittleEndian(encoded.AsSpan(offset, 4), value);
            CombatStatusCodec.Decode(encoded, slots);
        }
        int R(int offset) => LegacyActorFixture.ReadInt32LittleEndian(CombatStatusCodec.Encode(slots).AsSpan(offset, 4));
        for (int i = 0; i < 10; i++)
        {
            W(i * 24, 100);
            W(i * 24 + 4, 99);
            W(i * 24 + 20, 77);
        }

        W(24 + 4, 15);
        W(48, 0);
        int selected = CombatStatusSlots.Insert(slots, 16, 0, 7, 500, 0, 50);
        Check(selected == 1 && R(24) == 500 && R(36) == 7 && R(20) == 0 && R(44) == 77, "Status code refresh precedes expired slots and preserves native first-slot counter write.");
        selected = CombatStatusSlots.Insert(slots, 17, 0, 0, 600, 9, 50);
        Check(selected == 2 && R(60) == 1, "Enemy-sourced status field3 inversion and first expired-slot selection.");
        for (int i = 0; i < 3; i++)
        {
            W(i * 24, 50);
        }

        selected = CombatStatusSlots.Insert(slots, 18, 0, 1, 700, 9, 50);
        Check(selected == 0 && R(12) == 0, "Deadline equality is not expiry; full bank overwrites slot0.");
        W(9 * 24, 0);
        selected = CombatStatusSlots.Insert(slots, 19, 1, 1, 800, 0, 50);
        Check(selected == 9 && R(9 * 24 + 8) == 1, "Nonzero bank flag searches all10 native slots.");
    }

    private static void CheckAttackMath()
    {
        CombatActorState attacker = new(), target = new CombatActorState();
        static void W(object b, int o, int v) => LegacyActorFixture.WriteInt32LittleEndian(b.FixtureSpan(o, 4), v);
        W(attacker, 0x1f4, 10);
        W(attacker, 0x1ec, 3);
        W(target, 0x1f0, 4);
        W(target, 0x2a0, 2);
        W(attacker, 0x2a4, 1);
        var frame = new Atria.Render.Codecs.AnimationFrame(new ushort[28], 0, 0, 0);
        frame.Words[8] = 5;
        int calls = 0;
        Check(CombatAttackDamageMath.BaseAmount(attacker.AttackStats, 2, frame, target.AttackStats, 128, () =>
        {
            calls++;
            return 5;
        }) == 30 && calls == 1, "Signed low-byte class2 roll128 qualifies, adding remainder2 times attack10.");
        Check(CombatAttackDamageMath.BaseAmount(attacker.AttackStats, 2, frame, target.AttackStats, 100, () => throw new InvalidOperationException()) == 10, "Exact signed-byte100 boundary does not consume class2 extra RNG.");
        W(target, 0x1f0, 999);
        Check(CombatAttackDamageMath.BaseAmount(attacker.AttackStats, 9, frame, target.AttackStats, 0, () => throw new InvalidOperationException()) == 1, "Native signed base damage floors1 before optional class2 addition.");
        frame.Words[23] = 7;
        W(attacker, 0x29c, 16);
        W(attacker, 0x1e0, 16);
        Check(CombatAttackDamageMath.CriticalAmount(attacker.AttackStats, frame, 10, () => 31) == 30
&& CombatAttackDamageMath.CriticalAmount(attacker.AttackStats, frame, 10, () => 32) == 10, "Critical odds strict low-byte boundary and original odds-shift multiplier.");
        frame.Words[23] = 6;
        Check(CombatAttackDamageMath.CriticalAmount(attacker.AttackStats, frame, 10, () => throw new InvalidOperationException()) == 10, "Reaction word<=6 consumes no critical random sample.");
    }

    private static void CheckMaintenance()
    {
        CombatActorState actor = new();
        void W(int offset, int value) => LegacyActorFixture.WriteInt32LittleEndian(actor.AsSpan(offset, 4), value);
        int R(int offset) => LegacyActorFixture.ReadInt32LittleEndian(actor.AsSpan(offset, 4));
        W(0x170, 80);
        W(4, 100);
        W(8, 2);
        W(0xc, 50);
        W(0x10, 30);
        CombatActorMaintenance.RegenerateAndUpdateDisplay(actor, 50);
        Check(R(0x170) == 80 && R(8) == 2 && R(4) == 90, "Regeneration deadline is strict, while damage display still updates.");
        CombatActorMaintenance.RegenerateAndUpdateDisplay(actor, 51);
        Check(R(0x170) == 81 && R(8) == 1 && R(0xc) == 81 && R(4) == 89, "One timed recovery point and native1-point display tail.");
        W(0x170, 100);
        W(0x16c, 100);
        W(0xc, 0);
        CombatActorMaintenance.RegenerateAndUpdateDisplay(actor, 82);
        Check(R(0x170) == 101 && R(8) == 0, "Native maintenance does not invent a maximum-HP clamp.");
        W(0x2b0, 1);
        Check(CombatActorMaintenance.ModifyActionInterval(actor, 1) == 0 && CombatActorMaintenance.ModifyActionInterval(actor, -3) == -2, "Actual status interval modifier keeps signed shift and zero.");
        var frame = new Atria.Render.Codecs.AnimationFrame(new ushort[28], 0, 0, 0);
        frame.Words[22] = 65535;
        frame.Words[25] = 17;
        CombatActorMaintenance.CaptureSecondaryAction(actor, frame);
        Check(R(0x54) == 65535 && R(0x58) == 17, "Secondary-action test uses signed nonzero but stores unsigned words.");
        frame.Words[22] = 0;
        frame.Words[25] = 19;
        CombatActorMaintenance.CaptureSecondaryAction(actor, frame);
        Check(R(0x58) == 17, "Zero frame trigger retains prior secondary action.");
    }

    private static void CheckDamageApplication()
    {
        CombatActorState attacker = new(), target = new CombatActorState();
        static void W(object bytes, int offset, int value) => LegacyActorFixture.WriteInt32LittleEndian(bytes.FixtureSpan(offset, 4), value);
        static int R(object bytes, int offset) => LegacyActorFixture.ReadInt32LittleEndian(bytes.FixtureSpan(offset, 4));
        W(attacker, 0x22c, 1);
        W(target, 0x22c, 1);
        W(target, 0x170, 100);
        W(target, 0x180, 300);
        W(target, 0x208, 40);
        W(target, 0x20c, 41);
        W(target, 0x210, 42);
        W(target, 0x214, 58);
        W(target, 0x218, 59);
        CombatDamageResult result = CombatDamageApplication.Apply(attacker, 0, target, 1, 30, _ => throw new InvalidOperationException("Surviving hit must not enter death reaction."));
        Check(result == new CombatDamageResult(13, false) && R(target, 0x170) == 87 && R(target, 4) == 100 && R(target, 8) == 13 && R(target, 0x10) == 0, "Native two-stage truncated rank scaling, snapshot HP, cumulative item recovery and item58 priority.");
        W(target, 0x180, 299);
        result = CombatDamageApplication.Apply(attacker, 0, target, 1, 0, _ => throw new InvalidOperationException());
        Check(result.AppliedAmount == 0 && R(target, 0x10) == 50, "Negative recovery timer resets50 while exact zero remains zero.");
        W(target, 0x170, 1);
        target.ControlledBy = 1;
        attacker.ControlledBy = 1;
        attacker.Facing = 255;
        int deaths = 0;
        result = CombatDamageApplication.Apply(attacker, 0, target, 1, 2, reaction =>
        {
            Check(reaction == new CombatDeathReaction(15, 15, 6, 4, -1), "Actual native death reaction argument contract.");
            deaths++;
        });
        Check(result.Lethal && deaths == 1 && R(target, 0x170) == 0 && R(target, 8) == 0, "Lethal HP clamps0 and requires death animation transition.");
        attacker.ControlledBy = 0;
        W(attacker, 0x22c, 4);
        Check(CombatDamageApplication.ScaleByControlRank(attacker, 0, 999) == 0 && CombatDamageApplication.ScaleByControlRank(attacker, 9, 999) == 999, "Control-rank4 protects only native playable types with automatic control flag0.");
    }

    private static void CheckHits()
    {
        CombatActorState attacker = new(), target = new CombatActorState();
        static void W(object bytes, int offset, int value) => LegacyActorFixture.WriteInt32LittleEndian(bytes.FixtureSpan(offset, 4), value);
        static void S(object bytes, int offset, short value) => LegacyActorFixture.WriteInt16LittleEndian(bytes.FixtureSpan(offset, 2), value);
        W(attacker, 0x224, 1);
        attacker.HitLatch = 1;
        attacker.Team = 1;
        target.Team = 2;
        W(attacker, 0x78, 100);
        W(target, 0x78, 112);
        W(target, 0x170, 1);
        S(attacker, 0xff, 10);
        S(attacker, 0x101, 10);
        S(attacker, 0x103, 20);
        S(attacker, 0x105, 20);
        S(target, 0x107, 20);
        S(target, 0x109, 20);
        S(target, 0x10b, 30);
        S(target, 0x10d, 30);
        var attack = new Atria.Render.Codecs.AnimationFrame(new ushort[28], 0, 0, 0);
        var defend = new Atria.Render.Codecs.AnimationFrame(new ushort[28], 0, 0, 0);
        attack.Words[9] = 10;
        bool Hit(int type = 9) => CombatHitEligibility.CanHit(0, 1, attacker, type, attack, target, defend);
        Check(Hit(), "Touching attack/hurt rectangle corners are inclusive; target active224 is not queried.");
        W(target, 0x78, 113);
        Check(!Hit(), "Enemy depth13 is strict rather than inclusive.");
        W(target, 0x78, 120);
        attack.Words[24] = 1;
        Check(Hit(0), "Playable type uses fixed depth21 instead of frame depth.");
        W(target, 0x78, 112);
        attack.Words[24] = 0;
        defend = defend with
        {
            FinalDword = 1
        };
        defend.Words[9] = 10;
        Check(!Hit(), "Defender attack priority tie blocks hit when its flag1 is set.");
        defend.Words[9] = 9;
        Check(Hit(), "Strictly stronger attack beats defender priority.");
        W(target, 0x240, 1);
        Check(!Hit(), "Native target invulnerability gate.");
        attack = attack with
        {
            FinalDword = 0x10
        };
        Check(Hit(), "Attack flag10 bypasses that invulnerability gate.");
        attack = attack with
        {
            FinalDword = 0x50
        };
        Check(!Hit(), "Attack flag40 always prevents damage processing.");
        attack = attack with
        {
            FinalDword = 0x10
        };
        target.Team = 1;
        Check(!Hit(), "Team bytes exclude friendly targets.");
        Check(attacker.HitLatch == 1, "Predicate does not pretend to consume the animation event; actual hit loop owns its reset.");
        var sprite = new Atria.Render.Codecs.SpriteFrame(0, 0, 0, [new(10, 20, 30, 40), new(5, 6, 35, 45)], [], 100, 100, []);
        W(attacker, 0x7c, 100);
        W(attacker, 0x80, 200);
        attacker.Facing = 1;
        CombatHitEligibility.RebuildRectangles(attacker, sprite);
        short R(int offset) => LegacyActorFixture.ReadInt16LittleEndian(attacker.AsSpan(offset, 2));
        Check(R(0xff) == 110 && R(0x103) == 130 && R(0x101) == 220 && R(0x105) == 240 && R(0x107) == 105 && R(0x10d) == 245, "Native expanded SHORT-pointer offsets resolve to all four fields of attack/hurt rectangles.");
        attacker.Facing = 255;
        CombatHitEligibility.RebuildRectangles(attacker, sprite);
        Check(R(0xff) == 170 && R(0x103) == 190 && R(0x107) == 165 && R(0x10b) == 195, "Mirrored rectangles use width minus opposite edge.");
    }

    private static void CheckSelection()
    {
        var state = new GameState();
        for (int actor = 0; actor < 2; actor++)
        {
            state.SetMemberStatus(actor, 1);
            state.SetPartyInt(actor, 260, 1);
        }

        var selection = new CombatPartySelection(state, 2);
        Check(selection.Leaders.SequenceEqual(new[] { 0, 1 }) && selection.PlayerCount == 2 && state.GetPartyInt(0, 76) == 0, "Actual initial leaders and detached revived HP before selection commit.");
        var sounds = new List<string>();
        selection.ApplyInput((player, input) => player == 1 && input == CombatSelectionInput.Previous || input == CombatSelectionInput.Finish, sounds.Add);
        Check(!selection.Completed && selection.ErrorCode == 5 && sounds.SequenceEqual(new[] { "move.wav", "error.wav" }), "Real input can select duplicate leaders; native finish rejects with error5.");
        selection.ApplyInput((player, input) => player == 1 && input == CombatSelectionInput.Next || input == CombatSelectionInput.Finish, sounds.Add);
        Check(selection.Completed && sounds[^1] == "start.wav", "Valid finish confirms actual selected leaders.");
        selection.Commit(state);
        Check(state.GetPartyInt(0, 256) == 1 && state.GetPartyInt(1, 256) == 1 && state.GetPartyInt(0, 76) == 1, "Native selection writes256/260 and revives zero current HP to1.");
        state.SetMemberStatus(1, 0);
        selection = new CombatPartySelection(state, 2);
        Check(selection.PlayerCount == 1 && selection.Leaders[0] == 0, "Single eligible member forces one-player mode.");
        selection.ApplyInput((player, input) => input is CombatSelectionInput.Toggle or CombatSelectionInput.Finish, sounds.Add);
        Check(!selection.Completed && selection.ErrorCode == 1, "No participants cannot auto-complete battle selection.");
    }

    private sealed class EntryRecorder : IScriptEffects
    {
        public int Calls, Flag, Number;
        public ScriptEffectKind Kind;
        public void Execute(ScriptEffect effect, GameState state)
        {
            Calls++;
            Kind = effect.Kind;
            Flag = state.EndingResetFlag;
            Number = state.PendingBattle;
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
