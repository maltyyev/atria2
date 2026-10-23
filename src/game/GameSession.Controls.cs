using Atria.Game.Controls;
using Atria.Game.Combat;
using Atria.Game.Dialogue;
using Atria.Platform;
using Atria.Resources;

namespace Atria.Game;

public sealed partial class GameSession
{
    private InputRouter controls=null!;
    private MoveCatalog? moveCatalog;
    private CombatExecutionHost? currentCombat;
    private CombatControlOverlay? combatControls;
    private bool blockingControls;
    private MoveCatalog Moves=>moveCatalog??=new(root);
    private string ControlsPath=>Path.Combine(sessionOutputDirectory,"config","controls.json");
    private void InitializeModernControls(ControlBindings original)
    {
        var defaults=ControlProfile.Defaults(original);
        for(int p=0;p<2;p++)
        {
            bool changed=false;for(int a=0;a<8;a++)changed|=bindings[p,a]!=original[p,a];
            if(changed){for(int a=0;a<8;a++)defaults.Players[p].Bindings[a][0]=InputBinding.Key(bindings[p,a]);defaults.Players[p].Preset="Custom";defaults.Players[p].Classic=true;}
        }
        controls=new(ControlProfileStore.Read(ControlsPath,defaults),bindings,new XInputDevice());
        Console.WriteLine("Controls: semantic input, profiles="+string.Join(",",controls.Profile.Players.Select(p=>p.Preset))+"; XInput enabled.");
    }
    private void PollSessionInput()
    {
        CheckLifetime();controls?.Poll(window);
        if(controls is not null)controls.ExplorationMovement=!battleActive&&!dialogueActive&&actions.Depth<0&&state.Mode is 300 or 310 or 330;
        if(controls?.FocusPauseRequested==true)((MonotonicModalClock)gate.Clock).Pause();
        if(controls?.FocusPauseRequested==true&&host.Focused&&!blockingControls)ShowPause(battleActive,currentCombat);
        else if(controls is not null&&!blockingControls&&!battleActive&&host.Focused&&actions.Depth<0&&
            (dialogueActive||state.Mode is 300 or 310 or 330 or 400 or 410 or 420)&&
            (controls.Press(0,GameAction.Pause)||controls.Press(1,GameAction.Pause)))ShowPause(false);
    }
    private void ShowPause(bool battle,CombatExecutionHost? combat=null)
    {
        blockingControls=true;
        var clock=(MonotonicModalClock)gate.Clock;clock.Pause();controls.StopVibration();controls.Clear();
        var panel=new ControlPanel(etc,smallFont);bool done=false,openMenu=false;var previousOverlay=host.Overlay;host.Overlay=null;
        ushort[] background=host.FrameBuffer.ToArray();
        try
        {
            while(!done)
            {
                window.Pump();CheckLifetime();controls.Poll(window);if(!host.Focused){Thread.Sleep(15);continue;}
                var rows=new List<ControlRow>{new("Продолжить","",()=>done=true),new("Настройки","",()=>ShowSettingsModal(background)),new("Справочник приёмов","",()=>ShowMoveReference(0,alreadyPaused:true))};
                if(!battle&&!dialogueActive&&actions.Depth<0&&state.Mode is 300 or 310 or 330 or 400 or 410 or 420)
                    rows.Add(new("Игровое меню / сохранение","",()=>{openMenu=true;done=true;}));
                if(battle&&combat is not null)
                {
                    rows.Add(new("Автобой",combat.Execution.Controller.AutomationEnabled?"Вкл":"Выкл",()=>combat.Execution.Controller.SetAutomation(!combat.Execution.Controller.AutomationEnabled)));
                    rows.Add(new("Детализация",combat.Execution.VisualDetail.ToString(),Adjust:d=>combat.Execution.VisualDetail=Math.Clamp(combat.Execution.VisualDetail+d,0,3)));
                    rows.Add(new("Период старой отрисовки",combat.Ui.Schedule.Period+" мс",Adjust:d=>combat.Ui.Schedule.PollSpeedKeys(scan=>scan==(d<0?0x12:0x13),gate.Clock.Milliseconds)));
                }
                if(panel.Input(controls,rows))done=true;
                background.CopyTo(host.FrameBuffer);panel.Draw(host.FrameBuffer,controls.FocusPauseRequested?"Пауза: фокус или контроллер потерян":"Пауза",rows,"Состояние боя и игровые таймеры остановлены");host.Present();Thread.Sleep(8);
            }
        }
        finally{blockingControls=false;controls.FocusPauseRequested=false;controls.Clear();background.CopyTo(host.FrameBuffer);host.Overlay=previousOverlay;clock.Continue();gate.Resume();}
        if(openMenu){if(state.Mode is 300 or 310)modes.Push(300);modes.Push(200);EnterMode(200);}
    }
    private void ShowSettingsModal(ushort[] background)
    {
        var screen=CreateModernSettings();controls.Clear();
        while(!screen.Closed){window.Pump();CheckLifetime();controls.Poll(window);if(!host.Focused){Thread.Sleep(15);continue;}screen.Update();background.CopyTo(host.FrameBuffer);screen.Draw(host.FrameBuffer);host.Present();Thread.Sleep(8);}
        controls.Clear();
    }
    private void ShowMoveReference(int party,bool alreadyPaused=false)
    {
        var clock=(MonotonicModalClock)gate.Clock;clock.Pause();controls.StopVibration();controls.Clear();
        var prior=host.Overlay;host.Overlay=null;ushort[] background=host.FrameBuffer.ToArray();
        var panel=new ControlPanel(etc,smallFont);int form=1;bool done=false;
        try
        {
            while(!done)
            {
                window.Pump();CheckLifetime();controls.Poll(window);if(!host.Focused){Thread.Sleep(15);continue;}
                var rows=new List<ControlRow>{new("Герой",MoveCatalog.Names[party],Adjust:d=>{party=(party+d+6)%6;}),new("Развитие: набор",form+" / 5",Adjust:d=>form=Math.Clamp(form+d,1,5)),
                    new("Обычные удары",controls.Hint(0,GameAction.Attack)),new("Прыжок / удар в воздухе",controls.Hint(0,GameAction.Jump)+" / "+controls.Hint(0,GameAction.Attack)),new("Бег / удар с разбега",controls.Hint(0,GameAction.Run)+" / "+controls.Hint(0,GameAction.Attack))};
                foreach(var move in MoveCatalog.FromInfo(Moves.Info(party,form),form))rows.Add(new(move.Name,move.IsMagic?move.Cost+" MP":move.IsStrategy?"Отряд":""));
                done=panel.Input(controls,rows)||controls.Press(0,GameAction.Reference);
                background.CopyTo(host.FrameBuffer);panel.Draw(host.FrameBuffer,"Приёмы: "+MoveCatalog.Names[party],rows,"Назначение: Q / LB. Запуск: 1-4 / RT + крестовина","Быстрые слоты выполняют нужные удары из покоя");host.Present();Thread.Sleep(8);
            }
        }
        finally{background.CopyTo(host.FrameBuffer);host.Overlay=prior;controls.Clear();if(!alreadyPaused){controls.FocusPauseRequested=false;clock.Continue();gate.Resume();}}
    }
}
