using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using SyncStick.App.Services;
using SyncStick.App.ViewModels;
using SyncStick.Core.Services;
using SyncStick.Engine;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace SyncStick.App;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateEvent;
    private bool _ownsMutex;
    private bool _isExiting;
    private bool _cleanedUp;
    private bool _windowHiddenNotified;
    private bool _showingUiErrorDialog;
    private SyncCoordinator? _coordinator;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ToolStripMenuItem? _autoSyncMenuItem;
    private WinForms.ToolStripMenuItem? _syncNowMenuItem;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!TryAcquireSingleInstance())
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();
        SessionEnding += (_, _) => Cleanup();

        try
        {
            var settingsStore = new SettingsStore();
            var activityStore = new ActivityStore();
            _coordinator = new SyncCoordinator(settingsStore, activityStore, new WindowsDeviceService());
            _viewModel = new MainViewModel(_coordinator);
            _mainWindow = new MainWindow
            {
                DataContext = _viewModel
            };
            MainWindow = _mainWindow;

            _coordinator.Notice += OnCoordinatorNotice;
            _coordinator.StateChanged += OnCoordinatorStateChanged;

            var startMinimized = e.Args.Any(argument =>
                argument.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            var trayReady = false;
            if (!startMinimized)
            {
                trayReady = CreateTrayIcon();
            }

            _coordinator.Start();
            UpdateTrayState();

            if (startMinimized)
            {
                // Auto-start can run before Explorer has created the notification
                // area.  Wait for the shell before registering the icon, otherwise
                // the app keeps syncing in the background with no visible icon.
                _ = CreateTrayIconWhenShellIsReadyAsync();
            }
            else
            {
                ShowMainWindow();
                if (!trayReady)
                {
                    ShowTrayUnavailableNotice(
                        "系统托盘不可用，已打开主窗口。你仍然可以正常同步，但请保持窗口打开。");
                }
            }
        }
        catch (Exception ex)
        {
            LogException(ex);
            MessageBox.Show(
                "SyncStick 启动失败。\n\n" + ex.Message,
                "SyncStick",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    public void HandleMainWindowClosing(Window window, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        if (_coordinator?.Settings.MinimizeToTray == true && _trayIcon is not null)
        {
            e.Cancel = true;
            window.Hide();
            if (!_windowHiddenNotified)
            {
                _windowHiddenNotified = true;
                ShowBalloon("SyncStick 仍在后台监听 U 盘。图标可能在任务栏右下角的 ^ 里；右键可退出。");
            }

            return;
        }

        e.Cancel = true;
        Dispatcher.BeginInvoke(RequestExit, DispatcherPriority.Background);
    }

    private void RequestExit()
    {
        if (_isExiting)
        {
            return;
        }

        if (_coordinator?.IsSyncing == true)
        {
            var confirm = DialogService.Confirm(
                "同步还在进行",
                "现在退出会中断正在写入的文件。未完成的临时文件会被清理，但 U 盘可能留下不完整的新版本。\n\n确定要退出吗？",
                "仍然退出",
                danger: true);
            if (!confirm)
            {
                return;
            }
        }

        ExitApplication();
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        Cleanup();
        Shutdown();
    }

    private void Cleanup()
    {
        if (_cleanedUp)
        {
            return;
        }

        _cleanedUp = true;
        _isExiting = true;

        try
        {
            _trayIcon?.Dispose();
        }
        catch (Exception)
        {
        }

        _trayIcon = null;

        try
        {
            _coordinator?.Dispose();
        }
        catch (Exception)
        {
        }

        _coordinator = null;

        try
        {
            _activateEvent?.Set();
            _activateEvent?.Dispose();
        }
        catch (Exception)
        {
        }

        _activateEvent = null;

        if (_ownsMutex)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was already released.
            }
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
    }

    private bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\SyncStick.SingleInstance", out var createdNew);
            if (!createdNew)
            {
                try
                {
                    using var existing = EventWaitHandle.OpenExisting(@"Local\SyncStick.Activate");
                    existing.Set();
                }
                catch (Exception)
                {
                }

                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                return false;
            }

            _ownsMutex = true;
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SyncStick.Activate");
            _ = Task.Run(ActivationListener);
            return true;
        }
        catch (Exception ex)
        {
            LogException(ex);
            return true;
        }
    }

    private void ActivationListener()
    {
        while (!_isExiting)
        {
            try
            {
                if (_activateEvent?.WaitOne() == true)
                {
                    Dispatcher.BeginInvoke(ShowMainWindow);
                }
                else
                {
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    private bool CreateTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return true;
        }

        WinForms.NotifyIcon? tray = null;
        try
        {
            tray = new WinForms.NotifyIcon
            {
                Text = "SyncStick · U 盘自动同步"
            };

            // The icon must be present before Visible is set: NotifyIcon adds
            // the shell icon as soon as Visible becomes true, and an add with
            // a null icon can be dropped without an exception.
            tray.Icon = LoadTrayIcon();

            var menu = new WinForms.ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = true,
                Font = new Drawing.Font("Microsoft YaHei UI", 9f),
                BackColor = Drawing.Color.FromArgb(252, 251, 246),
                Renderer = new WinForms.ToolStripProfessionalRenderer(new TrayColorTable())
            };

            var openItem = new WinForms.ToolStripMenuItem("打开 SyncStick") { Font = new Drawing.Font("Microsoft YaHei UI", 9f, Drawing.FontStyle.Bold) };
            openItem.Click += (_, _) => ShowMainWindow();

            _syncNowMenuItem = new WinForms.ToolStripMenuItem("立即同步");
            _syncNowMenuItem.Click += OnTraySyncNow;

            _autoSyncMenuItem = new WinForms.ToolStripMenuItem("插入 U 盘后自动同步");
            _autoSyncMenuItem.Click += (_, _) => _viewModel?.ToggleAutoSyncFromTray();

            var exitItem = new WinForms.ToolStripMenuItem("退出");
            exitItem.Click += (_, _) => RequestExit();

            menu.Items.Add(openItem);
            menu.Items.Add(_syncNowMenuItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(_autoSyncMenuItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (_, _) => ShowMainWindow();

            _trayIcon = tray;
            tray.Visible = true;
            return true;
        }
        catch (Exception ex)
        {
            LogException(ex);

            try
            {
                tray?.Dispose();
            }
            catch (Exception)
            {
            }

            if (ReferenceEquals(_trayIcon, tray))
            {
                _trayIcon = null;
            }

            return false;
        }
    }

    private Drawing.Icon LoadTrayIcon()
    {
        try
        {
            using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/syncstick.ico"))?.Stream;
            if (stream is not null)
            {
                using var icon = new Drawing.Icon(stream);
                return (Drawing.Icon)icon.Clone();
            }
        }
        catch (Exception ex)
        {
            LogException(ex);
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    private async Task CreateTrayIconWhenShellIsReadyAsync()
    {
        try
        {
            // At logon the app can start several seconds before the
            // notification area exists.  Registering early leaves the process
            // running with no tray icon forever, so wait for the shell first.
            for (var attempt = 0; attempt < 120; attempt++)
            {
                if (_isExiting)
                {
                    return;
                }

                if (IsShellTrayReady())
                {
                    break;
                }

                await Task.Delay(250);
            }

            if (_isExiting)
            {
                return;
            }

            if (!IsShellTrayReady() || !CreateTrayIcon())
            {
                ShowTrayUnavailableNotice(
                    "Windows 任务栏还没有准备好，已打开主窗口。请保持窗口打开；托盘图标出现后可以关闭窗口。");
                return;
            }

            UpdateTrayState();
            ShowBalloon("SyncStick 已在后台运行。如果看不到图标，请点任务栏右下角的 ^ 展开隐藏图标。");
        }
        catch (Exception ex)
        {
            LogException(ex);
            ShowTrayUnavailableNotice(
                "系统托盘初始化失败，已打开主窗口。你仍然可以正常同步，但请保持窗口打开。");
        }
    }

    private void ShowTrayUnavailableNotice(string message)
    {
        ShowMainWindow();
        try
        {
            _viewModel?.ShowToast(message, isError: false);
        }
        catch (Exception)
        {
            // The window is already open; the guidance is best-effort.
        }
    }

    private static bool IsShellTrayReady()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
        {
            return false;
        }

        return FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null) != IntPtr.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);


    private async void OnTraySyncNow(object? sender, EventArgs e)
    {
        if (_viewModel is null || _coordinator is null)
        {
            return;
        }

        if (_coordinator.IsSyncing || !_coordinator.TargetConnected || !_viewModel.HasSources)
        {
            ShowMainWindow();
            _viewModel.ShowToast(
                _coordinator.IsSyncing
                    ? "正在同步，请稍候。"
                    : !_coordinator.TargetConnected
                        ? "目标 U 盘还未连接。"
                        : "还没有选择要同步的内容。",
                isError: false);
            return;
        }

        try
        {
            await _viewModel.SyncNowFromTrayAsync();
        }
        catch (Exception ex)
        {
            ShowBalloon("同步失败：" + ex.Message, isError: true);
        }
    }

    private void ShowMainWindow()
    {
        if (_isExiting || _mainWindow is null)
        {
            return;
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
        _windowHiddenNotified = false;
    }

    private void OnCoordinatorNotice(object? sender, NoticeEventArgs e)
    {
        if (_coordinator is null)
        {
            return;
        }

        if (e.IsError || _coordinator.Settings.NotifyOnComplete)
        {
            ShowBalloon(e.Message, e.IsError);
        }
    }

    private void OnCoordinatorStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            UpdateTrayState();
        }
        else
        {
            Dispatcher.BeginInvoke(UpdateTrayState, DispatcherPriority.Background);
        }
    }

    private void UpdateTrayState()
    {
        if (_trayIcon is null || _coordinator is null)
        {
            return;
        }

        _autoSyncMenuItem?.Checked = _coordinator.Settings.AutoSyncEnabled;
        if (_syncNowMenuItem is not null)
        {
            _syncNowMenuItem.Enabled = _coordinator.TargetConnected
                                       && _coordinator.EnabledSourceCount > 0
                                       && !_coordinator.IsSyncing;
        }

        var text = _coordinator.IsSyncing
            ? "SyncStick · 正在同步"
            : _coordinator.TargetMatch.Device is not null
                ? $"SyncStick · {_coordinator.TargetMatch.Device.DriveLetter} 已连接"
                : _coordinator.Settings.TargetDevice is not null
                    ? "SyncStick · 等待目标 U 盘"
                    : "SyncStick · 尚未设置目标";
        _trayIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    private void ShowBalloon(string message, bool isError = false)
    {
        if (_trayIcon is null)
        {
            return;
        }

        try
        {
            _trayIcon.BalloonTipTitle = isError ? "SyncStick · 需要注意" : "SyncStick";
            _trayIcon.BalloonTipText = message;
            _trayIcon.BalloonTipIcon = isError ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info;
            _trayIcon.ShowBalloonTip(4200);
        }
        catch (Exception)
        {
            // Notifications are best-effort.
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
        e.Handled = true;

        if (_showingUiErrorDialog)
        {
            return;
        }

        _showingUiErrorDialog = true;
        try
        {
            MessageBox.Show(
                "SyncStick 遇到一个未处理的界面错误，但会继续运行。\n\n" + e.Exception.Message,
                "SyncStick",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _showingUiErrorDialog = false;
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogException(exception);
        }
    }

    public static void LogException(Exception exception)
    {
        try
        {
            var directory = Path.Combine(SettingsStore.AppDataDirectory, "logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"error-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(
                path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Logging must never throw.
        }
    }

    private sealed class TrayColorTable : WinForms.ProfessionalColorTable
    {
        private static readonly Drawing.Color Cream = Drawing.Color.FromArgb(252, 251, 246);
        private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(234, 229, 217);
        private static readonly Drawing.Color Ink = Drawing.Color.FromArgb(23, 33, 28);

        public override Drawing.Color ToolStripDropDownBackground => Cream;

        public override Drawing.Color ImageMarginGradientBegin => Cream;

        public override Drawing.Color ImageMarginGradientMiddle => Cream;

        public override Drawing.Color ImageMarginGradientEnd => Cream;

        public override Drawing.Color MenuBorder => Drawing.Color.FromArgb(222, 215, 199);

        public override Drawing.Color MenuItemBorder => Hover;

        public override Drawing.Color MenuItemSelected => Hover;

        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;

        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;

        public override Drawing.Color SeparatorDark => Drawing.Color.FromArgb(222, 215, 199);

        public override Drawing.Color SeparatorLight => Cream;

        public override Drawing.Color MenuItemPressedGradientBegin => Hover;

        public override Drawing.Color MenuItemPressedGradientEnd => Hover;

        public override Drawing.Color CheckBackground => Cream;

        public override Drawing.Color CheckSelectedBackground => Hover;
    }
}
