using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BnetManager.Controls;
using BnetManager.Core;

namespace BnetManager;

public partial class MainWindow : ModernWindow
{
    private readonly AccountManager manager;
    private readonly DispatcherTimer clientTimer;
    private bool busy;

    public static readonly DependencyProperty IsClientRunningProperty = DependencyProperty.Register(
        nameof(IsClientRunning), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public bool IsClientRunning
    {
        get => (bool)GetValue(IsClientRunningProperty);
        private set => SetValue(IsClientRunningProperty, value);
    }

    public MainWindow()
    {
        InitializeComponent();
        manager = new();
        RefreshView();
        if (manager.Catalog.LauncherPath == null)
            SetStatus("请在客户端设置中选择战网启动程序。");
        clientTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        clientTimer.Tick += (_, _) => RefreshClientState();
        clientTimer.Start();
        Loaded += WindowLoaded;
    }

    private void RefreshView()
    {
        var selectedId = (AccountsList.SelectedItem as AccountProfile)?.Id;
        var accounts = manager.Catalog.Accounts.OrderBy(a => a.Region).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        AccountsList.ItemsSource = accounts;
        AccountsList.SelectedItem = accounts.FirstOrDefault(a => a.Id == selectedId);
        AccountCount.Text = accounts.Count.ToString();
        EmptyState.Visibility = accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AccountsList.IsEnabled = !busy && !manager.NeedsRecovery;
        LoginActions.IsEnabled = !busy && !manager.NeedsRecovery;
        EmptyState.IsEnabled = !busy && !manager.NeedsRecovery;
        SettingsButton.IsEnabled = !busy;
        RefreshClientState();
    }

    private void RefreshClientState()
    {
        IsClientRunning = manager.Client.IsRunning;
        ClientState.Text = IsClientRunning ? "战网运行中" : "战网未运行";
        ClientDot.Fill = IsClientRunning ? new SolidColorBrush(Color.FromRgb(43, 139, 95)) : new SolidColorBrush(Color.FromRgb(120, 133, 154));
    }

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = error ? (Brush)FindResource("DangerInk") : (Brush)FindResource("Muted");
    }

    private async Task PerformAsync(Func<IProgress<string>, Task> action, string success)
    {
        if (busy) return;
        busy = true;
        RefreshView();
        OperationProgress.Visibility = Visibility.Visible;
        var progress = new Progress<string>(message => SetStatus(message));
        try
        {
            await Task.Run(() => action(progress));
            SetStatus(success);
        }
        catch (Exception error)
        {
            SetStatus(error.Message, error: true);
            MessageDialog.Show(this, "操作未完成", error.Message);
        }
        finally
        {
            busy = false;
            OperationProgress.Visibility = Visibility.Collapsed;
            RefreshView();
        }
    }

    private void UiAction(Action action)
    {
        try { action(); }
        catch (Exception error) { SetStatus(error.Message, error: true); MessageDialog.Show(this, "操作未完成", error.Message); }
    }

    private static void OpenMenu(Button button)
    {
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void AccountMenuClick(object sender, RoutedEventArgs e) => OpenMenu((Button)sender);

    private async void SaveCurrentClick(object sender, RoutedEventArgs e)
    {
        if (busy || manager.NeedsRecovery) return;
        AccountDialog? dialog = null;
        UiAction(() =>
        {
            if (!File.Exists(BattleConfiguration.ConfigPath)) throw new InvalidOperationException("请先登录战网，再保存当前账号。");
            var launcher = BattleNetProcess.ValidateLauncher(manager.Catalog.LauncherPath);
            var info = BattleConfiguration.ReadLogin(File.ReadAllBytes(BattleConfiguration.ConfigPath), launcher);
            dialog = new AccountDialog(AccountDialogMode.Capture, info.Region) { Owner = this };
        });
        if (dialog?.ShowDialog() != true) return;
        var name = dialog.AccountName;
        var battleTag = dialog.BattleTag;
        var region = dialog.Region;
        await PerformAsync(p => manager.SaveCurrentAsync(name, battleTag, "", region, null, p), "账号已保存。");
    }

    private async void NewLoginClick(object sender, RoutedEventArgs e)
    {
        if (busy || manager.NeedsRecovery) return;
        var dialog = new AccountDialog(AccountDialogMode.Login, BattleRegion.China) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var region = dialog.Region;
        await PerformAsync(p => manager.LoginNewAsync(region, p), $"已打开{Regions.Get(region).Name}登录窗口。登录后保存账号。");
    }

    private async void SwitchClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AccountProfile profile }) await SwitchProfileAsync(profile);
    }

    private async void AccountKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is Button || AccountsList.SelectedItem is not AccountProfile profile) return;
        e.Handled = true;
        await SwitchProfileAsync(profile);
    }

    private Task SwitchProfileAsync(AccountProfile profile)
    {
        if (busy || manager.NeedsRecovery) return Task.CompletedTask;
        return PerformAsync(p => manager.SwitchAsync(profile.Id, p), $"已启动「{profile.Name}」 · {profile.RegionName}。");
    }

    private async void RefreshSessionClick(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not FrameworkElement { DataContext: AccountProfile profile }) return;
        await PerformAsync(p => manager.SaveCurrentAsync(profile.Name, profile.BattleTag, profile.Group, profile.Region, profile.Id, p), "登录状态已更新。");
    }

    private void EditClick(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not FrameworkElement { DataContext: AccountProfile profile }) return;
        var dialog = new AccountDialog(AccountDialogMode.Edit, profile.Region, profile) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        UiAction(() =>
        {
            manager.UpdateProfile(profile.Id, dialog.AccountName, dialog.BattleTag, profile.Group);
            RefreshView();
            SetStatus("账号信息已保存。");
        });
    }

    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not FrameworkElement { DataContext: AccountProfile profile }) return;
        if (!MessageDialog.Show(this, "移除账号", $"移除「{profile.Name}」的本地记录和登录状态？", "移除", confirm: true, destructive: true)) return;
        UiAction(() => { manager.Delete(profile.Id); RefreshView(); SetStatus("账号记录已移除。"); });
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (manager.NeedsRecovery)
            await PerformAsync(p => manager.RepairInterruptedSwitchAsync(p), "");
    }

    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(manager.Catalog.LauncherPath, manager.Store.Root) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        UiAction(() => { manager.SetLauncher(dialog.LauncherPath); SetStatus("战网客户端路径已保存。"); });
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; SetStatus("正在处理登录状态，请稍候。"); return; }
        clientTimer?.Stop();
    }
}
