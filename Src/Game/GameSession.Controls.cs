using Atria.Game.Combat;
using Atria.Game.Controls;
using Atria.Game.Dialogue;
using Atria.Platform;
using Atria.Platform.Win32;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    private InputRouter controls = null!;
    private MoveCatalog? moveCatalog;
    private CombatExecutionHost? currentCombat;
    private CombatControlOverlay? combatControls;
    private bool blockingControls;
    private MoveCatalog Moves => moveCatalog ??= new(root);

    private void InitializeModernControls(ControlBindings original)
    {
        controls = new(initialControlProfile ?? ControlProfile.Defaults(original), bindings, new XInputDevice());
        Console.WriteLine("Controls: semantic input, profiles=" + string.Join(",", controls.Profile.Players.Select(p => p.Preset)) + "; XInput enabled.");
    }

    private void PollSessionInput()
    {
        CheckLifetime();
        controls?.Poll(window);
        if (controls is not null)
        {
            controls.ExplorationMovement = !battleActive && !dialogueActive && actions.Depth < 0 && state.Mode is GameMode.World or GameMode.WorldEntry or GameMode.Dungeon;
        }

        if (controls?.FocusPauseRequested == true)
        {
            ((MonotonicModalClock)gate.Clock).Pause();
        }

        if (controls?.FocusPauseRequested == true && host.Focused && !blockingControls)
        {
            ShowPause(battleActive, currentCombat);
        }
        else if (controls is not null && !blockingControls && !battleActive && host.Focused && actions.Depth < 0 && (dialogueActive
|| state.Mode is GameMode.World or GameMode.WorldEntry or GameMode.Dungeon or GameMode.Town or GameMode.Interior or GameMode.Inn) && (controls.Press(0, GameAction.Pause) || controls.Press(1, GameAction.Pause)))
        {
            ShowPause(false);
        }
    }

    private void ShowPause(bool battle, CombatExecutionHost? combat = null)
    {
        blockingControls = true;
        var clock = (MonotonicModalClock)gate.Clock;
        clock.Pause();
        controls.StopVibration();
        controls.Clear();
        var panel = new ControlPanel(etc, smallFont);
        bool done = false, openMenu = false;
        var previousOverlay = host.Overlay;
        host.Overlay = null;
        ushort[] background = host.FrameBuffer.ToArray();
        try
        {
            RunControlModal(() => done, () =>
            {
                var rows = new List<ControlRow>
                {
                    new(GameText.Get("ui.pause.resume"), "", () => done = true),
                    new(GameText.Get("ui.settings.title"), "", () => ShowSettingsModal(background)),
                    new(GameText.Get("ui.controls.reference"), "", () => ShowMoveReference(0, alreadyPaused: true))
                };
                if (!battle && !dialogueActive && actions.Depth < 0
&& state.Mode is GameMode.World or GameMode.WorldEntry or GameMode.Dungeon or GameMode.Town or GameMode.Interior or GameMode.Inn)
                {
                    rows.Add(new(GameText.Get("ui.pause.gameMenu"), "", () =>
                    {
                        openMenu = true;
                        done = true;
                    }));
                }

                if (battle && combat is not null)
                {
                    rows.Add(new(
                        GameText.Get("ui.controls.automation"),
                        combat.Execution.Controller.AutomationEnabled ? GameText.Get("ui.common.on") : GameText.Get("ui.common.off"),
                        () => combat.Execution.Controller.SetAutomation(!combat.Execution.Controller.AutomationEnabled)
                    ));
                    rows.Add(new(
                        GameText.Get("ui.combat.detail"),
                        combat.Execution.VisualDetail.ToString(),
                        Adjust: d => combat.Execution.VisualDetail = Math.Clamp(combat.Execution.VisualDetail + d, 0, 3)
                    ));
                    rows.Add(new(
                        GameText.Get("ui.combat.legacyFramePeriod"),
                        GameText.Format("ui.units.milliseconds", ("amount", combat.Ui.Schedule.Period)),
                        Adjust: d => combat.Ui.Schedule.PollSpeedKeys(scan => scan == (d < 0 ? 0x12 : 0x13), gate.Clock.Milliseconds)
                    ));
                }

                if (panel.Input(controls, rows))
                {
                    done = true;
                }

                background.CopyTo(host.FrameBuffer);
                panel.Draw(
                    host.FrameBuffer,
                    controls.FocusPauseRequested ? GameText.Get("ui.pause.focusLost") : GameText.Get("ui.pause.title"),
                    rows,
                    GameText.Get("ui.pause.timersStopped")
                );
            });
        }
        finally
        {
            blockingControls = false;
            controls.FocusPauseRequested = false;
            controls.Clear();
            background.CopyTo(host.FrameBuffer);
            host.Overlay = previousOverlay;
            clock.Continue();
            gate.Resume();
        }

        if (openMenu)
        {
            if (state.Mode is GameMode.World or GameMode.WorldEntry)
            {
                modes.Push(GameMode.World);
            }

            modes.Push(GameMode.MainMenu);
            EnterMode(GameMode.MainMenu);
        }
    }

    private void ShowSettingsModal(ushort[] background)
    {
        var screen = CreateModernSettings();
        controls.Clear();
        RunControlModal(() => screen.Closed, () =>
        {
            screen.Update();
            background.CopyTo(host.FrameBuffer);
            screen.Draw(host.FrameBuffer);
        });
        controls.Clear();
    }

    private void ShowMoveReference(int party, bool alreadyPaused = false)
    {
        var clock = (MonotonicModalClock)gate.Clock;
        clock.Pause();
        controls.StopVibration();
        controls.Clear();
        var prior = host.Overlay;
        host.Overlay = null;
        ushort[] background = host.FrameBuffer.ToArray();
        var panel = new ControlPanel(etc, smallFont);
        int form = 1;
        bool done = false;
        try
        {
            RunControlModal(() => done, () =>
            {
                var rows = new List<ControlRow>
                {
                    new(GameText.Get("ui.common.hero"), MoveCatalog.Name(party), Adjust: d =>
                    {
                        party = (party + d + 6) % 6;
                    }),
                    new(GameText.Get("ui.controls.form"), form + " / 5", Adjust: d => form = Math.Clamp(form + d, 1, 5)),
                    new(GameText.Get("ui.controls.basicAttacks"), controls.Hint(0, GameAction.Attack)),
                    new(GameText.Get("ui.controls.jumpAttack"), controls.Hint(0, GameAction.Jump) + " / " + controls.Hint(0, GameAction.Attack)),
                    new(GameText.Get("ui.controls.runningAttack"), controls.Hint(0, GameAction.Run) + " / " + controls.Hint(0, GameAction.Attack))
                };
                foreach (var move in MoveCatalog.FromInfo(Moves.Info(party, form), form))
                {
                    rows.Add(new(move.Name, move.IsMagic ? GameText.Format("ui.units.mana", ("amount", move.Cost)) : move.IsStrategy ? GameText.Get("ui.combat.party") : ""));
                }

                done = panel.Input(controls, rows) || controls.Press(0, GameAction.Reference);
                background.CopyTo(host.FrameBuffer);
                panel.Draw(
                    host.FrameBuffer,
                    GameText.Format("ui.controls.movesFor", ("character", MoveCatalog.Name(party))),
                    rows,
                    GameText.Get("ui.controls.shortcutsHelp"),
                    GameText.Get("ui.controls.shortcutsExplanation")
                );
            });
        }
        finally
        {
            background.CopyTo(host.FrameBuffer);
            host.Overlay = prior;
            controls.Clear();
            if (!alreadyPaused)
            {
                controls.FocusPauseRequested = false;
                clock.Continue();
                gate.Resume();
            }
        }
    }

    private void RunControlModal(Func<bool> closed, Action updateAndDraw)
    {
        while (!closed())
        {
            window.Pump();
            CheckLifetime();
            controls.Poll(window);
            if (!host.Focused)
            {
                Thread.Sleep(15);
                continue;
            }

            updateAndDraw();
            host.Present();
            Thread.Sleep(8);
        }
    }
}
