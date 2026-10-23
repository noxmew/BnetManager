using System.Windows;
using BnetManager.Core;

namespace BnetManager.Controls;

public enum AccountDialogMode { Capture, Edit, Login }

public partial class AccountDialog : ModernWindow
{
    private readonly AccountDialogMode mode;
    public string AccountName => NameInput.Text.Trim();
    public string BattleTag => TagInput.Text.Trim();
    public BattleRegion Region => (BattleRegion)RegionPicker.SelectedValue;

    public AccountDialog(AccountDialogMode mode, BattleRegion initialRegion, AccountProfile? profile = null)
    {
        InitializeComponent();
        this.mode = mode;
        RegionPicker.ItemsSource = Regions.All;
        RegionPicker.SelectedValue = initialRegion;
        if (mode != AccountDialogMode.Login)
        {
            RegionPicker.Visibility = Visibility.Collapsed;
            ReadOnlyRegion.Visibility = Visibility.Visible;
            var region = Regions.Get(initialRegion);
            RegionDisplay.Text = $"{region.Code} · {region.Name}";
        }
        if (profile != null)
        {
            NameInput.Text = profile.Name;
            TagInput.Text = profile.BattleTag;
        }
        switch (mode)
        {
            case AccountDialogMode.Capture:
                Title = "保存当前账号";
                Description.Text = "请先勾选「保持登录」。保存时会暂时关闭战网。";
                RegionPicker.IsEnabled = false;
                break;
            case AccountDialogMode.Edit:
                Title = "编辑账号信息";
                Description.Visibility = Visibility.Collapsed;
                RegionPicker.IsEnabled = false;
                AcceptButton.Content = "保存修改";
                break;
            case AccountDialogMode.Login:
                Title = "登录新账号";
                Description.Text = "完成战网登录后，点击「保存当前账号」。";
                Details.Visibility = Visibility.Collapsed;
                AcceptButton.Content = "打开战网";
                break;
        }
        Loaded += (_, _) => { if (mode != AccountDialogMode.Login) NameInput.Focus(); };
    }

    private void AcceptClick(object sender, RoutedEventArgs e)
    {
        if (RegionPicker.SelectedItem == null) { Validation.Text = "请选择一个区服。"; return; }
        if (mode != AccountDialogMode.Login && string.IsNullOrWhiteSpace(AccountName))
        {
            Validation.Text = "请输入账号备注。";
            NameInput.Focus();
            return;
        }
        DialogResult = true;
    }
}
