using System.Windows;
using SyncStick.App.Views.Dialogs;

namespace SyncStick.App.Services;

public static class DialogService
{
    public static bool Confirm(
        string title,
        string message,
        string confirmText = "确定",
        bool danger = false,
        string cancelText = "取消")
    {
        var dialog = new ConfirmDialog(title, message, confirmText, cancelText, danger);
        var owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true;
    }

    public static void Info(string title, string message, string confirmText = "知道了")
    {
        var dialog = new ConfirmDialog(title, message, confirmText);
        var owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
    }
}
