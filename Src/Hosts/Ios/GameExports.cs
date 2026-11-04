using System.Runtime.InteropServices;
using System.Text;
using Atria.Core.Contracts.Host;
using Atria.Core.Runtime;
using Atria.Core.Resources.Localization;
using Atria.Core.Contracts.Input;
using Atria.Core;
using Atria.Platforms.Ios;

namespace Atria.Hosts.Ios;

public static unsafe class GameExports
{
    private const int MobileTargetIdMaximumUtf8Bytes = 512;
    private const int MobileTextMaximumUtf8Bytes = 4096;
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
        AppleMovies? movies = null;
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
            movies = new AppleMovies(window, Path.Combine(resources, "movies"));
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
            try { movies?.StopPlayback(); }
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

    /// <summary>Reports a direct touch action even when no touch-control state changed.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_touch_activity")]
    public static void TouchActivity(int identity)
    {
        try { session.SubmitTouchActivity(identity); }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>Gets the revision of the cached Core-owned mobile UI snapshot.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_mobile_ui_revision")]
    public static ulong MobileUiRevision()
    {
        try { return session.UiRevision; }
        catch (Exception ex) { Fail(ex); return 0; }
    }

    /// <summary>Copies cached UTF-8 UI JSON into caller-owned memory; returns required length.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_mobile_ui_copy")]
    public static int MobileUiCopy(byte* output, int capacity)
    {
        try
        {
            if (capacity < 0 || output == null && capacity != 0) return -1;
            return output == null ? session.CopyUiSnapshot(Span<byte>.Empty)
                : session.CopyUiSnapshot(new Span<byte>(output, capacity));
        }
        catch (Exception ex) { Fail(ex); return -1; }
    }

    /// <summary>Queues one context-guarded semantic action from the native mobile menu.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_mobile_ui_command")]
    public static void MobileUiCommand(ulong contextId, int action, byte* targetId, int value, byte* text)
    {
        try
        {
            var kind = (MobileUiAction)action;
            if (!Enum.IsDefined(kind)) return;
            if (!TryReadUtf8(targetId, MobileTargetIdMaximumUtf8Bytes, out string? target)) return;
            bool validText = TryReadUtf8(text, MobileTextMaximumUtf8Bytes, out string? submittedText);
            bool invalidText = !validText;
            session.SubmitUiCommand(new(contextId, kind, target, value, submittedText, invalidText));
        }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>Queues raw scene-aware touch controls without translating them to Xbox buttons.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_mobile_touch_sample")]
    public static void MobileTouchSample(ulong contextId, short leftX, short leftY, ushort buttons)
    {
        try
        {
            const ushort knownButtons = (ushort)(MobileTouchButtons.Up | MobileTouchButtons.Down | MobileTouchButtons.Left |
                MobileTouchButtons.Right | MobileTouchButtons.A | MobileTouchButtons.B | MobileTouchButtons.X);
            if ((buttons & ~knownButtons) != 0) return;
            session.SubmitTouchState(new(contextId, leftX, leftY, (MobileTouchButtons)buttons));
        }
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

    /// <summary>Queues a physical keyboard scan transition for the active session.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_key")]
    public static void Key(byte scan, int down)
    {
        try { session.SubmitKey(scan, down != 0); }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>Queues a pointer button transition in drawable pixel coordinates.</summary>
    [UnmanagedCallersOnly(EntryPoint = "atria_pointer")]
    public static void Pointer(int x, int y, int down)
    {
        try { session.SubmitPointer(x, y, down != 0); }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>Copies null-terminated UTF-8 host text into the session input queue.</summary>
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

    /// <summary>Returns the UTF-8 error length, copying the error when the supplied buffer fits.</summary>
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

    private static bool TryReadUtf8(byte* value, int maximumBytes, out string? decoded)
    {
        decoded = null;
        if (value == null) return true;
        int length = 0;
        while (length < maximumBytes && value[length] != 0) length++;
        if (length == maximumBytes) return false;
        try
        {
            decoded = new UTF8Encoding(false, true).GetString(new ReadOnlySpan<byte>(value, length));
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

}
