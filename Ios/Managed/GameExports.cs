using System.Runtime.InteropServices;
using System.Text;
using Atria.Game;
using Atria.Localization;
using Atria.Platform.Input;
using Atria.Render.UI;

namespace Atria.Ios;

public static unsafe class GameExports
{
    private static readonly MobileWindow window = new();
    private static readonly MobilePresenter presenter = new();
    private static int started;
    private static string error = "";
    private static int finished;

    [UnmanagedCallersOnly(EntryPoint = "atria_start")]
    public static int Start(byte* bundle, byte* storage)
    {
        try
        {
            if (bundle == null || storage == null) return -1;
            string resources = Marshal.PtrToStringUTF8((nint)bundle)!;
            string output = Marshal.PtrToStringUTF8((nint)storage)!;
            if (Interlocked.CompareExchange(ref started, 1, 0) != 0) return 0;
            new Thread(() => Run(resources, output)) { IsBackground = true, Name = "Atria game" }.Start();
            return 1;
        }
        catch (Exception ex) { Volatile.Write(ref error, ex.ToString()); return -1; }
    }

    private static void Run(string resources, string output)
    {
        try
        {
            Directory.CreateDirectory(output);
            string logPath = Path.Combine(output, "ios-session.log");
            if (File.Exists(logPath)) File.Move(logPath, Path.Combine(output, "ios-session.previous.log"), true);
            using var log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(log);
            Console.SetError(log);
            Console.WriteLine($"{DateTime.UtcNow:O} Atria {BuildInfo.Version}: managed session starting.");
            GameText.Use(new LocalizationCatalog(Path.Combine(resources, "localization")));
            UnicodeText.UseRasterizer(new AppleText());
            using var audio = new AppleAudio();
            using var music = new AppleMusic(Path.Combine(resources, "music"));
            var movies = new AppleMovies(window, Path.Combine(resources, "movies"));
            GameSession.RunHosted(Path.Combine(resources, "game"), output, window, presenter, audio, music,
                new GameHostServices(resources, window, movies.Play));
        }
        catch (Exception ex)
        {
            Volatile.Write(ref error, ex.ToString());
            try { File.WriteAllText(Path.Combine(output, "ios-error.log"), ex.ToString(), new UTF8Encoding(false)); }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
        }
        finally { Volatile.Write(ref finished, 1); }
    }

    [UnmanagedCallersOnly(EntryPoint = "atria_surface")]
    public static void Surface(int width, int height, int focused)
    {
        if (width is > 0 and <= 8192 && height is > 0 and <= 8192)
            window.SetSurface(width, height, focused != 0);
    }

    [UnmanagedCallersOnly(EntryPoint = "atria_pad")]
    public static void Pad(ushort buttons, byte leftTrigger, byte rightTrigger, short x, short y)
        => window.SubmitPad(new PadSample(true, buttons, leftTrigger, rightTrigger, x, y, 0, 0));

    [UnmanagedCallersOnly(EntryPoint = "atria_key")]
    public static void Key(byte scan, int down) => window.SubmitKey(scan, down != 0);

    [UnmanagedCallersOnly(EntryPoint = "atria_pointer")]
    public static void Pointer(int x, int y, int down) => window.SubmitPointer(x, y, down != 0);

    [UnmanagedCallersOnly(EntryPoint = "atria_text")]
    public static void Text(byte* utf8)
    {
        if (utf8 != null) window.SubmitText(Marshal.PtrToStringUTF8((nint)utf8) ?? "");
    }

    [UnmanagedCallersOnly(EntryPoint = "atria_frame")]
    public static int Frame(uint* destination, int capacity, int* width, int* height)
    {
        if (destination == null || width == null || height == null || capacity < 0 || capacity > MobilePresenter.MaximumPixels) return -1;
        return presenter.CopyFrame(new Span<uint>(destination, capacity), out *width, out *height);
    }

    [UnmanagedCallersOnly(EntryPoint = "atria_error")]
    public static int Error(byte* output, int capacity)
    {
        string message = Volatile.Read(ref error);
        if (message.Length == 0 && Volatile.Read(ref finished) != 0) message = "Session closed. Reopen Atria to play again.";
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        if (output != null && capacity > bytes.Length)
        {
            bytes.CopyTo(new Span<byte>(output, capacity));
            output[bytes.Length] = 0;
        }
        return bytes.Length;
    }
}
