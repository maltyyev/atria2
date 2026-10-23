using Atria.Game.Dialogue;
using Atria.Game.State;
using Atria.Resources;
using Atria.Game.Controls;

namespace Atria.Game.Combat;

/// <summary>Hosted424100. Shares the owning window, input queue, logical gate, pixels and exclusive sound channel.</summary>
public sealed class CombatSelectionModal(GameState state, IDialogueFrameHost host, CombatSelectionRaster raster,
    ControlBindings bindings, DialogueSceneDraw battleBackdrop, Action updateAwardMusic,
    Action<string> playExclusiveSound, Action stopExclusiveSound, Action<CombatPartySelection> bindPlayerRoles,Action? controlMenus=null)
{
    private bool active;
    private bool pointerToggle,pointerFinish;
    private readonly Func<int, CombatSelectionInput, bool> query = (player, input) =>
        host is NativeDialogueFrameHost {Controls: {} controls} ? controls.PlayerMenu(player,input switch
        {CombatSelectionInput.Previous=>GameAction.MenuUp,CombatSelectionInput.Next=>GameAction.MenuDown,CombatSelectionInput.Toggle=>GameAction.Attack,_=>GameAction.Confirm},input is CombatSelectionInput.Previous or CombatSelectionInput.Next)
        :host.QueryScan(input switch
        {CombatSelectionInput.Previous=>bindings[player,2],CombatSelectionInput.Next=>bindings[player,3],CombatSelectionInput.Toggle=>bindings[player,4],_=>bindings[0,5]});

    public CombatPartySelection Run(int configuredPlayers)
    {
        if (active) throw new InvalidOperationException("Native party-selection state cannot reenter itself.");
        var selection = new CombatPartySelection(state, configuredPlayers);
        active = true;
        uint lastDraw = 0, errorDeadline = 0;
        int forcedDraws = 2; bool dirty = true;
        try
        {
            while (true)
            {
                PumpFocused(ref errorDeadline); int ticks = 0;
                while (ticks < 8 && host.Gate.IsDue)
                {
                    ticks++; host.Gate.CommitScheduled();
                    uint now = host.Gate.LastFrameMilliseconds;
                    host.BeginLogicalFrame(now); updateAwardMusic();
                    if(host is NativeDialogueFrameHost {Controls: {} modern})
                    {
                        if(modern.Take(256))
                        {
                            if(modern.MouseX is >=126 and <392&&modern.MouseY is >=133 and <253)
                                pointerToggle=selection.PointerSelect(0,(modern.MouseY-133)/20);
                            else if(modern.MouseX is >=130 and <510&&modern.MouseY is >=370 and <400)pointerFinish=true;
                            dirty=true;
                        }
                    }
                    if (unchecked(lastDraw + 5000) < now || dirty || forcedDraws != 0)
                    {
                        forcedDraws = unchecked(forcedDraws - 1); lastDraw = now;
                        battleBackdrop(state, host.FrameBuffer); raster.Draw(selection, host.FrameBuffer);
                        if(host is NativeDialogueFrameHost {Controls: {} hints})raster.DrawControls(hints,host.FrameBuffer);
                        // Native clears the error AFTER drawing its last visible frame.
                        if (unchecked((int)errorDeadline) < unchecked((int)now)) selection.ClearDisplayedError();
                    }
                    uint previousInput = selection.InputRevision, previousFinish = selection.FinishRevision;
                    selection.ApplyInput((player,action)=>player==0&&action==CombatSelectionInput.Toggle&&pointerToggle||player==0&&action==CombatSelectionInput.Finish&&pointerFinish||query(player,action), playExclusiveSound);
                    pointerToggle=pointerFinish=false;
                    dirty = previousInput != selection.InputRevision;
                    if (previousFinish != selection.FinishRevision && selection.ErrorCode != 0)
                        errorDeadline = unchecked(now + 2000);
                    if (selection.Completed)
                    {
                        selection.Commit(state);
                        //408570's control-rank and input-row bindings require actual owning battle records.
                        bindPlayerRoles(selection);
                        stopExclusiveSound();
                        return selection;
                    }
                    PumpFocused(ref errorDeadline);
                }
                if (host.Gate.IsDue) continue;
                host.Present(); host.Gate.Clock.Yield();
            }
        }
        catch { stopExclusiveSound(); throw; }
        finally { active = false; }
    }

    private void PumpFocused(ref uint errorDeadline)
    {
        host.Pump(); bool paused = !host.Focused; uint before = host.Gate.Clock.Milliseconds;
        if(host.Focused)controlMenus?.Invoke();
        while (!host.Closed && !host.Focused) { host.Gate.Clock.Yield(); host.Pump(); }
        if (host.Closed) throw new OperationCanceledException("Game window closed during party selection.");
        if (paused)
        {
            errorDeadline = unchecked(errorDeadline + host.Gate.Clock.Milliseconds - before);
            host.Gate.Resume();
        }
    }
}
