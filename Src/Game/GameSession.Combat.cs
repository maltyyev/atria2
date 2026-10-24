using System.Text;
using Atria.Game.Combat;
using Atria.Game.Controls;
using Atria.Game.Progression;
using Atria.Render.Codecs;

namespace Atria.Game;

public sealed partial class GameSession
{
    private bool battleActive;
    //Development --session-probe only: recover the complete caller path with the ORIGINAL untimed
    //opportunity loop. This is not a selected production modernization cadence or pacing acceptance.
    private void RunPendingBattle()
    {
        if (state.EndingResetFlag == 0)
        {
            return;
        }

        if (battleActive)
        {
            throw new InvalidOperationException("A battle cannot reenter its owning game session.");
        }

        int region = map?.AreaIndex ?? throw new InvalidOperationException("Battle attributes require the loaded world region.");
        nextBattleSeed ??= ReadBattleSeed();
        if (activeScriptHost is null)
        {
            WriteAutosave("Перед боем");
        }

        uint encounterSeed = nextBattleSeed.Value;
        nextBattleSeed = null;
        battleActive = true;
        try
        {
            RunBattleTransition();
            StopAudio();
            StopExclusiveScenarioSound();
            state.Globals[61] = 0;
            host.FrameBuffer.Clear();
            host.Present();
            presents++;
            uint entered = gate.Clock.Milliseconds;
            using CombatEntryPreparation entry = state.BattleVariant == 2 ? CombatEntryPreparation.PrepareRandom(
                root,
                CombatRandomEncounterInputs.FromGlobals(state.Globals, state.PendingBattle),
                random,
                () => encounterSeed,
                entered,
                audio.LoadEffect,
                PlayBattleExclusiveBytes
            ) : new(
    root,
    new((FixedEncounterKind)state.BattleVariant, state.PendingBattle),
    random,
    () => encounterSeed,
    entered,
    audio.LoadEffect,
    PlayBattleExclusiveBytes
);
            awardMusicTrack = entry.MusicTrack;
            var backdrop = new CombatSelectionBackdrop(entry.Runtime, entry.Assets, retainedBattleVisualDetail);
            CombatSelectionAssets? selectionAssets = CombatSelectionAssets.Load(root);
            CombatSelectionRaster? selectionRaster = new(selectionAssets);
            gate.RebaseSchedule();
            var selection = new CombatSelectionModal(state, host, selectionRaster, bindings, (_, target) => backdrop.Pixels.Span.CopyTo(target), () => music.Update(awardMusicTrack), name => PlayExclusiveScenarioSound(Encoding.ASCII.GetBytes(name)), StopExclusiveScenarioSound, chosen => entry.BindSelectedParty(chosen, region, gate.LastFrameMilliseconds), () =>
            {
                if (controls.Press(0, GameAction.Pause) || controls.Press(1, GameAction.Pause))
                {
                    ShowPause(true);
                }

                if (controls.Press(0, GameAction.Reference) || controls.Press(1, GameAction.Reference))
                {
                    ShowMoveReference(0);
                }
            });
            selection.Run(settings.PlayerCount);
            using var transport = new NativeCombatPresentationHost(window, host, settings.PlayerCount, () => music.Update(awardMusicTrack), AwaitCombatFocus);
            var combat = new CombatExecutionHost(
                root,
                entry,
                state,
                random,
                bindings,
                transport.Input,
                () => CurrentEffectsVolume,
                transport,
                () => gate.Clock.Milliseconds,
                gate.Clock.Milliseconds,
                retainedBattleRenderPeriod,
                retainedBattleVisualDetail
            );
            controls.Clear();
            controls.InBattle = true;
            transport.Input.Modern = controls;
            currentCombat = combat;
            combatControls = new(
                controls,
                entry.Runtime,
                combat.Execution.Controller,
                etc,
                smallFont,
                entry.Assets.MagicIcons,
                () => preferencesStore!.Write(new(controls.Profile, settings, configuredSettingsBindings!))
            );
            host.Overlay = combatControls.Draw;
            CombatFrameResult result = RunLegacyCombatDiagnostic(combat, transport);
            host.Overlay = null;
            controls.InBattle = false;
            controls.Clear();
            combatControls = null;
            currentCombat = null;
            transport.Dispose(); //End deferred combat UI taps before XP takes ownership of the shared input bank.
            retainedBattleRenderPeriod = combat.Ui.Schedule.Period;
            retainedBattleVisualDetail = combat.Execution.VisualDetail;
            var outcome = new CombatOutcomeInput(
                result.ReturnCode,
                state.Globals[61] == 1,
                entry.Runtime.AccumulatedAward,
                combat.Execution.Hits.Hits.ComboTotal,
                state.BattleVariant,
                region
            );
            if (outcome.RewardEligible)
            {
                var reward = CombatOutcomeRewards.Calculate(outcome.BaseAward, outcome.ComboTotal, outcome.Variant, outcome.RegionIndex);
                Console.WriteLine($"Combat reward: policy=fixed-double-no-combo, base={outcome.BaseAward}, ignoredCombo={outcome.ComboTotal}, variant={outcome.Variant}, region={outcome.RegionIndex}, total={reward.FullValue}, signedXP={reward.SignedAward}, gold={reward.MoneyDelta}.");
            }

            CombatOutcomeRewards.Complete(state, entry.Runtime.Actors, entry.Runtime.Scene.PartyStartIndex, outcome, effect =>
            {
                switch (effect.Kind)
                {
                    case CombatOutcomeEffectKind.AwardExperience:
                        AwardBattleExperience(combat.Execution, effect.Value);
                        break;
                    case CombatOutcomeEffectKind.ReleaseBattleMenu:
                        selectionRaster = null;
                        selectionAssets = null;
                        break;
                    case CombatOutcomeEffectKind.ReleaseCombatResources:
                        entry.Dispose();
                        break;
                    case CombatOutcomeEffectKind.ResetDisplayAndClear:
                        host.FrameBuffer.Clear();
                        host.Present();
                        presents++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(effect));
                }
            });
            //Retain the modern window/devices and immutable scene assets through the native owner change.
            StopExclusiveScenarioSound();
            StopAudio();
            music.Update(musicId);
            CompleteBattleReturn(result.ReturnCode);
        }
        finally
        {
            host.Overlay = null;
            controls.InBattle = false;
            controls.Clear();
            combatControls = null;
            currentCombat = null;
            StopExclusiveScenarioSound();
            battleActive = false;
            gate.RebaseSchedule();
        }

        if (activeScriptHost is null && state.Mode != GameMode.Defeat)
        {
            WriteAutosave("После боя");
        }
    }

    private void RunBattleTransition()
    {
        ushort[] source = new ushort[640 * 480];
        DrawExperienceBackdrop(state, source);
        var font = BitmapFont.DecodeEng(File.ReadAllBytes(Path.Combine(root, "ENG.FNT")), false);
        //458538 is retained zero-fill BSS; no nonempty writer has been recovered. Do not invent a label.
        var raster = new CombatTransitionRaster(source, font, []);
        var transition = new CombatTransitionRuntime();
        while (!transition.Completed)
        {
            host.Pump();
            CheckLifetime();
            AwaitCombatFocus();
            raster.Draw(transition.Frame, host.FrameBuffer);
            host.Present();
            presents++;
            transition.AfterPresented();
        }
    }

    private CombatFrameResult RunLegacyCombatDiagnostic(CombatExecutionHost combat, NativeCombatPresentationHost transport)
    {
        ulong outer = 0, frozen = 0;
        while (!combat.Coordinator.Completed)
        {
            CheckLifetime();
            if (combat.Coordinator.NativePause)
            {
                var current = transport.RefreshFrozenInput();
                CheckLifetime();
                if (!current.Ready)
                {
                    Thread.Sleep(10);
                    continue;
                }

                combat.FrozenRenderOpportunity();
                frozen++;
            }
            else
            {
                combat.OuterOpportunity();
                outer++;
            }
        }

        Console.WriteLine($"Diagnostic combat: original untimed opportunities={outer}, frozenRenders={frozen}, logicalFrames={combat.Coordinator.FrameSerial}, return={combat.Coordinator.TerminalResult!.Value.ReturnCode}. Production cadence remains unverified.");
        return combat.Coordinator.TerminalResult!.Value;
    }

    private void AwaitCombatFocus()
    {
        while (!host.Focused)
        {
            CheckLifetime();
            Thread.Sleep(10);
            host.Pump();
        }

        CheckLifetime();
        if (currentCombat is not null)
        {
            if (controls.FocusPauseRequested || controls.Press(0, GameAction.Pause) || controls.Press(1, GameAction.Pause))
            {
                ShowPause(true, currentCombat);
            }

            for (int p = 0; p < 2; p++)
            {
                if (controls.Press(p, GameAction.Reference))
                {
                    int actor = combatControls!.ActorForPlayer(p);
                    ShowMoveReference(actor < 0 ? 0 : actor - currentCombat.Execution.Runtime.Scene.PartyStartIndex);
                }
            }

            combatControls?.Update();
        }
    }

    private static uint ReadBattleSeed() => unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    private void PlayBattleExclusiveBytes(ReadOnlyMemory<byte> bytes)
    {
        var sound = audio.LoadEffect(bytes);
        try
        {
            ReplaceExclusiveScenarioSound(sound, true, CurrentEffectsVolume);
        }
        catch
        {
            sound.Dispose();
            throw;
        }
    }

    private void AwardBattleExperience(CombatExecutionRuntime execution, int signedAward)
    {
        using var assets = new ExperienceAssets(root, audio, ExperienceAssetContext.Battle);
        var sound = new SessionExperienceNoticeSound(this, assets.LevelUp);
        using var modal = new NativeExperienceModalEffects(state, host, new ExperienceAllocationRaster(assets.Etc, assets.Font, assets.Labels, X87Precision.Bits53), assets.Font, sound, bindings, host.IsHeld, DrawExperienceBackdrop, () => music.Update(awardMusicTrack), StopExclusiveScenarioSound, () => CurrentEffectsVolume, () => true, battleBackground: (_, target, nativeTime) =>
        {
            execution.AdvanceLogicalVisual(nativeTime, false);
            execution.CopyCachedFrame(target);
        });
        gate.RebaseSchedule();
        new CombatExperienceAward(modal).Run(state, signedAward);
        StopExclusiveScenarioSound();
        gate.RebaseSchedule();
    }
}
