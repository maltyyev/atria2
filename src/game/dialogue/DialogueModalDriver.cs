using Atria.Audio;
using Atria.Game.State;
using Atria.Platform;
using Atria.Render;
using Atria.Render.Codecs;
using Atria.Resources;
using Atria.Game.Controls;

namespace Atria.Game.Dialogue;

public delegate void DialogueSceneDraw(GameState state, Span<ushort> target);
public delegate void FrameOverlay(Span<ushort> target);

public interface IDialogueFrameHost
{
    bool Closed { get; }
    bool Focused { get; }
    Span<ushort> FrameBuffer { get; }
    ModalFrameGate Gate { get; }
    void Pump();
    void BeginLogicalFrame(uint scheduledMilliseconds);
    bool QueryScan(byte scan);
    bool QueryAction(DialogueInputAction action);
    void UpdateMusic(GameState state);
    bool BeforeModalInput(GameState state);
    void ComposeScene(GameState state);
    void PlaySound(int id);
    void Present();
}

/// <summary>Uses an existing Win32/D3D11/XAudio2 session. Owns buffers only, never the window/devices/music.</summary>
public sealed class NativeDialogueFrameHost : IDialogueFrameHost
{
    private readonly NativeWindow window;
    private readonly D3D11Presenter presenter;
    private readonly ControlBindings bindings;
    private readonly ScheduledScanInput scanInput;
    private readonly DialogueSceneDraw compose;
    private readonly Action<GameState> updateMusic;
    private readonly Func<GameState, bool> beforeInput;
    private readonly IReadOnlyList<SoundEffectBank> effects;
    private readonly Func<int> effectAttenuation;
    private readonly Action? afterPump;
    private readonly ushort[] frameBuffer = new ushort[640 * 480];
    private readonly uint[] presentation = new uint[640 * 480];
    private readonly ushort[] overlayFrame=new ushort[640*480];
    public InputRouter? Controls {get;set;}
    public FrameOverlay? Overlay {get;set;}
    private uint inputResumeGeneration;
    public ModalFrameGate Gate { get; }
    public bool Closed => window.Closed;
    public bool Focused { get { var size = window.ClientSize; return window.Focused && size.Width > 0 && size.Height > 0; } }
    public Span<ushort> FrameBuffer => frameBuffer;

    public NativeDialogueFrameHost(NativeWindow window, D3D11Presenter presenter, ControlBindings bindings,
        ScanCodeEdges edges, ModalFrameGate gate, DialogueSceneDraw composeScene, Action<GameState> updateMusic,
        Func<GameState, bool> beforeModalInput, IReadOnlyList<SoundEffectBank> effects, Func<int> effectAttenuation,Action? afterPump=null)
    {
        this.window = window; this.presenter = presenter; this.bindings = bindings; Gate = gate;
        scanInput=new(NativeWindow.MessageClockMilliseconds,gate.Clock.Milliseconds,edges);
        inputResumeGeneration=gate.ResumeGeneration;
        scanInput.Resume(gate.Clock.Milliseconds);
        compose = composeScene; this.updateMusic = updateMusic; beforeInput = beforeModalInput;
        this.effects = effects; this.effectAttenuation = effectAttenuation;
        this.afterPump=afterPump;
    }

    public void Pump()
    {
        window.Pump();
        afterPump?.Invoke();
        SynchronizeInputResume();
        while(window.TryDequeueScanInput(out var input)) scanInput.Enqueue(input);
    }
    //Input-only transport refresh for native frozen combat; no afterPump, music or modal gate commit.
    public void RefreshTransportInput()
    {
        window.Pump();SynchronizeInputResume();
        while(window.TryDequeueScanInput(out var input)) scanInput.Enqueue(input);
        scanInput.BeginLogicalFrame(Gate.Clock.Milliseconds);
    }
    public bool QueryWithHeldScan(byte scan,Func<byte,bool> held)=>scanInput.QueryWithHeld(scan,held);
    public void DeferScanTapsUntilPoll(ReadOnlySpan<byte> scans)=>scanInput.DeferTapsUntilPoll(scans);
    public void CompleteDeferredScanPoll()=>scanInput.CompleteDeferredPoll();
    public void StopDeferringScanTaps()=>scanInput.StopDeferringTaps();
    public void BeginLogicalFrame(uint scheduledMilliseconds)
    {
        SynchronizeInputResume();
        scanInput.BeginLogicalFrame(scheduledMilliseconds);
    }
    private void SynchronizeInputResume()
    {
        if(inputResumeGeneration==Gate.ResumeGeneration) return;
        scanInput.Resume(Gate.LastFrameMilliseconds); inputResumeGeneration=Gate.ResumeGeneration;
    }
    public bool QueryScan(byte scan) => Controls?.LegacyMenu(scan,true)??scanInput.Query(scan);
    public bool IsHeld(byte scan) => Controls?.LegacyMenu(scan,false)??scanInput.IsHeld(scan);
    public void ClearScanLatch(byte scan) => scanInput.ClearQueryLatch(scan);
    public bool QueryAction(DialogueInputAction action) => QueryScan(action switch
    { DialogueInputAction.Confirm => bindings.Confirm, DialogueInputAction.Up => bindings.Up, _ => bindings.Down });
    public void UpdateMusic(GameState state) => updateMusic(state);
    public bool BeforeModalInput(GameState state) => beforeInput(state);
    public void ComposeScene(GameState state) => compose(state, frameBuffer);
    public void PlaySound(int id)
    {
        if ((uint)id >= (uint)effects.Count) throw new InvalidDataException("Required native dialogue sound is unavailable.");
        effects[id].SetCurrentVolume(effectAttenuation()); effects[id].Play();
    }
    public void Present()
    {
        frameBuffer.CopyTo(overlayFrame,0);Overlay?.Invoke(overlayFrame);
        for (int i = 0; i < frameBuffer.Length; i++) presentation[i] = SpriteRaster.ToBgra32(overlayFrame[i]);
        (int width, int height) = window.ClientSize;
        if (width <= 0 || height <= 0) throw new InvalidOperationException("Cannot present a minimized modal surface.");
        presenter.Resize(width, height); presenter.Present(presentation);
    }
}

/// <summary>Synchronous native42cde0 bridge with scheduled logical updates independent of GPU presentation.</summary>
public sealed class DialogueModalDriver : IDialogueModalDriver
{
    private readonly IDialogueFrameHost host;
    private readonly SpriteFile tiles;
    private readonly BitmapFont font;
    private readonly Func<int, SpriteFrame> loadFace;
    private readonly Func<DialogueInputAction, bool> readPressed;
    private readonly Action<int> sound;
    private readonly DialogueSceneDraw? finalOverlay;

    public DialogueModalDriver(IDialogueFrameHost host, SpriteFile tiles, BitmapFont smallFont, FaceSpriteSource faces,DialogueSceneDraw? finalOverlay=null)
        : this(host, tiles, smallFont, faces.Load,finalOverlay) { }

    public DialogueModalDriver(IDialogueFrameHost host, SpriteFile tiles, BitmapFont smallFont, Func<int, SpriteFrame> loadFace,DialogueSceneDraw? finalOverlay=null)
    {
        this.host = host; this.tiles = tiles; font = smallFont; this.loadFace = loadFace;
        readPressed = host.QueryAction; sound = host.PlaySound;
        this.finalOverlay=finalOverlay;
    }

    public void Run(DialogueSession session, GameState state)
    {
        // Both callbacks load the requested face even when the pending transition bypasses the loop.
        SpriteFrame? face = session.Request.Face == -1 ? null : loadFace(session.Request.Face);
        if (state.EndingResetFlag != 0) { session.BypassNativeLoop(state); return; }
        bool hasFrame=false;
        int mouseX=-1,mouseY=-1;
        const int maximumTicksPerBatch=8;
        while (!session.Completed)
        {
            AwaitFocused();
            int ticks=0;
            while(!session.Completed && host.Gate.IsDue && ticks<maximumTicksPerBatch)
            {
                AwaitFocused();
                if(!host.Gate.IsDue) break; // focus resume discarded only paused wall time.
                host.Gate.CommitScheduled();
                host.BeginLogicalFrame(host.Gate.LastFrameMilliseconds);
                if (state.Mode != 1) host.UpdateMusic(state);
                // Input speed changes select the NEXT opportunity; the current deadline was already due.
                bool faster = host.QueryScan(0x4e), slower = host.QueryScan(0x4a);
                host.Gate.AdjustSpeed(faster, slower);
                if(host is NativeDialogueFrameHost {Controls: {} control}&&(mouseX!=control.MouseX||mouseY!=control.MouseY))
                {mouseX=control.MouseX;mouseY=control.MouseY;session.PointerSelect(mouseX,mouseY);}
                if (host.BeforeModalInput(state)) session.ApplyInput(readPressed, state, sound);
                host.ComposeScene(state);
                if (!session.Completed)
                    DialogueRaster.Draw(session.Request, session.AdvancePresentation(), tiles, face, font, host.FrameBuffer);
                finalOverlay?.Invoke(state,host.FrameBuffer);
                hasFrame=true; ticks++;
            }
            // Bound each batch without dropping debt or blocking the input pump behind presentation.
            if(!session.Completed && host.Gate.IsDue) continue;
            if(hasFrame) host.Present(); // Pure redraw of cached pixels: no logical state changes.
            if(!session.Completed) host.Gate.Clock.Yield();
        }
    }

    private void AwaitFocused()
    {
        host.Pump();
        if (host.Closed) throw new OperationCanceledException("The game window closed during dialogue.");
        if (host.Focused) return;
        do
        {
            host.Gate.Clock.Yield(); host.Pump();
            if (host.Closed) throw new OperationCanceledException("The game window closed during dialogue.");
        } while (!host.Focused);
        host.Gate.Resume(); // Intentional focus-pause policy: no catch-up or automatic answer on restore.
    }
}
