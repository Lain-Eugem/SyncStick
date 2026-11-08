using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SyncStick.App.Views.Dialogs;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(
        string title,
        string message,
        string confirmText,
        string cancelText,
        bool danger)
    {
        InitializeComponent();
        ApplyContent(title, message, confirmText);

        if (danger)
        {
            ConfirmButton.Style = (Style)FindResource("DangerButton");
        }

        WireDrag();
    }

    public ConfirmDialog(string title, string message, string confirmText)
    {
        InitializeComponent();
        ApplyContent(title, message, confirmText);

        KickerText.Text = "SYNCSTICK / NOTICE";
        CancelButton.Visibility = Visibility.Collapsed;
        DialogIconBorder.Background = (Brush)FindResource("AccentSoftBrush");
        DialogIconPath.Data = (Geometry)FindResource("Icon.Info");
        DialogIconPath.Stroke = (Brush)FindResource("AccentDeepBrush");

        WireDrag();
    }

    private void ApplyContent(string title, string message, string confirmText)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    private void WireDrag()
    {
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
