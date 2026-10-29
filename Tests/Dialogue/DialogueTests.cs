using System.Buffers.Binary;
using Atria.Game.Dialogue;
using Atria.Game.State;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Script;

namespace Atria.Tests.Dialogue;

public static class DialogueTests
{
    public static string Run(string? resourceRoot = null)
    {
        Atria.Tests.Dialogue.PhysicalInputTests.Run();
        byte[] index = new byte[60];
        BinaryPrimitives.WriteInt32LittleEndian(index, 1);
        index[4] = 0xb7;
        index[5] = 0xce; // High-bit raw key: never a display-transcoded string.
        byte[] data = [0xc0, 13, 10, 13, 10, 0xff, 10, 0xfe, 13, 10];
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(56), data.Length);
        var catalog = DialogueCatalog.Parse(index, data);
        var call = new ScriptCall("FIXTURE", "question_dialog2", 19, [new(-1), new(-1), new(new byte[] { 0xb7, 0xce })]);
        DialogueRequest request = DialogueRequest.Create(new(ScriptEffectKind.QuestionKey, call), catalog);
        Check(request.Lines.Count == 4 && request.Lines[1].IsEmpty && request.Lines[3].IsEmpty, "CRLF and trailing empty lines.");
        Check(request.Lines[2].Span.SequenceEqual(new byte[] { 0xff, 10, 0xfe }), "Lone LF and high bytes preserved.");
        data[0] = 0;
        Check(request.Lines[0].Span[0] == 0xc0, "Owned line copy.");
        byte[] overlap = Enumerable.Repeat((byte)'a', 130).Concat(new byte[] { 13, 10, (byte)'b' }).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(56), overlap.Length);
        DialogueRequest overlapRequest = DialogueRequest.Create(new(ScriptEffectKind.QuestionKey, call), DialogueCatalog.Parse(index, overlap));
        Check(overlapRequest.Lines[0].Length == 130 && overlapRequest.Lines[0].Span[128] == (byte)'b'
&& overlapRequest.Lines[1].Span.SequenceEqual("ba"u8), "Native overlapping128-byte line slots.");
        var state = new GameState();
        state.Globals[10] = 71;
        var session = new DialogueSession(request, false);
        Check(session.ApplyInput(new(true, true, true), state).Count == 0 && !session.Completed, "Opening blocks input.");
        int[] offsets = [200, 100, 50, 25, 12, 6, 3, 0];
        for (int i = 0; i < offsets.Length; i++)
        {
            DialogueFrame frame = session.AdvancePresentation();
            Check(frame.OpeningOffset == offsets[i] && frame.TextVisible == (i >= 4), "Native opening cursor/order.");
        }

        session.ApplyInput(new(false, false, true), state);
        session.ApplyInput(new(false, false, true), state);
        Check(state.Globals[10] == 71, "No answer before confirm.");
        session.ApplyInput(new(true, false, false), state);
        Check(session.Completed && state.Globals[10] == 3 && session.FirstLine == 4, "One-based answer and terminal page increment.");
        var lines = Enumerable.Range(0, 9).Select(_ => (ReadOnlyMemory<byte>)new byte[] { 1 }).ToArray();
        session = new(new(-1, -1, false, lines), true, previousSelection: 5);
        for (int i = 0; i < 5; i++)
        {
            session.AdvancePresentation();
        }

        session.ApplyInput(new(true, false, false), state);
        Check(!session.Completed && session.FirstLine == 4, "Four-line pagination.");
        session.ApplyInput(new(true, false, false), state);
        session.ApplyInput(new(true, false, false), state);
        Check(session.Completed && state.Globals[10] == 6, "Persistent native question input flag.");
        byte[] glyphBytes = new byte[4096];
        glyphBytes[0xc0 * 16] = 0x80;
        BitmapFont font = BitmapFont.DecodeEng(glyphBytes, true);
        var blank = new SpriteFrame(0, 0, 0, [], [], 0, 0, []);
        var tiles = new SpriteFile(false, Enumerable.Repeat(blank, 20).ToArray());
        ushort[] pixels = Enumerable.Repeat((ushort)0xffff, 640 * 480).ToArray();
        var rasterRequest = new DialogueRequest(-1, -1, false, [new byte[] { 0xc0 }]);
        DialogueRaster.Draw(rasterRequest, new(80, 384, 0, true, 0, 0, false, 0, 0), tiles, null, font, pixels);
        Check(pixels[400 * 640 + 100] == 0x39e7, "Packed565 quarter shade fixture.");
        Check(pixels[401 * 640 + 104] == 0xffff && pixels[402 * 640 + 105] == 0, "Native text origin and shadow.");
        CheckModalBridge(tiles, font);
        if (resourceRoot is not null)
        {
            DialogueCatalog shipped = DialogueCatalog.Load(Path.Combine(resourceRoot, "roland.idx"), Path.Combine(resourceRoot, "roland.dat"));
            foreach (DialogueEntry entry in shipped.Entries)
            {
                var shippedCall = new ScriptCall("CATALOG", "dialogue_script2", 0, [new(-1), new(-1), new(entry.RawKey)]);
                DialogueRequest.Create(new(ScriptEffectKind.DialogueKey, shippedCall), shipped);
            }

            string facesPath = Path.Combine(resourceRoot, "face.SPR");
            using var faceSource = new FaceSpriteSource(facesPath);
            SpriteFile allFaces = SpriteFile.Decode(File.ReadAllBytes(facesPath));
            Check(faceSource.Count >= allFaces.Frames.Length, "Face index count.");
            foreach (int faceId in new[]
            {
                0,
                1,
                allFaces.Frames.Length - 1
            }
            )
            {
                SpriteFrame selected = faceSource.Load(faceId), full = allFaces.Frames[faceId];
                Check(selected.Width == full.Width && selected.Height == full.Height && selected.Metadata0 == full.Metadata0
&& selected.Runs.Length == full.Runs.Length, "Single-face disk frame becomes runtime frame0.");
                for (int i = 0; i < full.Runs.Length; i++)
                {
                    Check(selected.Runs[i].X == full.Runs[i].X && selected.Runs[i].Y == full.Runs[i].Y && selected.Runs[i].Colors.SequenceEqual(full.Runs[i].Colors), "Selected face preserves original packet pixels.");
                }
            }
        }

        return "dialogue: raw key/CRLF, owned controls, opening, page4, choice/global10 and persistent input flag passed";
    }

    private static void CheckModalBridge(SpriteFile tiles, BitmapFont font)
    {
        var clock = new FixtureClock
        {
            Milliseconds = uint.MaxValue - 10
        };
        var gate = new ModalFrameGate(clock, clock.Milliseconds);
        clock.Milliseconds = 9;
        Check(!gate.IsDue, "Strict >20 gate survives uint32 wrap.");
        clock.Milliseconds = 10;
        Check(gate.IsDue, "Twenty-one elapsed milliseconds cross the native gate.");
        gate.Commit();
        clock.Milliseconds += 200;
        Check(gate.IsDue, "Stall makes one frame due.");
        gate.Commit();
        Check(!gate.IsDue, "Commit starts from current time, with no catch-up backlog.");
        clock.Milliseconds = 0;
        gate = new(clock, 0);
        clock.Milliseconds = 210;
        int scheduled = 0;
        while (gate.IsDue)
        {
            gate.CommitScheduled();
            scheduled++;
        }

        Check(scheduled == 10 && gate.LastFrameMilliseconds == 210, "Scheduled commit retains all ten stalled logical opportunities.");
        clock.Milliseconds = uint.MaxValue - 10;
        gate = new(clock, clock.Milliseconds);
        clock.Milliseconds = 10;
        gate.CommitScheduled();
        Check(gate.LastFrameMilliseconds == 10, "Scheduled deadline crosses uint32 wrap.");
        clock.Milliseconds = 1000;
        gate.Resume();
        Check(!gate.IsDue, "Focus/movie resume resets paused time only.");
        gate.AdjustSpeed(true, false);
        clock.Milliseconds = 1019;
        Check(!gate.IsDue, "Faster threshold remains strict-greater.");
        clock.Milliseconds = 1020;
        gate.CommitScheduled();
        Check(gate.LastFrameMilliseconds == 1020 && gate.IntervalMilliseconds == 20, "Speed control selects threshold-plus-one scheduled interval.");
        var state = new GameState();
        var host = new FixtureFrameHost();
        var session = new DialogueSession(new(-1, -1, true, ["one"u8.ToArray(), "two"u8.ToArray()]), false);
        var driver = new DialogueModalDriver(host, tiles, font, _ => throw new InvalidOperationException("Unexpected face load."));
        driver.Run(session, state);
        Check(host.LogicalTicks == 6 && host.Presents > 0 && session.Completed && state.Globals[10] == 1, "Real driver waits for explicit post-opening input on sixth scheduled tick.");
        Check(host.Events.Take(7).SequenceEqual(new[] { "music", "scan78", "scan74", "before", "Confirm", "Up", "Down" }), "Music/speed/input priority before compositor.");
        Check(host.Events.IndexOf("Confirm") < host.Events.IndexOf("scene") && host.Events.IndexOf("scene") < host.Events.IndexOf("present"), "Input -> scene -> strict gate -> present order.");
        var slow = RunAtPresentationInterval(33, 0, tiles, font);
        var fast = RunAtPresentationInterval(7, 0, tiles, font);
        Check(slow.Ticks.SequenceEqual(fast.Ticks) && slow.Ticks.SequenceEqual(new uint[] { 21, 42, 63, 84, 105, 126 }), "30Hz/144Hz presentations retain identical logical dialogue schedule/input outcome.");
        Check(slow.Presents < fast.Presents, "Additional GPU redraws do not add logical dialogue ticks.");
        var stalled = RunAtPresentationInterval(7, 400, tiles, font, 420);
        Check(stalled.Ticks.Count == 20 && stalled.Ticks[^1] == 420 && stalled.Pumps > stalled.Ticks.Count, "Stall catches up every logical opportunity and pumps throughout bounded batches.");
        state.EndingResetFlag = 1;
        state.Globals[10] = 77;
        var bypassHost = new FixtureFrameHost();
        session = new(new(-1, -1, true, ["one"u8.ToArray()]), false);
        new DialogueModalDriver(bypassHost, tiles, font, _ => throw new InvalidOperationException()).Run(session, state);
        Check(session.NativeLoopBypassed && !session.Completed && state.Globals[10] == 77 && bypassHost.Presents == 0, "Native pending-transition bypass does not fabricate answer.");
        var queries = new List<DialogueInputAction>();
        session = new(new(-1, -1, true, ["one"u8.ToArray()]), false);
        session.ApplyInput(action =>
        {
            queries.Add(action);
            return true;
        }, state, _ =>
        {
        });
        Check(queries.SequenceEqual(new[] { DialogueInputAction.Confirm }), "Opening confirm consumes only its own edge query.");
    }

    private static FixtureFrameHost RunAtPresentationInterval(uint interval, uint stall, SpriteFile tiles, BitmapFont font, uint confirmAt = 126)
    {
        var host = new FixtureFrameHost
        {
            PresentationIntervalMilliseconds = interval,
            FirstPresentStall = stall,
            ConfirmAt = confirmAt
        };
        var session = new DialogueSession(new(-1, -1, true, ["one"u8.ToArray(), "two"u8.ToArray()]), false);
        var state = new GameState();
        new DialogueModalDriver(host, tiles, font, _ => throw new InvalidOperationException()).Run(session, state);
        Check(session.Completed && state.Globals[10] == 1, "Timestamped fixture chooses real explicit first answer.");
        return host;
    }

    private sealed class FixtureClock : IModalClock
    {
        public uint Milliseconds
        {
            get; set;
        }

        public void Yield() => Milliseconds = unchecked(Milliseconds + 1);
    }

    private sealed class FixtureFrameHost : IDialogueFrameHost
    {
        private readonly ushort[] pixels = new ushort[640 * 480];
        public bool Closed => false;
        public bool Focused => true;
        public Span<ushort> FrameBuffer => pixels;
        public ModalFrameGate Gate { get; } = new(new FixtureClock(), 0);
        public int Presents
        {
            get; private set;
        }
        public int LogicalTicks
        {
            get; private set;
        }
        public int Pumps
        {
            get; private set;
        }
        public uint PresentationIntervalMilliseconds
        {
            get; init;
        }
        public uint FirstPresentStall
        {
            get; init;
        }
        public uint ConfirmAt { get; init; } = 126;
        public List<uint> Ticks { get; } = [];
        public List<string> Events { get; } = [];

        public void Pump()
        {
            Pumps++;
        }

        public void BeginLogicalFrame(uint scheduledMilliseconds)
        {
            LogicalTicks++;
            Ticks.Add(scheduledMilliseconds);
        }

        public bool QueryScan(byte scan)
        {
            Events.Add($"scan{scan}");
            return false;
        }

        public bool QueryAction(DialogueInputAction action)
        {
            Events.Add(action.ToString());
            return action == DialogueInputAction.Confirm && Gate.LastFrameMilliseconds >= ConfirmAt;
        }

        public void UpdateMusic(GameState state) => Events.Add("music");
        public bool BeforeModalInput(GameState state)
        {
            Events.Add("before");
            return true;
        }

        public void ComposeScene(GameState state)
        {
            Events.Add("scene");
            Array.Clear(pixels);
        }

        public void PlaySound(int id) => Events.Add($"sound{id}");
        public void Present()
        {
            Check(unchecked(Gate.Clock.Milliseconds - Gate.LastFrameMilliseconds) < uint.MaxValue / 2, "Scheduled deadline is never in future at presentation.");
            Events.Add("present");
            Presents++;
            var clock = (FixtureClock)Gate.Clock;
            clock.Milliseconds = unchecked(clock.Milliseconds + PresentationIntervalMilliseconds + (Presents == 1 ? FirstPresentStall : 0));
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
