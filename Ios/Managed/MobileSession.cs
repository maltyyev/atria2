using Atria.Platform.Input;

namespace Atria.Ios;

/// <summary>Owns one managed game thread and its input and frame transports.</summary>
public sealed class MobileSession
{
    private readonly object sync = new();
    private MobileWindow? window;
    private MobilePresenter? presenter;
    private Thread? thread;
    private string error = "";
    private bool finished;

    /// <summary>Gets the latest worker failure, if the session ended with an error.</summary>
    public string Error { get { lock (sync) return error; } }

    /// <summary>Starts a fresh session; a running session remains the sole owner.</summary>
    public int Start(Action<MobileWindow, MobilePresenter> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        lock (sync)
        {
            if (thread is { IsAlive: true }) return 0;
            ReleaseFinishedSession();
            var nextWindow = new MobileWindow();
            var nextPresenter = new MobilePresenter();
            window = nextWindow;
            presenter = nextPresenter;
            error = "";
            finished = false;
            thread = new Thread(() =>
            {
                try { run(nextWindow, nextPresenter); }
                catch (Exception ex) { lock (sync) error = ex.ToString(); }
                finally { lock (sync) finished = true; }
            }) { IsBackground = true, Name = "Atria game" };
            try
            {
                thread.Start();
                return 1;
            }
            catch
            {
                ReleaseFinishedSession();
                throw;
            }
        }
    }

    /// <summary>Updates host size and focus without advancing the game's logical clock.</summary>
    public void SetSurface(int width, int height, bool focused)
    {
        lock (sync) window?.SetSurface(width, height, focused);
    }

    /// <summary>Changes focus while preserving the current drawable dimensions.</summary>
    public void SetFocused(bool focused)
    {
        lock (sync)
        {
            if (window is { } current)
                current.SetSurface(current.ClientSize.Width, current.ClientSize.Height, focused);
        }
    }

    /// <summary>Indicates that the most recently started worker has exited.</summary>
    public bool Closed { get { lock (sync) return finished; } }

    /// <summary>Publishes one physical controller sample without combining other devices.</summary>
    public void SubmitPad(int slot, PadSample sample)
    {
        lock (sync) window?.SubmitPad(slot, sample);
    }

    /// <summary>Publishes a touch-control sample on the independent virtual-pad channel.</summary>
    public void SubmitVirtualPad(PadSample sample)
    {
        lock (sync) window?.SubmitVirtualPad(sample);
    }

    /// <summary>Records a touch action that may not alter the virtual-pad sample.</summary>
    public void SubmitTouchActivity(int identity)
    {
        lock (sync) window?.SubmitTouchActivity(identity);
    }

    /// <summary>Queues one host keyboard transition.</summary>
    public void SubmitKey(byte scan, bool down)
    {
        lock (sync) window?.SubmitKey(scan, down);
    }

    /// <summary>Queues a pointer transition in drawable coordinates.</summary>
    public void SubmitPointer(int x, int y, bool down)
    {
        lock (sync) window?.SubmitPointer(x, y, down);
    }

    /// <summary>Queues UTF-8 text converted by the host bridge.</summary>
    public void SubmitText(string value)
    {
        lock (sync) window?.SubmitText(value);
    }

    /// <summary>Copies the current owned frame while protecting it from session disposal.</summary>
    public int CopyFrame(Span<uint> destination, out int width, out int height)
    {
        lock (sync)
        {
            if (presenter is null) { width = height = 0; return 0; }
            return presenter.CopyFrame(destination, out width, out height);
        }
    }

    /// <summary>Requests shutdown and wakes a producer waiting for a frame consumer.</summary>
    public void RequestStop()
    {
        lock (sync)
        {
            window?.RequestClose();
            presenter?.Wake();
        }
    }

    /// <summary>Joins shutdown away from the UI thread, then releases the old presenter.</summary>
    public int WaitStopped(TimeSpan timeout)
    {
        Thread? stoppedThread;
        lock (sync) stoppedThread = thread;
        if (stoppedThread is null) return 0;
        if (stoppedThread == Thread.CurrentThread || !stoppedThread.Join(timeout)) return -1;
        lock (sync)
        {
            if (ReferenceEquals(thread, stoppedThread)) ReleaseFinishedSession();
        }
        return 0;
    }

    private void ReleaseFinishedSession()
    {
        if (thread is { IsAlive: true }) return;
        presenter?.Dispose();
        thread = null;
        window = null;
        presenter = null;
    }
}
