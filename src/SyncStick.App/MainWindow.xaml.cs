using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SyncStick.App.ViewModels;

namespace SyncStick.App;

public partial class MainWindow : Window
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmRoundCorner = 2;

    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        StateChanged += OnWindowStateChanged;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPageKey))
        {
            AnimatePage();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsToastVisible))
        {
            AnimateToast(_viewModel?.IsToastVisible == true);
        }
    }

    private void AnimatePage()
    {
        if (!IsLoaded)
        {
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });

        if (PageHost.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        }
    }

    private void AnimateToast(bool show)
    {
        var duration = TimeSpan.FromMilliseconds(show ? 190 : 140);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ToastHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(ToastHost.Opacity, show ? 1 : 0, duration) { EasingFunction = ease });

        if (ToastHost.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(transform.Y, show ? 0 : 8, duration) { EasingFunction = ease });
        }
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove throws if the mouse button was released before the call.
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Data = (Geometry)FindResource(maximized ? "Icon.Restore" : "Icon.Maximize");
        RootFrame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var preference = DwmRoundCorner;
            _ = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref preference, sizeof(int));
        }
        catch (Exception)
        {
            // Rounded corners are a Windows 11 nicety; older versions can ignore it.
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.HandleMainWindowClosing(this, e);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
