using Atria.Platform;
using Atria.Resources;
using Atria.Render;

namespace Atria.Game.Controls;

/// <summary>Device polling is independent of simulation. Each physical edge has one consumer.</summary>
public sealed class InputRouter : IDisposable
{
    private readonly IGamepadDevice pads;
    private static readonly ushort[] buttonMasks=[0x1000,0x2000,0x4000,0x8000,0x100,0x200,0x20,0x10,0x40,0x80,1,8,2,4];
    private readonly ControlBindings legacy;
    private readonly bool[] held=new bool[448],previous=new bool[448],pending=new bool[448],blocked=new bool[448];
    private readonly long[] repeat=new long[448],nextProbe=new long[4],rumbleEnd=new long[4];
    private readonly PadSample[] samples=new PadSample[4];
    private bool wasFocused=true;
    private readonly int[] contextVersions=new int[2];
    public int ContextVersion(int player)=>contextVersions[player];
    private int wheel;
    public int TakeWheel(){int steps=wheel/120;wheel-=steps*120;return steps;}
    public ControlProfile Profile {get;set;}
    public bool FocusPauseRequested {get;set;}
    public bool LastWasPad {get;private set;}
    public bool InBattle {get;set;}
    public bool ExplorationMovement {get;set;}
    public int MouseX {get;private set;}=-1;
    public int MouseY {get;private set;}=-1;
    public string Notice {get;set;}="";
    public long Now {get;private set;}
    public InputRouter(ControlProfile profile,ControlBindings legacy,IGamepadDevice pads)
    {Profile=profile;this.legacy=legacy;this.pads=pads;}
    public bool Connected(int slot)=>slot>=0&&samples[slot].Connected;
    public void Poll(NativeWindow window)
    {
        var size=window.ClientSize;var v=Viewport.Fit(size.Width,size.Height);
        if(v.TryMapInput(window.MouseX,window.MouseY,out int x,out int y)){MouseX=x;MouseY=y;}else MouseX=MouseY=-1;
        Poll(window.ScanKeys,window.ScanPressed,[window.LeftButton,window.RightButton,window.MiddleButton],window.Focused,Environment.TickCount64,window.MousePressed);
        if(window.Focused)wheel+=window.MouseWheelDelta;
    }
    public void Poll(ReadOnlySpan<bool> keys,ReadOnlySpan<bool> pressed,ReadOnlySpan<bool> mouse,bool focused,long now,ReadOnlySpan<bool> mousePressed=default)
    {
        Now=now;Array.Copy(held,previous,held.Length);Array.Clear(held);
        if(wasFocused&&!focused){FocusPauseRequested=true;Clear();StopVibration();}wasFocused=focused;
        if(focused){for(int i=0;i<256;i++)held[i]=keys[i];for(int i=0;i<3;i++)held[256+i]=mouse[i];}
        for(int slot=0;slot<4;slot++)
        {
            if(now>=rumbleEnd[slot]&&rumbleEnd[slot]!=0){pads.Vibrate(slot,0,0);rumbleEnd[slot]=0;}
            if(!samples[slot].Connected&&now<nextProbe[slot])continue;
            var before=samples[slot];var s=pads.Read(slot);samples[slot]=s;nextProbe[slot]=now+1500;
            if(before.Connected&&!s.Connected&&Profile.Players.Any(p=>p.PadSlot==slot)){FocusPauseRequested=true;Clear();StopVibration();}
            if(!focused||!s.Connected)continue;
            int at=300+slot*32;
            for(int b=0;b<14;b++)held[at+b]=(s.Buttons&buttonMasks[b])!=0;
            held[at+14]=s.LeftTrigger>=Profile.TriggerThreshold;held[at+15]=s.RightTrigger>=Profile.TriggerThreshold;
            for(int d=0;d<4;d++)held[428+slot*4+d]=held[at+15]&&held[at+10+d];
            Axis(at+16,s.LX,false);Axis(at+18,s.LY,true);Axis(at+20,s.RX,false);Axis(at+22,s.RY,true);
        }
        for(int i=0;i<held.Length;i++)
        {
            if(blocked[i]){if(!held[i])blocked[i]=false;pending[i]=false;continue;}
            bool tap=focused&&(i<256&&pressed[i]||i>=256&&i<259&&mousePressed.Length==3&&mousePressed[i-256]);
            if(held[i]&&!previous[i]||tap){pending[i]=true;repeat[i]=now+350;LastWasPad=i>=300;}
            if(!held[i])repeat[i]=0;
        }
    }
    private void Axis(int at,int value,bool positiveFirst)
    {
        int negative=positiveFirst?at+1:at,positive=positiveFirst?at:at+1;
        held[negative]=value<-(Profile.DeadZone-(previous[negative]?1800:0));held[positive]=value>Profile.DeadZone-(previous[positive]?1800:0);
    }
    private int Token(int player,InputBinding b)
    {
        var p=Profile.Players[player];return b.Kind switch {InputKind.Key when p.Keyboard=>b.Code,InputKind.Mouse when p.Keyboard=>256+b.Code,InputKind.Pad when p.PadSlot>=0=>300+p.PadSlot*32+b.Code,_=>-1};
    }
    public bool Held(int player,GameAction action)=>Held(player,Profile.Players[player].Bindings[(int)action]);
    public bool Held(int player,InputBinding[] row)
    {foreach(var b in row){int t=Token(player,b);if(t>=0&&held[t]&&!blocked[t])return true;}return false;}
    public bool Press(int player,GameAction action,bool allowRepeat=false)=>Press(player,Profile.Players[player].Bindings[(int)action],allowRepeat);
    public bool PressQuick(int player,int slot,bool assigning=false)
    {
        foreach(var b in Profile.Players[player].Bindings[(int)GameAction.Quick1+slot])
        {
            int token=Token(player,b);
            if(b.Kind!=InputKind.Pad||b.Code is <10 or >13){if(Take(token))return true;continue;}
            if(token<0)continue;
            int chord=428+Profile.Players[player].PadSlot*4+b.Code-10;
            int rt=300+Profile.Players[player].PadSlot*32+15;
            if(assigning){Take(chord);if(Take(token))return true;continue;}
            Take(token);
            if(Take(chord)){Take(rt);return true;}
        }
        return false;
    }
    public static string QuickBindingHint(InputBinding binding)=>binding.Kind==InputKind.Pad&&binding.Code is >=10 and <=13?"RT+"+binding:binding.ToString();
    public bool Press(int player,InputBinding[] row,bool allowRepeat=false)
    {foreach(var b in row){int t=Token(player,b);if(Take(t,allowRepeat))return true;}return false;}
    public bool Take(int token,bool allowRepeat=false)
    {
        if(token<0||blocked[token])return false;
        if(pending[token]){pending[token]=false;return true;}
        if(allowRepeat&&held[token]&&repeat[token]!=0&&Now>=repeat[token]){repeat[token]=Now+90;return true;}return false;
    }
    public bool RawHeld(int token)=>held[token]&&!blocked[token];
    public bool PlayerMenu(int player,GameAction action,bool repeat=false)
    {
        if(Press(player,action,repeat))return true;
        int stick=action switch{GameAction.MenuLeft=>16,GameAction.MenuRight=>17,GameAction.MenuUp=>18,GameAction.MenuDown=>19,_=>-1};
        int slot=Profile.Players[player].PadSlot;return stick>=0&&slot>=0&&Take(300+slot*32+stick,repeat);
    }
    public bool Menu(GameAction action,bool repeat=false)
    {
        for(int p=0;p<2;p++)if(Press(p,action,repeat))return true;
        int safe=action switch{GameAction.MenuLeft=>203,GameAction.MenuRight=>205,GameAction.MenuUp=>200,GameAction.MenuDown=>208,GameAction.Confirm=>28,GameAction.Back=>1,_=>-1};
        if(Take(safe,repeat))return true;
        int stick=action switch{GameAction.MenuLeft=>16,GameAction.MenuRight=>17,GameAction.MenuUp=>18,GameAction.MenuDown=>19,_=>-1};
        if(stick>=0)for(int p=0;p<2;p++){int slot=Profile.Players[p].PadSlot;if(slot>=0&&Take(300+slot*32+stick,repeat))return true;}
        return false;
    }
    public bool LegacyMenu(byte scan,bool query)
    {
        for(int p=0;p<2;p++)for(int a=0;a<8;a++)if(scan==legacy[p,a])
        {
            if(ExplorationMovement&&a<4)return query?Press(p,(GameAction)a,true):Held(p,(GameAction)a);
            GameAction action=a switch{0=>GameAction.MenuLeft,1=>GameAction.MenuRight,2=>GameAction.MenuUp,3=>GameAction.MenuDown,4=>GameAction.Confirm,5=>GameAction.Back,7=>GameAction.Switch,_=>GameAction.Execute};
            if(query)return ControlProfile.IsMenu(action)?Menu(action,a<4):Press(p,action);
            return Held(p,action);
        }
        return query?Take(scan):RawHeld(scan);
    }
    public void Clear()
    {contextVersions[0]++;contextVersions[1]++;wheel=0;Array.Clear(pending);for(int i=0;i<held.Length;i++)blocked[i]=held[i];}
    public void ClearPlayer(int player)
    {
        contextVersions[player]++;
        void ClearRow(InputBinding[] row){foreach(var binding in row){int t=Token(player,binding);if(t>=0){pending[t]=false;blocked[t]=held[t];}}}
        foreach(var row in Profile.Players[player].Bindings)ClearRow(row);
        foreach(var row in Profile.Players[player].Direct.Values)ClearRow(row);
        int slot=Profile.Players[player].PadSlot;if(slot>=0)for(int d=0;d<4;d++){int t=428+slot*4+d;pending[t]=false;blocked[t]=held[t];}
    }
    public InputBinding Capture(int player,InputKind kind,int? assignedPad=null)
    {
        int pad=assignedPad??Profile.Players[player].PadSlot;if(kind==InputKind.Pad&&pad<0)return default;
        int start=kind==InputKind.Pad?300+pad*32:0,end=kind==InputKind.Pad?start+24:259;
        for(int i=start;i<end;i++)if(Take(i))return i>=300?new(InputKind.Pad,i-start):i>=256?new(InputKind.Mouse,i-256):new(InputKind.Key,i);
        return default;
    }
    public string Hint(int player,GameAction a)
    {var row=Profile.Players[player].Bindings[(int)a];var binding=LastWasPad?row.FirstOrDefault(b=>b.Kind==InputKind.Pad):row.FirstOrDefault(b=>b.Kind is InputKind.Key or InputKind.Mouse);return a is >=GameAction.Quick1 and <=GameAction.Quick4?QuickBindingHint(binding):binding.ToString();}
    public void Rumble(int player)
    {int slot=Profile.Players[player].PadSlot;if(Profile.Vibration&&slot>=0&&Connected(slot)){pads.Vibrate(slot,10000,6000);rumbleEnd[slot]=Now+80;}}
    public void StopVibration(){for(int s=0;s<4;s++)if(samples[s].Connected){pads.Vibrate(s,0,0);rumbleEnd[s]=0;}}
    public void Dispose()=>StopVibration();
}
