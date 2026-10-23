using System.Runtime.InteropServices;
using System.Text;

namespace BnetManager.Cli;

internal static class CliConsole
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    public static void Initialize()
    {
        var output = GetStdHandle(-11);
        if (output == IntPtr.Zero || output == new IntPtr(-1)) AttachConsole(uint.MaxValue);
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        // Accepted account operations must finish before the process releases the data lock.
        Console.CancelKeyPress += (_, args) => args.Cancel = true;
    }
}
