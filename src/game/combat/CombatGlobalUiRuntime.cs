using System.Buffers.Binary;

namespace Atria.Game.Combat;

/// <summary>407fe0 scheduled global UI and4045f0/404640 shared sliding notice.</summary>
public sealed class CombatGlobalUiRuntime(CombatRenderSchedule schedule,IReadOnlyList<ReadOnlyMemory<byte>> labels)
{
    public CombatRenderSchedule Schedule { get; }=schedule;
    public int NoticePhase { get; private set; }
    public int NoticeX { get; private set; }
    public int NoticeY { get; private set; }
    public uint NoticeDeadline { get; private set; }
    public int NoticeInterval { get; private set; }
    public ReadOnlyMemory<byte> NoticeText { get; private set; }
    public uint SampledNow { get; private set; }
    public int VisualDetail { get; private set; }
    public bool SettingsNoticeVisible=>unchecked((int)SampledNow)<unchecked((int)Schedule.SettingsNoticeDeadline);

    public bool ShowRankNotice(int rank)=>Show(rank,5,0);
    public bool ShowConsumedItemNotice(int index)=>Show(index,6,6);
    private bool Show(int index,int count,int offset)
    {
        if(index<0) throw new ArgumentOutOfRangeException(nameof(index),"Negative notice would address outside the native label bank.");
        if(index>=count) return false; //Actual4045f0/404640 upper bound leaves an existing notice unchanged.
        if(labels.Count<=index+offset) throw new InvalidDataException("Battle notice label bank is incomplete.");
        ReadOnlyMemory<byte> text=labels[index+offset];int nul=text.Span.IndexOf((byte)0);
        NoticeText=nul<0?text:text[..nul];NoticeDeadline=0;NoticeX=410;NoticeY=70;NoticeInterval=30;NoticePhase=1;
        return true;
    }
    /// <summary>Call AFTER40a820 and HUD state/draw. New detail applies to the following world raster.</summary>
    public int Advance(Func<byte,bool> query,uint sampledNow,int visualDetail,Span<byte> currentActor)
    {
        if((uint)visualDetail>3 || currentActor.Length!=CombatSceneDefinition.RuntimeRecordSize)
            throw new ArgumentException("Global combat UI requires actual detail and current actor image.");
        SampledNow=sampledNow;VisualDetail=visualDetail;
        Schedule.PollSpeedKeys(query,sampledNow); //E has lazy priority over R.
        if(query(0x10))
        {
            if(VisualDetail>0) VisualDetail--;
            Schedule.RefreshSettingsNotice(sampledNow);
        }
        else if(query(0x11))
        {
            if(VisualDetail<3) VisualDetail++;
            Schedule.RefreshSettingsNotice(sampledNow);
        }
        AdvanceNotice();
        RegenerateMagic(currentActor,sampledNow);
        return VisualDetail;
    }
    private void AdvanceNotice()
    {
        int now=unchecked((int)SampledNow);
        if(NoticePhase==2)
        {
            if(unchecked((int)NoticeDeadline)<now) { NoticePhase=3;NoticeInterval=30; }
            return; //Native transition to exiting does not move in this call.
        }
        if(NoticePhase is not (1 or 3) || unchecked((int)NoticeDeadline)>=now) return;
        NoticeDeadline=unchecked(SampledNow+(uint)NoticeInterval);NoticeX=unchecked(NoticeX-10);
        if(NoticePhase==3)
        {
            if(NoticeX<10) NoticePhase=0;
        }
        else if(NoticeX<=unchecked((80-NoticeText.Length)*4))
        { NoticeDeadline=unchecked(SampledNow+3000);NoticePhase=2; }
    }
    private static void RegenerateMagic(Span<byte> actor,uint now)
    {
        bool equipped=false;
        for(int i=0;i<6;i++) if(Read(actor,0x208+i*4)==53) { equipped=true;break; }
        if(!equipped || Read(actor,0x375)>=unchecked((int)now)) return;
        int current=Read(actor,0x178);
        if(current<Read(actor,0x174)) Write(actor,0x178,unchecked(current+1));
        Write(actor,0x375,unchecked((int)now+1000)); //Refresh even when already full.
    }
    private static int Read(ReadOnlySpan<byte> a,int at)=>BinaryPrimitives.ReadInt32LittleEndian(a.Slice(at,4));
    private static void Write(Span<byte> a,int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(a.Slice(at,4),value);
}
