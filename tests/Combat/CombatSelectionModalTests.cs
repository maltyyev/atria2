using System.Buffers.Binary;
using Atria.Game.Combat;
using Atria.Game.Dialogue;
using Atria.Game.State;
using Atria.Platform;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Combat;

internal static class CombatSelectionModalTests
{
    internal static void Run()
    {
        byte[] cfg = new byte[64]; int[] scans = [203,205,200,208,46,45,44,31,80,81,82,83,84,85,86,87];
        for(int i=0;i<16;i++) BinaryPrimitives.WriteInt32LittleEndian(cfg.AsSpan(i*4),scans[i]);
        var bindings = ControlBindings.Parse(cfg);
        var blank = new SpriteFrame(0,0,0,[],[],0,0,[]);
        var tiles = new SpriteFile(false,Enumerable.Repeat(blank,8).ToArray());
        var font = BitmapFont.DecodeEng(Enumerable.Repeat((byte)255,4096).ToArray(),true);
        ReadOnlyMemory<byte>[] labels = Enumerable.Repeat<ReadOnlyMemory<byte>>(new byte[]{(byte)'x'},54).ToArray();
        var raster = new CombatSelectionRaster(new(tiles,tiles,font,labels));
        foreach(int interval in new[]{8,33}) foreach(bool stall in new[]{false,true})
        {
            var state = new GameState();
            for(int actor=0;actor<2;actor++) { state.SetMemberStatus(actor,1); state.SetPartyInt(actor,260,1); }
            var host = new Host(interval,stall); var sounds = new List<string>(); int music=0,stops=0,roles=0;
            var modal = new CombatSelectionModal(state,host,raster,bindings,static(_,pixels)=>pixels.Fill(0xffff),
                ()=>music++,sounds.Add,()=>stops++,selection=>{ Check(selection.Completed,"Only actual confirmed selection binds roles."); roles++; });
            CombatPartySelection result = modal.Run(2);
            Check(result.Completed && result.Leaders.SequenceEqual(new[]{0,1}) && state.GetPartyInt(0,256)==1 && state.GetPartyInt(1,256)==1,"Hosted real selection commits actual leaders.");
            Check(host.Gate.LastFrameMilliseconds==168 && music==8 && roles==1 && stops==1,"Prebattle logical state/audio/role order is independent of GPU rate/stall.");
            Check(sounds.SequenceEqual(new[]{"move.wav","error.wav","move.wav","start.wav"}),"Exact configured-key sound sequence through duplicate-player error.");
            Check(host.FrameBuffer[133*640+126]==0x000f && host.FrameBuffer[153*640+126]==0x07ff && host.FrameBuffer[124*640+114]==0x7bef,"Native leader highlights and panel half-shade raster boundaries.");
        }
    }
    private sealed class Clock : IModalClock { public uint Milliseconds{get;set;} public void Yield()=>Milliseconds++; }
    private sealed class Host(int interval,bool stall) : IDialogueFrameHost
    {
        private readonly Clock clock=new(); private readonly ScheduledScanInput input=new(0,0,new ScanCodeEdges());
        private readonly ushort[] pixels=new ushort[640*480]; private int cursor,presents;
        private readonly TimestampedScanInput[] events=
        [new(30,82,ScanInputEventKind.Down),new(31,82,ScanInputEventKind.Up),new(70,45,ScanInputEventKind.Down),new(71,45,ScanInputEventKind.Up),
         new(110,83,ScanInputEventKind.Down),new(111,83,ScanInputEventKind.Up),new(150,45,ScanInputEventKind.Down),new(151,45,ScanInputEventKind.Up)];
        private ModalFrameGate? gate;
        public ModalFrameGate Gate=>gate??=new(clock,0);
        public bool Closed=>false; public bool Focused=>true; public Span<ushort> FrameBuffer=>pixels;
        public void Pump() { if(clock.Milliseconds>2000) throw new InvalidOperationException("Bounded prebattle fixture failed to consume actual input."); while(cursor<events.Length && events[cursor].Time<=clock.Milliseconds) input.Enqueue(events[cursor++]); }
        public void BeginLogicalFrame(uint time)=>input.BeginLogicalFrame(time);
        public bool QueryScan(byte scan)=>input.Query(scan);
        public bool QueryAction(DialogueInputAction action)=>throw new InvalidOperationException("Selection uses per-player scan bindings.");
        public void UpdateMusic(GameState state)=>throw new InvalidOperationException("Selection uses battle reward music callback.");
        public bool BeforeModalInput(GameState state)=>throw new InvalidOperationException("Selection must not reenter pending battle.");
        public void ComposeScene(GameState state)=>throw new InvalidOperationException("Selection requires actual battle backdrop.");
        public void PlaySound(int id)=>throw new InvalidOperationException("Selection uses exclusive WAV channel.");
        public void Present() { clock.Milliseconds+=(uint)interval; if(stall && ++presents==3) clock.Milliseconds+=400; }
    }
    private static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
}
