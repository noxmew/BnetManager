using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using BnetManager.Core;
using BnetManager.Mcp;
using Microsoft.Win32;

namespace BnetManager.Controls;

public partial class SettingsDialog : ModernWindow
{
    private readonly string dataRoot;
    private CancellationTokenSource? updateCancellation;
    private Uri? updatePage;
    private readonly McpService mcp;
    private readonly CancellationToken serviceLifetime;
    private bool closed;
    public string LauncherPath => LauncherInput.Text.Trim();

    public SettingsDialog(string? launcher, string dataRoot, McpService mcp, CancellationToken serviceLifetime)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 32;
        LauncherInput.Text = launcher ?? "";
        this.dataRoot = dataRoot;
        this.mcp = mcp;
        this.serviceLifetime = serviceLifetime;
        VersionText.Text = $"v{UpdateChecker.CurrentVersionText}";
        mcp.Changed += RefreshMcpControls;
        RefreshMcpControls();
    }

    private void RefreshMcpControls()
    {
        if (closed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(RefreshMcpControls); return; }
        McpStateText.Text = mcp.IsChanging ? "正在更新服务…" : mcp.IsRunning ? "运行中 · 仅本机访问" : mcp.Configuration.Enabled ? "未启动" : "未开启";
        McpToggleButton.Content = mcp.Configuration.Enabled ? "关闭服务" : "开启服务";
        McpToggleButton.IsEnabled = !mcp.IsChanging;
        McpRetryButton.Visibility = mcp.Configuration.Enabled && !mcp.IsRunning && !mcp.IsChanging ? Visibility.Visible : Visibility.Collapsed;
        McpPortInput.Text = mcp.Configuration.Port.ToString();
        McpPortInput.IsEnabled = !mcp.IsRunning && !mcp.IsChanging;
        McpCopyButton.IsEnabled = mcp.IsRunning && !mcp.IsChanging;
        McpEndpointText.Text = mcp.Configuration.Endpoint;
        McpErrorText.Text = mcp.LastError ?? "";
    }

    private async void McpToggleClick(object sender, RoutedEventArgs e) => await ConfigureMcpAsync(!mcp.Configuration.Enabled);

    private async void McpRetryClick(object sender, RoutedEventArgs e) => await ConfigureMcpAsync(true);

    private async Task ConfigureMcpAsync(bool enabled)
    {
        if (mcp.IsChanging) return;
        if (!int.TryParse(McpPortInput.Text, out var port) || port is < 1024 or > 65535)
        {
            McpErrorText.Text = "端口需要在 1024～65535 之间。";
            return;
        }
        try { await mcp.ConfigureAsync(enabled, port, serviceLifetime); }
        catch (Exception error) { if (!closed) McpErrorText.Text = error.Message; }
    }

    private void McpCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(mcp.Configuration.ConnectionJson());
            McpErrorText.Text = "";
            McpStateText.Text = "连接配置已复制";
        }
        catch (Exception) { McpErrorText.Text = "无法复制，请稍后重试。"; }
    }

    private async void CheckUpdateClick(object sender, RoutedEventArgs e)
    {
        if (updateCancellation != null) return;
        using var cancellation = new CancellationTokenSource();
        updateCancellation = cancellation;
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = "正在检查…";
        updatePage = null;
        UpdateDownloadButton.Visibility = Visibility.Collapsed;
        UpdateResult.Visibility = Visibility.Visible;
        UpdateStatus.Text = "正在检查更新…";
        try
        {
            var release = await UpdateChecker.CheckAsync(cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            UpdateStatus.Text = release == null ? "已是最新版本。" : $"发现新版本 v{release.Version.ToString(3)}";
            updatePage = release?.PageUri;
            UpdateDownloadButton.Visibility = release == null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (TimeoutException) { UpdateStatus.Text = "检查超时，请稍后重试。"; }
        catch (HttpRequestException) { UpdateStatus.Text = "无法连接更新服务，请检查网络后重试。"; }
        catch (Exception) { UpdateStatus.Text = "检查更新失败，请稍后重试。"; }
        finally
        {
            updateCancellation = null;
            if (!cancellation.IsCancellationRequested)
            {
                CheckUpdateButton.IsEnabled = true;
                CheckUpdateButton.Content = "检查更新";
            }
        }
    }

    private void DownloadUpdateClick(object sender, RoutedEventArgs e)
    {
        if (updatePage == null) return;
        try { using var process = Process.Start(new ProcessStartInfo { FileName = updatePage.AbsoluteUri, UseShellExecute = true }); }
        catch (Exception) { UpdateStatus.Text = "无法打开浏览器，请稍后重试。"; }
    }

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        mcp.Changed -= RefreshMcpControls;
        updateCancellation?.Cancel();
        base.OnClosed(e);
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
