using System.Buffers.Binary;
using Atria.Game.State;
using Atria.Resources;
using Atria.Game.Controls;

namespace Atria.Game.Combat;

public interface ICombatControlEffects : ICombatFrameEffects
{
    bool GlobalEffectBusy { get; }
    void ShowRankNotice(int rank,uint milliseconds);
}

/// <summary>404c40 actual manual routing and40e1a0 type9 dispatch. Unrecovered AI types diagnose.</summary>
public sealed class CombatActorController : ICombatActorActions
{
    private readonly CombatSceneRuntime runtime;
    private readonly GameState state;
    private readonly CombatScanInput input;
    private readonly ICombatControlEffects effects;
    private readonly Func<int> nextRandom;
    private readonly byte[][] rows=[new byte[8],new byte[8]];
    private readonly byte[][] bank;
    private readonly Action<int> sound,rankNotice,itemNotice;
    private int leaderHeldSince;
    private uint now;
    public bool CommandSelectionActive { get; private set; }
    public bool AutomationEnabled { get; private set; }
    public int ControlRank { get; private set; }=3;
    public string[][] SelectedMoves {get;}=[new string[6],new string[6]];
    public bool[] SelectorOpen {get;}=new bool[2];
    private readonly CombatMoveSequence[] moveSequences=[new(),new()];
    private readonly int[] moveContexts=new int[2];
    private readonly int[] moveActors=[-1,-1];
    public void SetAutomation(bool enabled){if(enabled!=AutomationEnabled)ToggleAutomation();}
    private readonly CombatScanInput[] semanticWalk=new CombatScanInput[2];
    private static readonly byte[] semanticDirections=[0,1,2,3,4,5,6,7];
    private static readonly int[] branchDirections=[1,2,3,4,6,7,8,9];
    private static readonly string[] partyPrefixes=["0/","1/","2/","3/","4/","5/"];
    public bool ExecuteMove(int actorIndex,string id,uint milliseconds)
        =>CombatSemanticControl.Execute(id,runtime,runtime.Actors[actorIndex],state,milliseconds,effects.GlobalEffectBusy,sound,rankNotice,itemNotice);

    public CombatActorController(CombatSceneRuntime runtime,GameState state,ControlBindings bindings,
        CombatScanInput input,ICombatControlEffects effects,Func<int> nextNativeRandom)
    {
        this.runtime=runtime;this.state=state;this.input=input;this.effects=effects;nextRandom=nextNativeRandom;
        for(int p=0;p<2;p++) for(int a=0;a<8;a++) rows[p][a]=bindings[p,a];
        bank=runtime.Actors.Select(a=>a.Fields).ToArray();sound=effects.PlaySound;rankNotice=RankNotice;itemNotice=ItemNotice;
        for(int p=0;p<2;p++){int player=p;semanticWalk[p]=new(scan=>scan<4?input.Modern!.Held(player,(GameAction)scan):scan==4&&input.Modern!.Profile.Players[player].Classic&&input.Modern.Held(player,GameAction.Attack));}
    }
    public void Process(int actorIndex,uint milliseconds)
    {
        now=milliseconds;CombatActorImage image=runtime.Actors[actorIndex];byte[] actor=image.Fields;
        if(actor[0xa4]==0) Automatic(actorIndex,milliseconds);
        else if(input.Modern is {} modern) ProcessSemantic(actorIndex,image,modern,milliseconds);
        else
        {
            if((uint)image.ControlPlayer>=2) throw new InvalidOperationException("Controlled actor has no actual input row.");
            byte[] row=rows[image.ControlPlayer];
            if(I(actor,0x230)!=0)
            {
                if(!input.IsHeld(row[4])) { CommandSelectionActive=false;leaderHeldSince=unchecked((int)milliseconds); }
                else if(unchecked(leaderHeldSince+700)<unchecked((int)milliseconds))
                {
                    CommandSelectionActive=true;NavigateCommands(actor,image.Binding!.Info,row);
                }
            }
            if(input.Query(row[7])) CombatPartyControl.Cycle(runtime.Actors,runtime.Scene.PartyStartIndex,actorIndex);
            else if(I(actor,0xad)<2)
            {
                if(input.Query(row[4])) CombatManualControl.Attack(actor,image.Binding!.Info,image.Animation!,milliseconds);
                else if(input.Query(row[5]))
                {
                    if(!CombatJumpRuntime.Begin(actor,image.Binding!.Info,image.Animation!,milliseconds))
                        CombatManualControl.Walk(actor,image.Binding!.Info,image.Animation!,row,input,milliseconds);
                }
                else if(input.Query(row[6])) CombatManualControl.ExecuteCommand(runtime,image,state,milliseconds,effects.GlobalEffectBusy,sound,rankNotice,itemNotice);
                else CombatManualControl.Walk(actor,image.Binding!.Info,image.Animation!,row,input,milliseconds);
            }
            else CombatManualControl.ContinueAction(image,row,input,milliseconds);
        }
        if(input.Modern is {} modernInput ? modernInput.Press(0,GameAction.Automation)||modernInput.Press(1,GameAction.Automation) : input.Query(30)) ToggleAutomation();
    }
    private void ProcessSemantic(int actorIndex,CombatActorImage image,InputRouter modern,uint milliseconds)
    {
        int player=image.ControlPlayer;if((uint)player>=2)throw new InvalidOperationException("Missing semantic player.");
        byte[] a=image.Fields;var info=image.Binding!.Info;var p=modern.Profile.Players[player];int party=actorIndex-runtime.Scene.PartyStartIndex;
        if(moveContexts[player]!=modern.ContextVersion(player)||moveActors[player]!=actorIndex)moveSequences[player].Cancel();
        if(SelectorOpen[player]){moveSequences[player].Cancel();return;}
        if(modern.Press(player,GameAction.Switch)){moveSequences[player].Cancel();CombatPartyControl.Cycle(runtime.Actors,runtime.Scene.PartyStartIndex,actorIndex);modern.ClearPlayer(player);return;}
        string? request=null;
        for(int q=0;q<4;q++)if(modern.PressQuick(player,q)){request=p.Quick[party][q];break;}
        if(request is null)foreach(var pair in p.Direct)if(pair.Key.StartsWith(partyPrefixes[party],StringComparison.Ordinal)&&modern.Press(player,pair.Value)){request=pair.Key[(pair.Key.IndexOf('/')+1)..];break;}
        if(request is null)for(int i=0;i<8;i++)if(modern.Press(player,GameAction.BackDown+i)){request="v:"+branchDirections[i];break;}
        if(request is not null)
        {RequestSemanticMove(actorIndex,player,request,modern,milliseconds);return;}
        if(modern.Press(player,GameAction.Execute))
        {
            string selected=SelectedMoves[player][party]??"";if(selected.Length==0)SelectedMoves[player][party]=selected=MoveCatalog.FromInfo(info).FirstOrDefault(m=>m.IsMagic)?.Id??"s:3";
            RequestSemanticMove(actorIndex,player,selected,modern,milliseconds);return;
        }
        bool manual=modern.Held(player,GameAction.Attack)||modern.Held(player,GameAction.Jump)||modern.Held(player,GameAction.Branch)||
            modern.Held(player,GameAction.Left)||modern.Held(player,GameAction.Right)||modern.Held(player,GameAction.Up)||modern.Held(player,GameAction.Down);
        if(manual)moveSequences[player].Cancel();
        if(moveSequences[player].Advance(image,milliseconds))return;
        if(I(a,0xad)<2)
        {
            if(modern.Press(player,GameAction.Attack))CombatManualControl.Attack(a,info,image.Animation!,milliseconds);
            else if(modern.Press(player,GameAction.Jump))
            {if(!CombatJumpRuntime.Begin(a,info,image.Animation!,milliseconds))CombatManualControl.Walk(a,info,image.Animation!,semanticDirections,semanticWalk[player],milliseconds,p.Classic?null:modern.Held(player,GameAction.Run));}
            else CombatManualControl.Walk(a,info,image.Animation!,semanticDirections,semanticWalk[player],milliseconds,p.Classic?null:modern.Held(player,GameAction.Run));
        }
        else
        {
            bool branch=p.Classic?modern.Held(player,GameAction.Jump):modern.Held(player,GameAction.Branch);
            if(branch){int d=CombatSemanticControl.Direction(modern,player,a[0xfa]);if(d!=0)CombatSemanticControl.Variation(image,d,0,milliseconds);}
            else if(modern.Held(player,GameAction.Attack))CombatSemanticControl.AirAttack(image,milliseconds);
            // Do not retain an early attack/jump press until an unrelated future legal state.
            modern.Press(player,GameAction.Attack);modern.Press(player,GameAction.Jump);
        }
    }
    private void RequestSemanticMove(int actorIndex,int player,string id,InputRouter input,uint now)
    {
        moveSequences[player].Cancel();
        bool result=ExecuteMove(actorIndex,id,now);
        if(!result&&id.StartsWith("v:",StringComparison.Ordinal))result=moveSequences[player].Start(runtime.Actors[actorIndex],id,now);
        moveContexts[player]=input.ContextVersion(player);moveActors[player]=actorIndex;
        input.Notice=result?"":"Приём сейчас недоступен: состояние героя, ранг или MP";
        if(result)input.Rumble(player);
    }
    private void Automatic(int index,uint milliseconds)
    {
        CombatActorImage image=runtime.Actors[index];byte[] actor=image.Fields;int flags=I(actor,0xad);
        if(flags is not (0 or 1)) return;
        int type=image.Binding!.Info.Type;
        if(flags==0)
        {
            int target=CombatTargetSelection.Select(bank,runtime.TargetIndices,index,0,bank.Length,milliseconds,nextRandom);
            runtime.TargetIndices[index]=target;
            if(type==9) BanditAiDecision.Begin(actor,target<0?null:bank[target],image.Animation!,runtime.Scene.Header73,runtime.Scene.Header77,milliseconds,nextRandom);
            else if(type==8) EnemyType8AiDecision.Begin(actor,target<0?null:bank[target],image.Animation!,runtime.Scene.Header73,runtime.Scene.Header77,milliseconds,nextRandom);
            else if(CommonEnemyAiDecision.Supports(type)) CommonEnemyAiDecision.Begin(actor,type,target<0?null:bank[target],image.Animation!,runtime.Scene.Header73,runtime.Scene.Header77,milliseconds,nextRandom);
            else if((uint)type<=5 || type is >=46 and <=51)
            {
                if(!PartyAiDecision.Begin(actor,type>=46?0:type,target<0?null:bank[target],image.Animation!,runtime.Scene.Header73,runtime.Scene.Header77,milliseconds,nextRandom)) runtime.TargetIndices[index]=-1;
            }
            // Native default dispatch returns after target selection.
        }
        else if(type==9)
        {
            int target=runtime.TargetIndices[index];
            BanditAiDecision.Continue(actor,target<0?null:bank[target],image.Animation!,image.Binding.Info,milliseconds,nextRandom);
        }
        else if((uint)type<=5 || type is >=46 and <=51)
        {
            int target=runtime.TargetIndices[index];
            PartyAiDecision.Continue(actor,type>=46?1:type,target<0?null:bank[target],image.Animation!,image.Binding.Info,milliseconds,nextRandom);
        }
        else if(type==8)
        {
            int target=runtime.TargetIndices[index];
            EnemyType8AiDecision.Continue(actor,target<0?null:bank[target],image.Animation!,image.Binding.Info,milliseconds,nextRandom);
        }
        else if(CommonEnemyAiDecision.Supports(type))
        {
            int target=runtime.TargetIndices[index];
            CommonEnemyAiDecision.Continue(actor,type,target<0?null:bank[target],image.Animation!,image.Binding.Info,milliseconds,nextRandom,state.Globals[51]);
        }
        else BanditAiDecision.AdvanceMovement(actor,runtime.TargetIndices[index]<0?null:bank[runtime.TargetIndices[index]],image.Animation!,milliseconds);
    }
    private void NavigateCommands(byte[] actor,CombatInfoCharacter info,byte[] row)
    {
        int category=I(actor,0x2c);
        if((uint)category>=10) throw new InvalidDataException("Combat command category exceeds native count table.");
        if(input.Query(row[3]))
        {
            if(category<9 && info.Read(0xaac+category*4)!=0 && category<I(actor,0x17c))
            { if(category==0) W(actor,0x22c,I(actor,0x34));W(actor,0x34,0);W(actor,0x30,0);W(actor,0x2c,category+1); }
            sound(6);
        }
        else if(input.Query(row[2]))
        {
            if(category!=0) { W(actor,0x34,0);W(actor,0x30,0);W(actor,0x2c,category-1);if(category==1) W(actor,0x34,I(actor,0x22c)); }
            sound(6);
        }
        else if(input.Query(row[1]))
        {
            int selected=I(actor,0x34),count=info.Read(0xaa8+category*4);
            if(selected<4 && selected<count-1) W(actor,0x34,selected+1);
            else if(unchecked(I(actor,0x30)+4)<count) W(actor,0x30,unchecked(I(actor,0x30)+1));
            sound(6);
        }
        else if(input.Query(row[0]))
        {
            int selected=I(actor,0x34);
            if(selected<1) { if(I(actor,0x30)!=0) W(actor,0x30,unchecked(I(actor,0x30)-1)); }
            else W(actor,0x34,selected-1);
            sound(6);
        }
    }
    private void ToggleAutomation()
    {
        foreach(var move in moveSequences)move.Cancel();
        if(!AutomationEnabled)
        {
            for(int i=runtime.Scene.PartyStartIndex;i<bank.Length;i++) { bank[i][0xa5]=bank[i][0xa4];bank[i][0xa4]=0; }
        }
        else
        {
            for(int i=runtime.Scene.PartyStartIndex;i<bank.Length;i++)
            {
                byte[] actor=bank[i];byte saved=actor[0xa5];if(saved==0) continue;
                if(I(actor,0x170)>0) actor[0xa4]=saved;
                else
                {
                    for(int j=runtime.Scene.PartyStartIndex;j<bank.Length;j++)
                    {
                        if(I(bank[j],0x224)==0 || bank[j][0xa4]!=0 || I(bank[j],0x170)<=0) continue;
                        runtime.Actors[j].ControlPlayer=runtime.Actors[i].ControlPlayer;runtime.Actors[i].ControlPlayer=-1;bank[j][0xa4]=saved;break;
                    }
                }
                actor[0xa5]=0;
            }
        }
        AutomationEnabled=!AutomationEnabled;
    }
    private void RankNotice(int rank) { ControlRank=rank;effects.ShowRankNotice(rank,now); }
    private void ItemNotice(int id)=>effects.ShowConsumedItemNotice(id,now);
    private static int I(byte[] actor,int offset)=>BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(offset,4));
    private static void W(byte[] actor,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(actor.AsSpan(offset,4),value);
}
