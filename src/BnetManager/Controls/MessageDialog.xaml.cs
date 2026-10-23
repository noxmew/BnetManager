using System.Windows;

namespace BnetManager.Controls;

public partial class MessageDialog : ModernWindow
{
    public MessageDialog() => InitializeComponent();

    public static bool Show(Window? owner, string title, string message, string acceptText = "确定", bool confirm = false, bool destructive = false)
    {
        var dialog = new MessageDialog { Title = title };
        if (owner?.IsVisible == true) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.MessageText.Text = message;
        dialog.AcceptButton.Content = acceptText;
        if (confirm) dialog.CancelButton.Visibility = Visibility.Visible;
        if (destructive) dialog.AcceptButton.Style = (Style)dialog.FindResource("Danger");
        if (confirm) dialog.Loaded += (_, _) => dialog.CancelButton.Focus();
        return dialog.ShowDialog() == true;
    }

    private void AcceptClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
