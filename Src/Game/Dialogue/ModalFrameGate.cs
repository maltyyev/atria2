using System.Diagnostics;

namespace Atria.Game.Dialogue;

public interface IModalClock
{
    uint Milliseconds
    {
        get;
    }

    void Yield();
}

public sealed class MonotonicModalClock : IModalClock
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    public uint Milliseconds => unchecked((uint)clock.ElapsedMilliseconds);

    public void Pause() => clock.Stop();
    public void Continue() => clock.Start();
    public void Yield() => Thread.Sleep(1);
}

/// <summary>Native strict-greater millisecond threshold; scheduled commits retain elapsed logical opportunities across GPU stalls.</summary>
public sealed class ModalFrameGate(IModalClock clock, uint previousFrameMilliseconds, int thresholdMilliseconds = 20)
{
    public IModalClock Clock { get; } = clock;
    public uint LastFrameMilliseconds { get; private set; } = previousFrameMilliseconds;
    public int ThresholdMilliseconds { get; private set; } = thresholdMilliseconds >= 0 ? thresholdMilliseconds : throw new ArgumentOutOfRangeException(nameof(thresholdMilliseconds));
    public bool IsDue => unchecked(Clock.Milliseconds - LastFrameMilliseconds) > (uint)ThresholdMilliseconds;
    public uint IntervalMilliseconds => checked((uint)ThresholdMilliseconds + 1u);
    public uint NextFrameMilliseconds => unchecked(LastFrameMilliseconds + IntervalMilliseconds);

    // Intentional scheduling policy: consume one logical opportunity, not all elapsed time.
    public void CommitScheduled() => LastFrameMilliseconds = NextFrameMilliseconds;
    // Legacy observed-current-time commit retained for native gate fixtures, not deterministic runtime scheduling.
    public void Commit() => LastFrameMilliseconds = Clock.Milliseconds;
    public uint ResumeGeneration
    {
        get; private set;
    }

    // Ownership changes discard modal scheduling debt, but keep the shared input/query latch.
    public void RebaseSchedule() => LastFrameMilliseconds = Clock.Milliseconds;
    public void Resume()
    {
        RebaseSchedule();
        ResumeGeneration = unchecked(ResumeGeneration + 1);
    }

    // Mode entry may change the native comparison threshold without resetting its prior timestamp.
    public void SetThresholdMilliseconds(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ThresholdMilliseconds = value;
    }

    public void AdjustSpeed(bool faster, bool slower)
    {
        if (faster && ThresholdMilliseconds > 15)
        {
            ThresholdMilliseconds--;
        }

        if (slower && ThresholdMilliseconds < 45)
        {
            ThresholdMilliseconds++;
        }
    }
}

/// <summary>Shared per-scan query latch4048c0. Query only keys actually visited by native priority flow.</summary>
public sealed class ScanCodeEdges
{
    private readonly bool[] consumed = new bool[256];
    public void Reset() => Array.Clear(consumed);
    public void Reset(byte scan) => consumed[scan] = false;
    //4048c0 can poll the physical source twice on the first rising observation.
    public bool Query(Func<byte, bool> currentKeys, byte scan)
    {
        if (!currentKeys(scan))
        {
            consumed[scan] = false;
        }
        else if (!consumed[scan] && currentKeys(scan))
        {
            consumed[scan] = true;
            return true;
        }

        return false;
    }

    public bool Query(ReadOnlySpan<bool> currentKeys, byte scan)
    {
        if (currentKeys.Length != 256)
        {
            throw new ArgumentException("Expected256 scan states.");
        }

        bool down = currentKeys[scan];
        if (!down)
        {
            consumed[scan] = false;
            return false;
        }

        if (consumed[scan])
        {
            return false;
        }

        consumed[scan] = true;
        return true;
    }
}
