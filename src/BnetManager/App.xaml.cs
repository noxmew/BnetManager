using System.Windows;
using BnetManager.Controls;
using BnetManager.Core;

namespace BnetManager;

public partial class App : Application
{
    private FileStream? instanceLock;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instanceLock = InstanceLease.TryAcquire();
        if (instanceLock is null)
        {
            MessageDialog.Show(null, "BnetManager", "BnetManager 已在运行，请切回已有窗口。");
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            MessageDialog.Show(MainWindow, "无法继续运行", args.Exception.Message);
            Shutdown(1);
        };
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception error)
        {
            MessageDialog.Show(null, "启动失败", error.Message);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instanceLock?.Dispose();
        base.OnExit(e);
    }
}
