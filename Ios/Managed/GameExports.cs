using System.Runtime.InteropServices;
using System.Text;
using Atria.Game;
using Atria.Localization;
using Atria.Platform.Input;

namespace Atria.Ios;

public static unsafe class GameExports
{
    private static readonly MobileSession session = new();
    private static string apiError = "";

    /// <summary>Starts a managed game session, or returns zero when one is already running.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_start")]
    public static int Start(byte* bundle, byte* storage)
    {
        try
        {
            if (bundle == null || storage == null) return -1;
            string resources = Marshal.PtrToStringUTF8((nint)bundle)!;
            string output = Marshal.PtrToStringUTF8((nint)storage)!;
            Volatile.Write(ref apiError, "");
            return session.Start((window, presenter) => Run(resources, output, window, presenter));
        }
        catch (Exception ex) { Fail(ex); return -1; }
    }

    private static void Run(string resources, string output, MobileWindow window, MobilePresenter presenter)
    {
        var previousOut = Console.Out;
        var previousError = Console.Error;
        StreamWriter? log = null;
        try
        {
            Directory.CreateDirectory(output);
            string logPath = Path.Combine(output, "ios-session.log");
            if (File.Exists(logPath)) File.Move(logPath, Path.Combine(output, "ios-session.previous.log"), true);
            log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(log);
            Console.SetError(log);
            BuildInfo.UseHostAssembly(typeof(GameExports).Assembly);
            Console.WriteLine($"{DateTime.UtcNow:O} Atria {BuildInfo.Version}: managed session starting.");
            GameText.Use(new LocalizationCatalog(Path.Combine(resources, "localization")));
            using var audio = new AppleAudio();
            using var music = new AppleMusic(Path.Combine(resources, "music"));
            var movies = new AppleMovies(window, Path.Combine(resources, "movies"));
            GameSession.RunHosted(Path.Combine(resources, "game"), output, window, presenter, audio, music,
                new GameHostServices(resources, window, movies.Play, new AppleText()));
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(output, "ios-error.log"), ex.ToString(), new UTF8Encoding(false)); }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
            throw;
        }
        finally
        {
            try { AppleNative.MovieStop(); }
            catch (Exception ex) { Fail(ex); }
            Console.SetOut(previousOut);
            Console.SetError(previousError);
            log?.Dispose();
        }
    }

    /// <summary>Updates surface dimensions and focus without changing the game clock.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_surface")]
    public static void Surface(int width, int height, int focused)
    {
        try { if (width is > 0 and <= 8192 && height is > 0 and <= 8192) session.SetSurface(width, height, focused != 0); }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>Suspends platform input and rendering while retaining session state.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_suspend")]
    public static void Suspend()
    {
        try { session.SetFocused(false); }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>Resumes platform input and rendering without rebasing simulation time.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_resume")]
    public static void Resume()
    {
        try { session.SetFocused(true); }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>Publishes one full physical controller state in its stable native slot.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_pad")]
    public static void Pad(int slot, int connected, ushort buttons, byte leftTrigger, byte rightTrigger,
        short lx, short ly, short rx, short ry)
    {
        try
        {
            if (slot is >= 0 and < MobileWindow.PadCount)
                session.SubmitPad(slot, new PadSample(connected != 0, buttons, leftTrigger, rightTrigger, lx, ly, rx, ry));
        }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>Publishes touch controls independently of physical gamepad slots.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_touch_pad")]
    public static void TouchPad(ushort buttons, byte leftTrigger, byte rightTrigger, short x, short y)
    {
        try { session.SubmitVirtualPad(new PadSample(false, buttons, leftTrigger, rightTrigger, x, y, 0, 0)); }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>Reports a direct touch action even when no virtual control changed.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_touch_activity")]
    public static void TouchActivity(int identity)
    {
        try { session.SubmitTouchActivity(identity); }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>Requests loop shutdown; call atria_wait_stopped away from the UI thread to join it.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_stop")]
    public static int Stop()
    {
        try { session.RequestStop(); return 0; }
        catch (Exception ex) { Fail(ex); return -1; }
    }

    /// <summary>Joins the stopped loop and releases frame transport resources.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_wait_stopped")]
    public static int WaitStopped()
    {
        try { return session.WaitStopped(TimeSpan.FromSeconds(10)); }
        catch (Exception ex) { Fail(ex); return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "atria_key")]
    public static void Key(byte scan, int down)
    {
        try { session.SubmitKey(scan, down != 0); }
        catch (Exception ex) { Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "atria_pointer")]
    public static void Pointer(int x, int y, int down)
    {
        try { session.SubmitPointer(x, y, down != 0); }
        catch (Exception ex) { Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "atria_text")]
    public static void Text(byte* utf8)
    {
        try { if (utf8 != null) session.SubmitText(Marshal.PtrToStringUTF8((nint)utf8) ?? ""); }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>Copies a tightly packed BGRA frame into caller-owned memory.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_frame")]
    public static int Frame(uint* destination, int capacity, int* width, int* height)
    {
        try
        {
            if (destination == null || width == null || height == null || capacity < 0 || capacity > MobilePresenter.MaximumPixels) return -1;
            return session.CopyFrame(new Span<uint>(destination, capacity), out *width, out *height);
        }
        catch (Exception ex) { Fail(ex); return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "atria_error")]
    public static int Error(byte* output, int capacity)
    {
        try
        {
            string message = Volatile.Read(ref apiError);
            if (message.Length == 0) message = session.Error;
            if (message.Length == 0 && session.Closed) message = "Session closed. Reopen Atria to play again.";
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            if (output != null && capacity > bytes.Length)
            {
                bytes.CopyTo(new Span<byte>(output, capacity));
                output[bytes.Length] = 0;
            }
            return bytes.Length;
        }
        catch (Exception ex) { Fail(ex); return 0; }
    }

    private static void Fail(Exception exception) => Volatile.Write(ref apiError, exception.ToString());
}
