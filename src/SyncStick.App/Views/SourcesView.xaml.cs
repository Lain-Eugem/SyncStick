using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SyncStick.App.ViewModels;

namespace SyncStick.App.Views;

public partial class SourcesView : UserControl
{
    public SourcesView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        UpdateDragState(e);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        UpdateDragState(e);
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (ViewModel is { } vm && !IsMouseOver)
        {
            vm.SetDropActive(false);
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        vm.SetDropActive(false);
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            vm.AddDroppedPaths(paths);
        }

        e.Handled = true;
    }

    private void UpdateDragState(DragEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            vm.SetDropActive(true);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            vm.SetDropActive(false);
        }

        e.Handled = true;
    }
}
