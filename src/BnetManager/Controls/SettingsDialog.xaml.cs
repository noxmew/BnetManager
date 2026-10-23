using System.Diagnostics;
using System.Windows;
using BnetManager.Core;
using Microsoft.Win32;

namespace BnetManager.Controls;

public partial class SettingsDialog : ModernWindow
{
    private readonly string dataRoot;
    public string LauncherPath => LauncherInput.Text.Trim();

    public SettingsDialog(string? launcher, string dataRoot)
    {
        InitializeComponent();
        LauncherInput.Text = launcher ?? "";
        this.dataRoot = dataRoot;
    }

    private void BrowseClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "选择官方战网启动程序", Filter = "战网客户端|Battle.net.exe;Battle.net Launcher.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) == true) LauncherInput.Text = picker.FileName;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try { BattleNetProcess.ValidateLauncher(LauncherPath); DialogResult = true; }
        catch (Exception error) { Validation.Text = error.Message; }
    }

    private void OpenDataClick(object sender, RoutedEventArgs e)
    {
        try { using var process = Process.Start(new ProcessStartInfo { FileName = dataRoot, UseShellExecute = true }); }
        catch (Exception error) { Validation.Text = error.Message; }
    }
}
