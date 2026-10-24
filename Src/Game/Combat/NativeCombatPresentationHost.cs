using Atria.Game.Dialogue;
using Atria.Platform;
using Atria.Platform.Win32;

namespace Atria.Game.Combat;

/// <summary>Live held keyboard during native frozen rendering; scheduled held input during outer samples.</summary>
public sealed class CombatHeldInputSource(Func<byte, bool> scheduledHeld, Func<byte, bool> physicalHeld)
{
    public bool FrozenTransport
    {
        get; private set;
    }

    public void UseFrozenTransport() => FrozenTransport = true;
    public void UseScheduledInput() => FrozenTransport = false;
    public bool IsHeld(byte scan) => FrozenTransport ? physicalHeld(scan) : scheduledHeld(scan);
}

public readonly record struct CombatTransportState(bool Closed, bool Focused)
{
    public bool Ready => !Closed && Focused;
}

/// <summary>Win32 transport for the existing window/frame host; supplies no combat opportunity frequency.</summary>
public sealed class NativeCombatPresentationHost : ICombatPresentationHost, IDisposable
{
    private readonly NativeWindow window;
    private readonly NativeDialogueFrameHost frames;
    private readonly Action updateMusic, ensureFocusedLifecycle;
    private readonly CombatHeldInputSource held;
    public CombatScanInput Input
    {
        get;
    }
    public bool Closed => window.Closed;
    public bool Focused => frames.Focused;

    public NativeCombatPresentationHost(NativeWindow window, NativeDialogueFrameHost frames, int configuredPlayers, Action updateMusic, Action ensureFocusedLifecycle)
    {
        this.window = window;
        this.frames = frames;
        this.updateMusic = updateMusic ?? throw new ArgumentNullException(nameof(updateMusic));
        this.ensureFocusedLifecycle = ensureFocusedLifecycle ?? throw new ArgumentNullException(nameof(ensureFocusedLifecycle));
        held = new(frames.IsHeld, scan => window.ScanKeys[scan]);
        Input = new(held.IsHeld, frames.QueryWithHeldScan)
        {
            ConfiguredPlayers = configuredPlayers
        };
        //P and E/R/Q/W are queried only during logical renders, not every actor opportunity.
        frames.DeferScanTapsUntilPoll([0x19, 0x12, 0x13, 0x10, 0x11]);
    }

    public void PumpEventsAndMusic()
    {
        frames.Pump();
        ensureFocusedLifecycle();
        if (Closed || !Focused)
        {
            throw new InvalidOperationException("Combat focus lifecycle returned without an active window.");
        }

        updateMusic();
    }

    public void BeginInputSample(uint milliseconds)
    {
        frames.BeginLogicalFrame(milliseconds);
        held.UseScheduledInput();
    }

    public void CompleteFrameInput() => frames.CompleteDeferredScanPoll();
    public void Dispose() => frames.StopDeferringScanTaps();
    //Intentional modern transport repair: original frozen loop never pumps Win32 messages.
    //RefreshTransportInput omits afterPump and consumes queued short taps once through the shared bank.
    public CombatTransportState RefreshFrozenInput()
    {
        held.UseFrozenTransport();
        frames.RefreshTransportInput();
        return new(Closed, Focused);
    }

    public void Present(ReadOnlySpan<ushort> completedPixels)
    {
        if (Closed || !Focused)
        {
            throw new InvalidOperationException("Combat transport cannot present a closed or inactive surface.");
        }

        if (completedPixels.Length != 640 * 480)
        {
            throw new ArgumentException("Combat framebuffer must be640x480.");
        }

        completedPixels.CopyTo(frames.FrameBuffer);
        frames.Present();
    }
}
