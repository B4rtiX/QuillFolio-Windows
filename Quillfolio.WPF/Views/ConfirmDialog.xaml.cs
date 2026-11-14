using System.Windows;
using System.Windows.Media;
using Quillfolio.Core.Services;
using Quillfolio.WPF.Services;

namespace Quillfolio.WPF.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmLabel = "OK", string? cancelLabel = null, bool isDestructive = false)
    {
        InitializeComponent();
        UIAnimationHelper.AttachWindowEntranceAnimation(this);

        TxtTitle.Text = title;
        TxtMessage.Text = message;
        BtnConfirm.Content = confirmLabel;

        string? resolvedCancel = cancelLabel ?? LocalizationService.Instance.Get("Cancel");
        if (string.IsNullOrEmpty(resolvedCancel))
        {
            BtnCancel.Visibility = Visibility.Collapsed;
        }
        else
        {
            BtnCancel.Content = resolvedCancel;
        }

        if (isDestructive)
        {
            IconBadge.Background = new SolidColorBrush(Color.FromArgb(32, 220, 38, 38));
            IconBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 220, 38, 38));
            IconPath.Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            BtnConfirm.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            BtnConfirm.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }
        else
        {
            IconBadge.Background = new SolidColorBrush(Color.FromArgb(28, 124, 58, 237));
            IconBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 124, 58, 237));
            IconPath.Fill = new SolidColorBrush(Color.FromRgb(124, 58, 237));
            IconPath.Data = Geometry.Parse("M11,9H13V7H11M12,20C7.59,20 4,16.41 4,12C4,7.59 7.59,4 12,4C16.41,4 20,7.59 20,12C20,16.41 16.41,20 12,20M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M11,17H13V11H11V17Z");
        }
    }

    public static bool ShowConfirm(Window? owner, string title, string message, string? confirmLabel = null, string? cancelLabel = null, bool isDestructive = true)
    {
        var loc = LocalizationService.Instance;
        var dlg = new ConfirmDialog(
            title,
            message,
            confirmLabel ?? loc.Get("Confirm"),
            cancelLabel ?? loc.Get("Cancel"),
            isDestructive)
        {
            Owner = owner
        };
        return dlg.ShowDialog() == true;
    }

    public static void ShowInfo(Window? owner, string title, string message, string okLabel = "OK")
    {
        var dlg = new ConfirmDialog(title, message, okLabel, string.Empty, isDestructive: false)
        {
            Owner = owner
        };
        dlg.ShowDialog();
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
