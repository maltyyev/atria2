namespace Atria.Platform;

/// <summary>Timestamp-driven logical opportunities. The mode supplies its recovered interval.</summary>
public sealed class LogicalFrameClock
{
    private readonly long interval;
    private long next;
    public long Frames
    {
        get; private set;
    }

    public LogicalFrameClock(TimeSpan interval)
    {
        if (interval.Ticks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        this.interval = interval.Ticks;
    }

    public bool TakeDueFrame(TimeSpan elapsed)
    {
        if (elapsed.Ticks < next)
        {
            return false;
        }

        next = checked(next + interval);
        Frames++;
        return true;
    }

    public void SuspendAt(TimeSpan elapsed) => next = checked(elapsed.Ticks + interval);
}
