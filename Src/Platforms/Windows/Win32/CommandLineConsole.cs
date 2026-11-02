using System.Runtime.InteropServices;

namespace Atria.Platforms.Windows.Win32;

/// <summary>Provides console attachment for the Windows executable host.</summary>
public static partial class CommandLineConsole
{
    /// <summary>Attaches standard output and error to the invoking console when available.</summary>
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
