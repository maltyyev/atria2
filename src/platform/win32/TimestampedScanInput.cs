namespace Atria.Platform;

public enum ScanInputEventKind { Down, Up, Reset }
// Time is uint32 Win32 MSG.time/GetTickCount, including wrap. Never a virtual-key identity.
public readonly record struct TimestampedScanInput(uint Time,byte Scan,ScanInputEventKind Kind);

// Kept separate from HWND dispatch so identical native event sequences can be replayed in tests.
public sealed class ScanInputCapture
{
    private readonly Queue<TimestampedScanInput> events=new();
    private uint lastTime;
    private bool hasTime;
    public bool[] Keys { get; }=new bool[256];
    public bool[] Pressed { get; }=new bool[256];
    public bool TryDequeue(out TimestampedScanInput input)=>events.TryDequeue(out input);
    public void Down(uint time,byte scan,bool previousMessageState)
    {
        if(!Keys[scan] && !previousMessageState)
        { Pressed[scan]=true; Record(time,scan,ScanInputEventKind.Down); }
        Keys[scan]=true;
    }
    public void Up(uint time,byte scan)
    {
        if(Keys[scan]) Record(time,scan,ScanInputEventKind.Up);
        Keys[scan]=false;
    }
    public void Reset(uint time)
    { Array.Clear(Keys); Array.Clear(Pressed); Record(time,0,ScanInputEventKind.Reset); }
    private void Record(uint time,byte scan,ScanInputEventKind kind)
    {
        // Sent/nested focus messages can arrive after an older queued key. Preserve delivery order.
        if(hasTime && unchecked((int)(time-lastTime))<0) time=lastTime;
        events.Enqueue(new(time,scan,kind)); lastTime=time; hasTime=true;
    }
}

public static class PhysicalScanCode
{
    // A nonzero physical scan and E0 bit are authoritative. Fallback applies only to scanless injection.
    public static byte Decode(long keyData,uint fallbackMappedScan=0)
    {
        int scan=(int)(keyData>>16)&0x7f;
        bool extended=(keyData&0x1000000)!=0;
        if(scan==0)
        { scan=(int)fallbackMappedScan&0x7f; extended=(fallbackMappedScan&0xff00)==0xe000; }
        return (byte)(extended?scan|0x80:scan);
    }
}
