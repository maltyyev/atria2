namespace Atria.Game.Combat;

/// <summary>Required real host operations. None has a default successful no-op implementation.</summary>
public interface ICombatSerialHost
{
    //0x408360, before the one outer clock sample. Source owns actual music/input/window work.
    void PumpEventsAndMusic();
    bool ActiveMagic
    {
        get;
    }

    CombatFrameResult ActorPass(uint sampledNow);
    void HitPass(uint sampledNow);
    void ProcessDebugHeldKeys(uint sampledNow);
    bool QueryNativePauseToggle();
    //0x40a820 is stateful: actor visuals, shake, particles and magic lifetime advance here exactly once.
    void ComposeLogicalFrame(uint sampledNow);
    //0x407fe0, after composition. Call schedule.PollSpeedKeys at the recovered position in actual UI input.
    void ProcessFrameInput(uint sampledNow, CombatRenderSchedule schedule);
    //Pure cached framebuffer transport. Must not compose, consume input or advance any game state.
    void PresentCachedFrame();
}

public readonly record struct CombatDispatchResult(
    uint SampledMilliseconds,
    CombatFrameResult? Actors,
    bool LogicalFrameProduced,
    bool Terminal,
    bool FrozenRenderRequired,
    ulong FrameSerial
);
/// <summary>Serial0x4083d0 ordering, independent of how an owner produces actor opportunities.</summary>
public sealed class CombatSerialCoordinator
{
    private readonly ICombatSerialHost host;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private bool dispatching, hasSample;
    public CombatRenderSchedule RenderSchedule
    {
        get;
    }
    public uint LastSampledMilliseconds
    {
        get; private set;
    }
    public bool NativePause
    {
        get; private set;
    }
    public bool HasCachedFrame
    {
        get; private set;
    }
    public bool Completed => TerminalResult is not null;
    public CombatFrameResult? TerminalResult
    {
        get; private set;
    }
    public ulong FrameSerial
    {
        get; private set;
    }

    public CombatSerialCoordinator(ICombatSerialHost host, CombatRenderSchedule renderSchedule)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        RenderSchedule = renderSchedule ?? throw new ArgumentNullException(nameof(renderSchedule));
    }

    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
        {
            throw new InvalidOperationException("Combat opportunities belong to one serial owner thread.");
        }
    }

    private void BeginDispatch()
    {
        CheckOwner();
        if (dispatching)
        {
            throw new InvalidOperationException("Combat dispatch cannot reenter itself.");
        }

        if (Completed)
        {
            throw new InvalidOperationException("Combat already returned its actual actor outcome.");
        }

        dispatching = true;
    }

    /// <summary>The producer decides when to call. Pump runs before exactly one supplied clock read.</summary>
    public CombatDispatchResult OuterOpportunity(Func<uint> sampleClock)
    {
        ArgumentNullException.ThrowIfNull(sampleClock);
        BeginDispatch();
        try
        {
            if (NativePause)
            {
                throw new InvalidOperationException("Native frozen render loop must finish before another outer opportunity.");
            }

            host.PumpEventsAndMusic();
            uint now = sampleClock();
            LastSampledMilliseconds = now;
            hasSample = true;
            CombatFrameResult? actors = null;
            //Test the pointer once. Magic created during actor work does not cancel this pass's later hit scan.
            if (!host.ActiveMagic)
            {
                actors = host.ActorPass(now);
                if (actors.Value.ReturnCode != 0)
                {
                    TerminalResult = actors;
                    return Result(actors, false);
                }

                host.HitPass(now);
            }

            host.ProcessDebugHeldKeys(now);
            bool rendered = false;
            if (RenderSchedule.IsDue(now))
            {
                LogicalRender(now);
                rendered = true;
            }

            return Result(actors, rendered);
        }
        finally
        {
            dispatching = false;
        }
    }

    /// <summary>One native0x4084a4..0x4084f1 iteration: no pump, new clock sample, actor or hit work.</summary>
    public CombatDispatchResult FrozenRenderOpportunity()
    {
        BeginDispatch();
        try
        {
            if (!NativePause || !hasSample)
            {
                throw new InvalidOperationException("There is no active native frozen render loop.");
            }

            LogicalRender(LastSampledMilliseconds);
            return Result(null, true);
        }
        finally
        {
            dispatching = false;
        }
    }

    private void LogicalRender(uint sampledNow)
    {
        if (host.QueryNativePauseToggle())
        {
            NativePause = !NativePause;
        }

        host.ComposeLogicalFrame(sampledNow);
        host.ProcessFrameInput(sampledNow, RenderSchedule);
        //0x4083d0 presents even the iteration that clears NativePause; only then checks its loop flag.
        host.PresentCachedFrame();
        HasCachedFrame = true;
        FrameSerial = unchecked(FrameSerial + 1);
        RenderSchedule.CompleteLogicalRender(sampledNow);
    }

    private CombatDispatchResult Result(CombatFrameResult? actors, bool logicalFrame) => new(LastSampledMilliseconds, actors, logicalFrame, Completed, NativePause, FrameSerial);
    /// <summary>Additional GPU presentation only. No opportunity, clock, input or stateful rendering is produced.</summary>
    public bool TryPresentCached()
    {
        CheckOwner();
        if (dispatching)
        {
            throw new InvalidOperationException("Cached presentation cannot reenter logical combat dispatch.");
        }

        if (!HasCachedFrame)
        {
            return false;
        }

        host.PresentCachedFrame();
        return true;
    }
}
