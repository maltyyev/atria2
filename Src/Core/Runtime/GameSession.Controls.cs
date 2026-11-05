using Atria.Core.Contracts.Input;
using Atria.Core.Contracts.Host;
using Atria.Core.Contracts.Window;
using Atria.Core.Game;
using Atria.Core.Game.Combat;
using Atria.Core.Game.Dialogue;
using Atria.Core.Game.Menu;
using Atria.Core.Game.Navigation;
using Atria.Core.Game.Progression;
using Atria.Core.Game.State;
using Atria.Core.Game.Town;
using Atria.Core.Persistence.Controls;
using Atria.Core.Presentation.Combat;
using Atria.Core.Presentation.Controls;
using Atria.Core.Presentation.Dialogue;
using Atria.Core.Resources;
using Atria.Core.Resources.Localization;
using Atria.Core.Resources.Sprites;
using Atria.Core.Runtime.Input;
using Atria.Core.Runtime.Timing;

namespace Atria.Core.Runtime;

public sealed partial class GameSession
{
    private InputRouter controls = null!;
    private MoveCatalog? moveCatalog;
    private CombatExecutionHost? currentCombat;
    private CombatControlOverlay? combatControls;
    private bool blockingControls;
    private MoveCatalog Moves => moveCatalog ??= new(root, state.HeroCatalog);

    private void InitializeModernControls(ControlBindings original)
    {
        ReplaceInputRouter(new(initialControlProfile ?? ControlProfile.Defaults(original), bindings, hostServices.Gamepads));
        Console.WriteLine("Controls: semantic input, profiles=" + string.Join(",", controls.Profile.Players.Select(p => p.Preset)) + "; XInput enabled.");
    }

    /// <summary>Installs a router and redirects session/menu input while detaching the previous router.</summary>
    private void ReplaceInputRouter(InputRouter next)
    {
        var previous = controls;
        if (previous is not null)
            previous.InputSourceChanged -= OnInputSourceChanged;
        controls = next;
        host.Controls = next;
        menuNavigation = new(bindings, next.MenuInput);
        inputHintsDirty = true;
        next.InputSourceChanged += OnInputSourceChanged;
        try
        {
            previous?.Dispose();
        }
        catch
        {
            next.InputSourceChanged -= OnInputSourceChanged;
            controls = null!;
            host.Controls = null;
            try
            {
                next.Dispose();
            }
            catch
            {
                // Preserve the original disposal failure; this replacement is no longer session-owned.
            }
            throw;
        }
    }

    /// <summary>Detaches input notifications and disposes the router owned by this session.</summary>
    private void DisposeInputRouter()
    {
        var current = controls;
        controls = null!;
        host.Controls = null;
        if (current is null)
            return;

        current.InputSourceChanged -= OnInputSourceChanged;
        current.Dispose();
    }

    /// <summary>Marks the cached input hint for refresh after the active input source changes.</summary>
    private void OnInputSourceChanged(InputActivity _) => inputHintsDirty = true;

    private void PollSessionInput()
    {
        CheckLifetime();
        if (window is IMobileUiChannel mobileChannel) PublishMobileUiSnapshot(mobileChannel);
        controls?.Poll(window);
        PollMobileUi();

        if (controls?.FocusPauseRequested == true)
        {
            ((MonotonicModalClock)gate.Clock).Pause();
        }

        if (controls?.FocusPauseRequested == true && host.Focused && !blockingControls)
        {
            ShowPause(battleActive, currentCombat);
        }
        else if (controls is not null
            && !blockingControls
            && !battleActive
            && host.Focused
            && actions.Depth < 0
            && (dialogueActive
                || state.Mode is GameMode.World or GameMode.WorldEntry or GameMode.Dungeon or GameMode.Town or GameMode.Interior or GameMode.Inn)
            && (state.Mode is GameMode.World or GameMode.WorldEntry or GameMode.Dungeon
                ? controls.WorldInput.Press(0, WorldAction.Pause) || controls.WorldInput.Press(1, WorldAction.Pause)
                : controls.TownInput.Press(0, TownAction.Pause) || controls.TownInput.Press(1, TownAction.Pause)))
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
            }, MobileOverlayKind.Navigation);
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
            OpenScreen(UiScreen.MainMenu);
        }
    }

    private void ShowSettingsModal(ushort[] background)
    {
        var screen = CreateModernSettings();
        controls.Clear();
        ControlSettingsMenu? previousMobileSettings = SetMobileSettingsMenu(screen);
        try
        {
            RunControlModal(() => screen.Closed, () =>
            {
                screen.Update();
                background.CopyTo(host.FrameBuffer);
                screen.Draw(host.FrameBuffer);
            }, MobileOverlayKind.None);
        }
        finally
        {
            SetMobileSettingsMenu(previousMobileSettings);
        }
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
                    new(GameText.Get("ui.common.hero"), state.HeroCatalog[party].Name, Adjust: d =>
                    {
                        party = (party + d + state.HeroCatalog.Count) % state.HeroCatalog.Count;
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

                done = panel.Input(controls, rows) || controls.MenuInput.Press(0, MenuAction.Reference);
                background.CopyTo(host.FrameBuffer);
                panel.Draw(
                    host.FrameBuffer,
                    GameText.Format("ui.controls.movesFor", ("character", state.HeroCatalog[party].Name)),
                    rows,
                    GameText.Format("ui.controls.shortcutsHelpDynamic",
                        ("selector", controls.Hint(0, GameAction.Selector)),
                        ("quick1", controls.Hint(0, GameAction.Quick1)),
                        ("quick4", controls.Hint(0, GameAction.Quick4))),
                    GameText.Get("ui.controls.shortcutsExplanation")
                );
            }, MobileOverlayKind.Navigation);
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

    private void RunControlModal(Func<bool> closed, Action updateAndDraw, MobileOverlayKind? overlayOverride = null)
    {
        var customPresentation = host.PresentCustomFrame;
        using IDisposable? mobileInputScope = host.BeginMobileOverlayScope?.Invoke(overlayOverride);
        host.PresentCustomFrame = null;
        try
        {
            if (window is IMobileUiChannel initialMobileChannel) PublishMobileUiSnapshot(initialMobileChannel);
            while (!closed())
            {
                window.Pump();
                CheckLifetime();
                if (window is IMobileUiChannel mobileChannel) PublishMobileUiSnapshot(mobileChannel);
                controls.Poll(window);
                PollMobileUi();
                if (!host.Focused)
                {
                    Thread.Sleep(15);
                    continue;
                }

                updateAndDraw();
                if (window is IMobileUiChannel updatedMobileChannel) PublishMobileUiSnapshot(updatedMobileChannel);
                host.Present();
                Thread.Sleep(8);
            }
        }
        finally
        {
            host.PresentCustomFrame = customPresentation;
        }
    }
}
