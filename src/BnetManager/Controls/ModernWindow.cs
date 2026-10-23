using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;

namespace BnetManager.Controls;

public class ModernWindow : Window
{
    public ModernWindow()
    {
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 56,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            UseAeroCaptionButtons = false
        });
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => Close()));
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => WindowState = WindowState.Minimized));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var immersiveDarkMode = 0;
        var corners = 2;
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(handle, 20, ref immersiveDarkMode, sizeof(int)));
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int)));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
