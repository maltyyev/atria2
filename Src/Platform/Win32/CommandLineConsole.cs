using System.Runtime.InteropServices;

namespace Atria.Platform.Win32;

internal static partial class CommandLineConsole
{
    public static void AttachToParent()
    {
        const int standardOutput = -11;
        nint output = GetStdHandle(standardOutput);
        if (output == 0 || output == -1)
        {
            AttachConsole(uint.MaxValue);
        }

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), System.Text.Encoding.UTF8) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), System.Text.Encoding.UTF8) { AutoFlush = true });
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);
}
