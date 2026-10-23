using System.Buffers.Binary;
using Atria.Game.Dialogue;
using Atria.Game.Progression;
using Atria.Game.State;
using Atria.Platform;
using Atria.Render.Codecs;
using Atria.Resources;

namespace Atria.Tests.Progression;

internal static class ExperienceDriverTests
{
    internal static void Run(ItemCatalog items)
    {
        byte[] cfg = new byte[64];
        int[] codes = [203, 205, 200, 208, 46, 45, 44, 31];
        for (int i = 0; i < 16; i++) BinaryPrimitives.WriteInt32LittleEndian(cfg.AsSpan(i * 4), codes[i % 8]);
        ControlBindings bindings = ControlBindings.Parse(cfg);
        int[] intervals = [33, 16, 8, 7, 4];
        foreach (int interval in intervals)
            foreach (bool stall in new[] { false, true })
            foreach (bool useBattleBackdrop in new[] { false, true })
            {
                GameState state = new() { PendingExperience = 200, ExperiencePending = 1 };
                for (int actor = 0; actor < 2; actor++)
                {
                    state.SetMemberStatus(actor, 1); state.SetPartyInt(actor, 200, 1);
                    state.SetPartyInt(actor, 192, 950); state.SetPartyInt(actor, 196, 1000);
                    for (int offset = 100; offset <= 120; offset += 4) state.SetPartyInt(actor, offset, 10);
                    for (int offset = 228; offset <= 248; offset += 4) state.SetPartyInt(actor, offset, -1);
                }
                var blank = new SpriteFrame(0, 0, 0, [], [], 0, 0, []);
                var tiles = new SpriteFile(false, Enumerable.Repeat(blank, 8).ToArray());
                var font = BitmapFont.DecodeEng(new byte[4096], true);
                ReadOnlyMemory<byte>[] labels = Enumerable.Repeat<ReadOnlyMemory<byte>>(new byte[] { (byte)'x' }, 54).ToArray();
                var raster = new ExperienceAllocationRaster(tiles, font, labels, X87Precision.Bits53);
                var host = new FixtureHost(interval, stall);
                var sound = new FixtureSound(); int musicUpdates = 0, exclusiveStops = 0;
                uint noticeStart=0;int staticDraws=0;
                var backdrops=new List<(uint Scheduled,uint Native,int Notices)>();
                sound.OnPlay=()=>noticeStart=host.Gate.LastFrameMilliseconds;
                using var driver = new NativeExperienceModalEffects(state, host, raster, font, sound, bindings, host.Held,
                    (_, pixels) => { staticDraws++;pixels.Clear(); }, () => musicUpdates++, () => exclusiveStops++, () => -10000, () => true,
                    battleBackground:useBattleBackdrop?(_,pixels,nativeTime)=>
                    { backdrops.Add((host.Gate.LastFrameMilliseconds,nativeTime,sound.Plays));pixels.Clear(); }:null);
                new PendingExperienceRuntime(items, driver).ProcessPending(state);
                Check(state.GetPartyInt(0, 192) == 1140 && state.GetPartyInt(1, 192) == 960, "Held transfers use scheduled input timestamps at every present rate/stall.");
                Check(state.GetPartyInt(0, 200) == 2 && state.GetPartyInt(1, 200) == 1 && state.ExperiencePending == 0, "Only the actual promoted actor advances.");
                Check(host.Gate.LastFrameMilliseconds == 420 && musicUpdates == 13 && sound.Plays == 1 && exclusiveStops == 1,
                    "Logical notice completion/music/audio order is independent of GPU timing.");
                if(useBattleBackdrop)
                {
                    Check(staticDraws==0 && backdrops.Any(b=>b.Notices==0) && backdrops.Any(b=>b.Notices==1),
                        "Battle backdrop explicitly replaces the exploration static copy at allocation and notice draws.");
                    Check(backdrops.Where(b=>b.Notices==0).All(b=>b.Native==unchecked(b.Scheduled+5000)),
                        "Allocation backdrop sees native45ae24 redraw deadline, not sampled current time.");
                    Check(backdrops.Where(b=>b.Notices==1).All(b=>b.Native==unchecked(noticeStart+4000)),
                        "Every level-notice backdrop sees its frozen native45ae24 deadline despite later logical ticks.");
                }
                else Check(staticDraws>0 && backdrops.Count==0,"Existing exploration compositor remains the default path.");
            }
    }
    private sealed class FixtureClock : IModalClock
    {
        public uint Milliseconds { get; set; }
        public void Yield() => Milliseconds++;
    }
    private sealed class FixtureSound : IExperienceNoticeSound
    {
        public int Plays { get; private set; }
        public Action? OnPlay { get; set; }
        public void Play(int attenuation) { Check(attenuation == -10000, "Fixture stays muted."); Plays++;OnPlay?.Invoke(); }
        public void Stop() { }
    }
    private sealed class FixtureHost : IDialogueFrameHost
    {
        private readonly FixtureClock clock = new();
        private readonly ScheduledScanInput input = new(0, 0, new ScanCodeEdges());
        private readonly ushort[] pixels = new ushort[640 * 480];
        private readonly int presentInterval;
        private readonly bool stall;
        private int presents, cursor;
        private readonly TimestampedScanInput[] events =
        [
            new(30,46,ScanInputEventKind.Down),new(31,46,ScanInputEventKind.Up),
            new(70,46,ScanInputEventKind.Down),new(71,46,ScanInputEventKind.Up),
            new(110,203,ScanInputEventKind.Down),new(175,203,ScanInputEventKind.Up),
            new(190,205,ScanInputEventKind.Down),new(215,205,ScanInputEventKind.Up),
            new(250,45,ScanInputEventKind.Down),new(320,45,ScanInputEventKind.Up),
            new(400,46,ScanInputEventKind.Down),new(450,46,ScanInputEventKind.Up)
        ];
        public FixtureHost(int interval, bool stall) { presentInterval = interval; this.stall = stall; Gate = new(clock, 0); }
        public bool Closed => false;
        public bool Focused => true;
        public Span<ushort> FrameBuffer => pixels;
        public ModalFrameGate Gate { get; }
        public bool Held(byte scan) => input.IsHeld(scan);
        public void Pump()
        {
            if (clock.Milliseconds > 5000) throw new InvalidOperationException("Bounded XP fixture did not finish from its supplied input.");
            while (cursor < events.Length && events[cursor].Time <= clock.Milliseconds) input.Enqueue(events[cursor++]);
        }
        public void BeginLogicalFrame(uint scheduledMilliseconds) => input.BeginLogicalFrame(scheduledMilliseconds);
        public bool QueryScan(byte scan) => input.Query(scan);
        public bool QueryAction(DialogueInputAction action) => throw new InvalidOperationException("XP must use its own configured input map.");
        public void UpdateMusic(GameState state) => throw new InvalidOperationException("XP must use the separate battle-track music callback.");
        public bool BeforeModalInput(GameState state) => throw new InvalidOperationException("XP must not reenter general dialogue input.");
        public void ComposeScene(GameState state) => throw new InvalidOperationException("XP requires its static backdrop compositor.");
        public void PlaySound(int id) => throw new InvalidOperationException("Levelup comes from WAV.000, not exploration UI sounds.");
        public void Present() { clock.Milliseconds += (uint)presentInterval; if (stall && ++presents == 3) clock.Milliseconds += 400; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
