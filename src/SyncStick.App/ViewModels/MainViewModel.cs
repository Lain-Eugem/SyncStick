using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using SyncStick.App.Controls;
using SyncStick.App.Infrastructure;
using SyncStick.App.Services;
using SyncStick.Core;
using SyncStick.Core.Models;
using SyncStick.Engine;

namespace SyncStick.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SyncCoordinator _coordinator;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _toastTimer;

    private string _currentPageKey = "dashboard";
    private bool _showFixedDrives;
    private bool _isDropActive;
    private bool _isToastVisible;
    private bool _isToastError;
    private string _toastMessage = string.Empty;
    private DateTimeOffset? _sessionCompletedAt;

    public MainViewModel(SyncCoordinator coordinator)
    {
        _coordinator = coordinator;
        _dispatcher = Application.Current.Dispatcher;

        NavItems = new ObservableCollection<NavItemViewModel>
        {
            new("dashboard", "同步总览", "Icon.Dashboard"),
            new("sources", "同步内容", "Icon.Folder"),
            new("devices", "目标设备", "Icon.Usb"),
            new("activity", "活动记录", "Icon.Clock"),
            new("settings", "设置", "Icon.Settings")
        };

        NavigateCommand = new RelayCommand(parameter => Navigate(parameter as string));
        AddFolderCommand = new RelayCommand(AddFolder);
        AddFilesCommand = new RelayCommand(AddFiles);
        PrimaryActionCommand = new AsyncRelayCommand(PrimaryActionAsync, () => true, ex => ShowToast("操作失败：" + ex.Message, true));
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        ClearTargetCommand = new RelayCommand(ClearTarget);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        ClearActivitiesCommand = new RelayCommand(ClearActivities);

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _coordinator.StateChanged += (_, _) => OnUi(RefreshFromCoordinator);
        _coordinator.ProgressChanged += (_, progress) => OnUi(() =>
        {
            _ = progress;
            RefreshStatus();
        });
        _coordinator.SyncFinished += (_, record) => OnUi(() =>
        {
            _sessionCompletedAt = record.Success ? DateTimeOffset.Now : null;
            RefreshAll();
        });
        _coordinator.Notice += (_, args) => OnUi(() => ShowToast(args.Message, args.IsError));

        RefreshAll();
        SetPageSelection("dashboard");
    }

    public ObservableCollection<NavItemViewModel> NavItems { get; }

    public ObservableCollection<SourceRowViewModel> Sources { get; } = new();

    public ObservableCollection<VolumeRowViewModel> RemovableDevices { get; } = new();

    public ObservableCollection<VolumeRowViewModel> FixedDevices { get; } = new();

    public ObservableCollection<ActivityRowViewModel> Activities { get; } = new();

    public ObservableCollection<ActivityRowViewModel> RecentActivities { get; } = new();

    public RelayCommand NavigateCommand { get; }

    public RelayCommand AddFolderCommand { get; }

    public RelayCommand AddFilesCommand { get; }

    public AsyncRelayCommand PrimaryActionCommand { get; }

    public RelayCommand RefreshDevicesCommand { get; }

    public RelayCommand ClearTargetCommand { get; }

    public RelayCommand OpenDataFolderCommand { get; }

    public RelayCommand ClearActivitiesCommand { get; }

    private AppSettings Settings => _coordinator.Settings;

    private VolumeInfo? ConnectedTarget => _coordinator.TargetMatch.Device;

    // ---------------------------------------------------------------- pages

    public string CurrentPageKey
    {
        get => _currentPageKey;
        private set
        {
            if (!SetProperty(ref _currentPageKey, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsDashboardPage));
            OnPropertyChanged(nameof(IsSourcesPage));
            OnPropertyChanged(nameof(IsDevicesPage));
            OnPropertyChanged(nameof(IsActivityPage));
            OnPropertyChanged(nameof(IsSettingsPage));
        }
    }

    public bool IsDashboardPage => CurrentPageKey == "dashboard";

    public bool IsSourcesPage => CurrentPageKey == "sources";

    public bool IsDevicesPage => CurrentPageKey == "devices";

    public bool IsActivityPage => CurrentPageKey == "activity";

    public bool IsSettingsPage => CurrentPageKey == "settings";

    public string TodayText => DateTime.Now.ToString("M 月 d 日 · dddd");

    // --------------------------------------------------------------- toasts

    public string ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public bool IsToastError
    {
        get => _isToastError;
        private set
        {
            if (SetProperty(ref _isToastError, value))
            {
                OnPropertyChanged(nameof(ToastIconGlyph));
            }
        }
    }

    public string ToastIconGlyph => IsToastError ? "!" : "✓";

    public void ShowToast(string message, bool isError = false)
    {
        ToastMessage = message;
        IsToastError = isError;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Interval = isError ? TimeSpan.FromSeconds(6.2) : TimeSpan.FromSeconds(4.2);
        _toastTimer.Start();
    }

    // ------------------------------------------------------------- dashboard

    public bool HasTarget => Settings.TargetDevice is not null;

    public bool TargetConnected => ConnectedTarget is not null;

    public bool TargetAmbiguous => _coordinator.TargetMatch.IsAmbiguous;

    public bool HasSources => Settings.Sources.Any(source => source.Enabled);

    public bool HasAnySources => Settings.Sources.Count > 0;

    public int SourceCount => Settings.Sources.Count;

    public int EnabledSourceCount => Settings.Sources.Count(source => source.Enabled);

    public string SourceCountText => EnabledSourceCount == 1 ? "1 个同步项" : $"{EnabledSourceCount} 个同步项";

    public string SourceNamesText
    {
        get
        {
            var names = Settings.Sources.Select(source => source.Name).ToList();
            if (names.Count == 0)
            {
                return "文件或文件夹";
            }

            var head = string.Join("、", names.Take(2));
            return names.Count > 2 ? $"{head} 等 {names.Count} 项" : head;
        }
    }

    public string TargetLabel => ConnectedTarget?.DisplayLabel ?? Settings.TargetDevice?.DisplayLabel ?? "目标 U 盘";

    public string TargetLetter => ConnectedTarget?.DriveLetter ?? Settings.TargetDevice?.LastDriveLetter ?? "U 盘";

    public string TargetDriveHint => TargetConnected || HasTarget ? TargetLetter : "U 盘";

    public string TargetFreeText => ConnectedTarget is null
        ? "未连接"
        : $"剩余 {ByteSize.Format(ConnectedTarget.FreeSpaceBytes)}";

    public bool IsSyncing => _coordinator.IsSyncing;

    public SyncProgress? CurrentProgress => _coordinator.Progress;

    public double ProgressPercent => IsSyncing
        ? CurrentProgress?.Percent ?? 0
        : TargetConnected ? 100 : 0;

    public string ProgressPercentText => $"{ProgressPercent:0}%";

    public string CurrentFileText
    {
        get
        {
            if (!IsSyncing)
            {
                return string.Empty;
            }

            var progress = CurrentProgress;
            return progress?.Stage == SyncStage.Preparing
                ? "正在扫描来源，准备同步计划…"
                : progress?.CurrentFile ?? "准备写入…";
        }
    }

    public string ProgressCounterText
    {
        get
        {
            var progress = CurrentProgress;
            if (progress is null || progress.TotalFiles == 0)
            {
                return IsSyncing ? "准备中" : "—";
            }

            return $"{progress.FilesDone} / {progress.TotalFiles} 个文件";
        }
    }

    public string ProgressBytesText
    {
        get
        {
            var progress = CurrentProgress;
            if (progress is null || progress.BytesTotal <= 0)
            {
                return IsSyncing ? "计算体积中" : "—";
            }

            return $"{ByteSize.Format(progress.BytesDone)} / {ByteSize.Format(progress.BytesTotal)}";
        }
    }

    public string SyncStageText => CurrentProgress?.Stage switch
    {
        SyncStage.Preparing => "扫描中",
        SyncStage.Copying => "写入中",
        SyncStage.Cleaning => "清理中",
        SyncStage.Completed => "已完成",
        _ => IsSyncing ? "同步中" : "待机"
    };

    public bool IsSessionComplete => _sessionCompletedAt.HasValue
                                     && DateTimeOffset.Now - _sessionCompletedAt.Value < TimeSpan.FromMinutes(10)
                                     && TargetConnected
                                     && !IsSyncing;

    public ProgressRingState RingState
    {
        get
        {
            if (IsSyncing)
            {
                return ProgressRingState.Busy;
            }

            if (TargetAmbiguous)
            {
                return ProgressRingState.Error;
            }

            if (TargetConnected && IsSessionComplete)
            {
                return ProgressRingState.Done;
            }

            return TargetConnected ? ProgressRingState.Ready : ProgressRingState.Idle;
        }
    }

    public string RingTitle => RingState switch
    {
        ProgressRingState.Busy => $"{ProgressPercent:0}%",
        ProgressRingState.Done => "✓",
        ProgressRingState.Error => "!",
        ProgressRingState.Ready => TargetLetter,
        _ => "USB"
    };

    public string RingSubtitle => RingState switch
    {
        ProgressRingState.Busy => SyncStageText,
        ProgressRingState.Done => "已完成",
        ProgressRingState.Error => "需检查",
        ProgressRingState.Ready => "已连接",
        _ => "等待插入"
    };

    public string StatusKicker => IsSyncing
        ? "WRITING TO DEVICE"
        : TargetAmbiguous
            ? "DEVICE CONFLICT"
            : !HasTarget
                ? "SETUP NEEDED"
                : !TargetConnected
                    ? "WAITING FOR DEVICE"
                    : !HasSources
                        ? "DEVICE READY"
                        : IsSessionComplete
                            ? "SYNC COMPLETE"
                            : "DEVICE READY";

    public string Headline
    {
        get
        {
            if (IsSyncing)
            {
                return $"正在写入 {TargetLetter}";
            }

            if (TargetAmbiguous)
            {
                return "设备需要确认";
            }

            if (!HasTarget)
            {
                return "先选一块 U 盘";
            }

            if (!TargetConnected)
            {
                return "等待 U 盘插入";
            }

            if (!HasSources)
            {
                return "还差同步内容";
            }

            if (IsSessionComplete)
            {
                return "这次已经写好了";
            }

            return $"{TargetLetter} 已就绪";
        }
    }

    public string StatusBody
    {
        get
        {
            if (IsSyncing)
            {
                return CurrentFileText;
            }

            if (TargetAmbiguous)
            {
                return "同时检测到多块相似的设备，无法确定该写哪一块。请到“目标设备”重新选择。";
            }

            if (!HasTarget)
            {
                return "指定要跟随的项目和一块外部存储设备。之后每次插入，SyncStick 都会自动把变化写进去。";
            }

            if (!TargetConnected)
            {
                return AutoSyncEnabled
                    ? $"已记住 {TargetLabel}。插入后会先比较文件，再覆盖有变化的内容。"
                    : "自动同步已暂停。插入 U 盘后，可以在总览中手动开始同步。";
            }

            if (!HasSources)
            {
                return "U 盘连接正常。先添加文件或文件夹，SyncStick 才知道要写什么。";
            }

            if (IsSessionComplete)
            {
                var record = _coordinator.LastRecord;
                var detail = record is null
                    ? "文件已是最新。"
                    : record.FilesCopied == 0
                        ? "没有文件需要更新。"
                        : $"{record.FilesCopied} 个文件 · {record.BytesText}。";
                return $"{detail}可以安全拔出 {TargetLetter}。";
            }

            return AutoSyncEnabled
                ? $"自动同步已就绪。也可以现在手动写入 {SourceCountText}。"
                : "自动同步已暂停，点击“立即同步”即可写入。";
        }
    }

    public string StatusBadge => IsSyncing
        ? "同步中"
        : TargetAmbiguous
            ? "需要确认"
            : !HasTarget
                ? "尚未设置"
                : !TargetConnected
                    ? "等待插入"
                    : !HasSources
                        ? "等待内容"
                        : IsSessionComplete
                            ? "已完成"
                            : "已连接";

    public string StatusTone => IsSyncing
        ? "busy"
        : TargetAmbiguous
            ? "error"
            : !HasTarget || !TargetConnected
                ? "idle"
                : !HasSources
                    ? "ready"
                    : IsSessionComplete
                        ? "done"
                        : "ready";

    public string PrimaryActionText
    {
        get
        {
            if (IsSyncing)
            {
                return "取消同步";
            }

            if (!HasTarget || TargetAmbiguous)
            {
                return "选择 U 盘";
            }

            if (!TargetConnected)
            {
                return "查看设备";
            }

            if (!HasSources)
            {
                return "添加内容";
            }

            return "立即同步";
        }
    }

    public string PrimaryActionGlyph => IsSyncing ? "Icon.Close" : "Icon.Sync";

    public string PathSourceValue => EnabledSourceCount > 0 ? SourceCountText : "还没有选择内容";

    public string PathSourceSub => SourceNamesText;

    public string PathRuleValue => Settings.Sync.SmartOverwrite ? "智能覆盖" : "全部覆盖";

    public string PathRuleSub => Settings.Sync.MirrorDelete
        ? "会删除目标中多余的文件"
        : "保留目录结构 · 不删其他文件";

    public string PathTargetValue => HasTarget ? TargetLabel : "还没有选择设备";

    public string PathTargetSub => TargetConnected
        ? $"{TargetLetter} · {TargetFreeText}"
        : HasTarget
            ? $"{TargetLetter} · 未连接"
            : "插入后自动识别";

    public bool HasLastSync => _coordinator.LastRecord is not null;

    public SyncRecord? LastRecord => _coordinator.LastRecord;

    public string LastSyncTitle
    {
        get
        {
            var record = LastRecord;
            if (record is null)
            {
                return "还没有同步记录";
            }

            return record.Success
                ? record.FilesCopied == 0 ? "已是最新" : "写入完成"
                : record.Cancelled ? "同步已取消" : "同步未完成";
        }
    }

    public string LastSyncSummary
    {
        get
        {
            var record = LastRecord;
            if (record is null)
            {
                return "第一次同步后，这里会留下文件数量、体积和用时。";
            }

            return record.Success
                ? record.FilesCopied == 0
                    ? "目标上的文件已经是新的。"
                    : $"写入 {record.FilesCopied} 个文件，跳过 {record.FilesSkipped} 个未变化文件。"
                : record.Cancelled
                    ? "写入被中断，未完成的临时文件已清理。"
                    : record.Error ?? "有文件未能写入，请查看活动记录。";
        }
    }

    public string LastSyncTimeText => LastRecord is null ? "—" : FormatWhen(LastRecord.StartedAt.LocalDateTime);

    public string LastSyncDeviceText => LastRecord is null ? "—" : $"{LastRecord.DriveLetter} · {LastRecord.DeviceLabel}";

    public string LastSyncFilesText => LastRecord?.FilesCopied.ToString() ?? "—";

    public string LastSyncBytesText => LastRecord?.BytesText ?? "—";

    public string LastSyncDurationText => LastRecord?.DurationText ?? "—";

    public string LastSyncStatusTone => LastRecord is null
        ? "idle"
        : LastRecord.Success
            ? "success"
            : LastRecord.Cancelled
                ? "cancelled"
                : "error";

    public bool LastSyncHasError => !string.IsNullOrWhiteSpace(LastRecord?.Error);

    public bool LastSyncWasSuccess => LastRecord?.Success == true;

    public bool LastSyncWasCancelled => LastRecord?.Cancelled == true;

    public bool LastSyncFailed => LastRecord is not null && !LastRecord.Success && !LastRecord.Cancelled;

    public string LastSyncErrorText => LastRecord?.Error ?? string.Empty;

    public bool AutoSyncEnabled
    {
        get => Settings.AutoSyncEnabled;
        set
        {
            if (value == Settings.AutoSyncEnabled)
            {
                return;
            }

            _coordinator.SetAutoSyncEnabled(value);
            ShowToast(value ? "自动同步已开启。" : "自动同步已暂停，仍可手动同步。");
            RefreshStatus();
        }
    }

    public string AutoSyncStateText => Settings.AutoSyncEnabled ? "自动同步已开启" : "自动同步已暂停";

    public string AutoSyncDescription => Settings.AutoSyncEnabled
        ? "插入目标 U 盘后，自动比较并写入有变化的文件。"
        : "插入 U 盘后不会自动开始，可在总览中手动同步。";

    public bool IsDropActive
    {
        get => _isDropActive;
        private set => SetProperty(ref _isDropActive, value);
    }

    // ---------------------------------------------------------------- sources

    public string SourcePageSummary => EnabledSourceCount == 0
        ? "还没有启用任何同步项。"
        : $"已启用 {EnabledSourceCount} 个同步项，共 {SourceCount} 个。";

    // ---------------------------------------------------------------- devices

    public string TargetDisplayName => HasTarget ? TargetLabel : "尚未选择目标设备";

    public string TargetLetterDisplay => HasTarget ? TargetLetter : "—";

    public string TargetStatusText => TargetAmbiguous
        ? "发现相似设备"
        : !HasTarget
            ? "未设置"
            : TargetConnected
                ? "已连接"
                : "等待插入";

    public string TargetStatusTone => TargetAmbiguous
        ? "error"
        : !HasTarget
            ? "idle"
            : TargetConnected
                ? "success"
                : "idle";

    public string TargetCapacityDisplay => ConnectedTarget is null
        ? Settings.TargetDevice?.SizeText ?? "—"
        : ByteSize.Format(ConnectedTarget.TotalSizeBytes);

    public string TargetFreeDisplay => ConnectedTarget is null
        ? "—"
        : ByteSize.Format(ConnectedTarget.FreeSpaceBytes);

    public string TargetFileSystemDisplay => ConnectedTarget?.FileSystem
                                             ?? Settings.TargetDevice?.FileSystem
                                             ?? "—";

    public string TargetSerialDisplay => Settings.TargetDevice is null
        ? "—"
        : string.IsNullOrWhiteSpace(Settings.TargetDevice.VolumeSerial)
            ? "无序列号（按卷标和容量识别）"
            : Settings.TargetDevice.VolumeSerial.ToUpperInvariant();

    public string TargetRootPathDisplay => ConnectedTarget?.RootPath
                                           ?? (HasTarget ? TargetLetter + "\\" : "—");

    public double TargetUsedPercent => ConnectedTarget?.UsedFraction * 100 ?? 0;

    public bool HasRemovableDevices => RemovableDevices.Count > 0;

    public bool HasFixedDevices => FixedDevices.Count > 0;

    public bool HasNoRemovableDevices => RemovableDevices.Count == 0;

    public bool ShowFixedSection => ShowFixedDrives && FixedDevices.Count > 0;

    public bool ShowAdvancedToggle => FixedDevices.Count > 0;

    public string RemovableDeviceCountText => $"{RemovableDevices.Count} 个";

    public bool ShowFixedDrives
    {
        get => _showFixedDrives;
        set
        {
            if (SetProperty(ref _showFixedDrives, value))
            {
                OnPropertyChanged(nameof(ShowFixedSection));
                OnPropertyChanged(nameof(HasNoRemovableDevices));
            }
        }
    }

    // --------------------------------------------------------------- settings

    public bool SmartOverwrite
    {
        get => Settings.Sync.SmartOverwrite;
        set
        {
            if (value == Settings.Sync.SmartOverwrite)
            {
                return;
            }

            Settings.Sync.SmartOverwrite = value;
            PersistSettings();
            RefreshStatus();
        }
    }

    public bool MirrorDelete
    {
        get => Settings.Sync.MirrorDelete;
        set
        {
            if (value == Settings.Sync.MirrorDelete)
            {
                return;
            }

            if (value && !DialogService.Confirm(
                    "开启镜像清理？",
                    "每次成功同步后，SyncStick 会删除所选文件夹在 U 盘内对应目录中“多余”的文件。\n\n其他目录不会被触碰，但如果“写入到”填错，仍可能误删文件。建议先检查每个同步项的目标子目录。",
                    "开启镜像清理",
                    danger: true))
            {
                OnPropertyChanged();
                return;
            }

            Settings.Sync.MirrorDelete = value;
            PersistSettings();
            RefreshStatus();
            ShowToast(value ? "镜像清理已开启，请确认目标子目录。" : "镜像清理已关闭。", value);
        }
    }

    public bool IgnoreSystemFiles
    {
        get => Settings.Sync.IgnoreSystemFiles;
        set
        {
            if (value == Settings.Sync.IgnoreSystemFiles)
            {
                return;
            }

            Settings.Sync.IgnoreSystemFiles = value;
            PersistSettings();
            RefreshStatus();
        }
    }

    public bool CopyTimestamps
    {
        get => Settings.Sync.CopyTimestamps;
        set
        {
            if (value == Settings.Sync.CopyTimestamps)
            {
                return;
            }

            Settings.Sync.CopyTimestamps = value;
            PersistSettings();
            RefreshStatus();
        }
    }

    public bool MinimizeToTray
    {
        get => Settings.MinimizeToTray;
        set
        {
            if (value == Settings.MinimizeToTray)
            {
                return;
            }

            Settings.MinimizeToTray = value;
            PersistSettings();
            OnPropertyChanged();
        }
    }

    public bool NotifyOnComplete
    {
        get => Settings.NotifyOnComplete;
        set
        {
            if (value == Settings.NotifyOnComplete)
            {
                return;
            }

            Settings.NotifyOnComplete = value;
            PersistSettings();
            OnPropertyChanged();
        }
    }

    public bool AutoSyncOnLaunch
    {
        get => Settings.AutoSyncOnLaunch;
        set
        {
            if (value == Settings.AutoSyncOnLaunch)
            {
                return;
            }

            Settings.AutoSyncOnLaunch = value;
            PersistSettings();
            OnPropertyChanged();
        }
    }

    public bool StartWithWindows
    {
        get => AutoStartService.IsEnabled();
        set
        {
            try
            {
                AutoStartService.SetEnabled(value);
                Settings.StartWithWindows = value;
                PersistSettings();
                OnPropertyChanged();
                ShowToast(value ? "已加入 Windows 开机启动。" : "已取消开机启动。");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Auto-start change failed: " + ex);
                OnPropertyChanged();
                ShowToast("开机启动没有设置成功。", true);
                ShowStartupRestrictionHelp(ex);
            }
        }
    }

    private static void ShowStartupRestrictionHelp(Exception error)
    {
        var programFolder = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var developmentHost = error.Message.Contains("发布为独立的", StringComparison.Ordinal);

        var reason = developmentHost
            ? "当前是以开发方式运行的，开机启动只支持发布后的 SyncStick.exe。"
            : "系统拒绝了开机启动项。SyncStick 尝试写入当前用户的启动项，也尝试了启动文件夹里的快捷方式。";

        var steps = developmentHost
            ? "请从发布目录（dist\\SyncStick）运行 SyncStick.exe，再回到“设置”打开“开机自动启动”。"
            : "这通常发生在程序被沙箱、安全软件或系统策略限制时。可以这样重试：" +
              Environment.NewLine +
              "1. 在资源管理器里打开程序所在文件夹；" +
              Environment.NewLine +
              "2. 双击 SyncStick.exe 重新运行；" +
              Environment.NewLine +
              "3. 回到“设置”，再打开“开机自动启动”。";

        var manualFallback = developmentHost
            ? string.Empty
            : Environment.NewLine + Environment.NewLine +
              "如果仍然失败，可以手动把 SyncStick 的快捷方式放进启动文件夹：" +
              Environment.NewLine + startupFolder;

        DialogService.Info(
            "开机启动没有设置成功",
            reason +
            Environment.NewLine + Environment.NewLine +
            steps +
            manualFallback +
            Environment.NewLine + Environment.NewLine +
            "当前程序目录：" + programFolder,
            "知道了");
    }

    public string DataDirectoryText => _coordinator.DataDirectory;

    public string AppVersionText => "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

    public string FooterVersionText => "SYNCSTICK " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

    // --------------------------------------------------------------- activity

    public bool HasActivities => Activities.Count > 0;

    public string ActivityCountText => Activities.Count == 0
        ? "还没有同步记录"
        : $"最近 {Activities.Count} 次同步";

    public string ActivityTotalRuns => Activities.Count.ToString();

    public string ActivitySuccessRate
    {
        get
        {
            if (Activities.Count == 0)
            {
                return "—";
            }

            var success = Activities.Count(row => row.Record.Success);
            return $"{success * 100 / Activities.Count}%";
        }
    }

    public string ActivityBytesTotal => Activities.Count == 0
        ? "—"
        : ByteSize.Format(Activities.Sum(row => row.Record.BytesCopied));

    // ---------------------------------------------------------------- actions

    public void NavigateTo(string page) => Navigate(page);

    public void OnSourceEnabledChanged()
    {
        PersistSettings();
        UpdateNavBadges();
        RefreshStatus();
    }

    public void OnSourceDestinationChanged()
    {
        PersistSettings();
        RefreshStatus();
    }

    public void RemoveSource(SourceRowViewModel row)
    {
        Settings.Sources.RemoveAll(source => source.Id == row.Source.Id);
        PersistSettings();
        RefreshAll();
        ShowToast("已从同步内容中移除，U 盘上的文件不会被删除。");
    }

    public void SelectDevice(VolumeInfo volume)
    {
        if (!volume.IsRemovable && !DialogService.Confirm(
                "选择固定磁盘？",
                $"{volume.DisplayName} 被 Windows 识别为固定磁盘，它可能是内置硬盘，也可能是 USB 硬盘盒。\n\n选择后，SyncStick 会在它接入时写入文件。请确认它不是系统盘或重要工作盘。",
                "仍然选择",
                danger: true))
        {
            return;
        }

        if (!volume.IsRemovable)
        {
            ShowFixedDrives = true;
        }

        _coordinator.SelectTarget(volume);
        RefreshAll();
        ShowToast($"已把 {volume.DisplayName} 设为同步目标。");
        if (string.IsNullOrWhiteSpace(volume.VolumeSerial))
        {
            ShowToast("这块设备没有可读取的卷序列号，将按卷标和容量识别。", true);
        }
    }

    public void ClearTarget()
    {
        _coordinator.ClearTarget();
        _sessionCompletedAt = null;
        RefreshAll();
        ShowToast("已移除目标设备，U 盘上的文件不会被修改。");
    }

    public void SetDropActive(bool active) => IsDropActive = active;

    public void AddDroppedPaths(IEnumerable<string> paths) => AddPaths(paths);

    public async Task SyncNowFromTrayAsync()
    {
        await PrimaryActionAsync();
    }

    public void ToggleAutoSyncFromTray()
    {
        AutoSyncEnabled = !AutoSyncEnabled;
    }

    // ---------------------------------------------------------------- helpers

    private void Navigate(string? page)
    {
        if (string.IsNullOrWhiteSpace(page))
        {
            return;
        }

        if (page is not ("dashboard" or "sources" or "devices" or "activity" or "settings"))
        {
            return;
        }

        CurrentPageKey = page;
        SetPageSelection(page);
    }

    private void SetPageSelection(string page)
    {
        foreach (var item in NavItems)
        {
            item.IsSelected = item.Key == page;
        }
    }

    private void AddFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择要同步的文件夹",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            AddPaths(dialog.FolderNames);
        }
    }

    private void AddFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要同步的文件",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            AddPaths(dialog.FileNames);
        }
    }

    public void BrowseDestination(SourceRowViewModel row)
    {
        var target = ConnectedTarget;
        if (target is null)
        {
            DialogService.Info(
                "先连接目标 U 盘",
                "要直接选择 U 盘上的文件夹，需要先把目标 U 盘插上。\n\n" +
                "也可以现在直接输入相对路径，例如：我的项目\\资料。插上 U 盘后会自动按这个路径写入。");
            return;
        }

        var root = Path.GetFullPath(target.RootPath);
        var rootNoSlash = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择 U 盘上的写入文件夹",
            UseDescriptionForTitle = true,
            SelectedPath = root,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK ||
            string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        string selected;
        try
        {
            selected = Path.GetFullPath(dialog.SelectedPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return;
        }

        if (!selected.Equals(rootNoSlash, StringComparison.OrdinalIgnoreCase) &&
            !selected.StartsWith(rootNoSlash + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            DialogService.Info(
                "只能选择目标 U 盘里的文件夹",
                "刚才选中的文件夹不在这块目标 U 盘上。\n\n" +
                "请选择 U 盘里的文件夹；如果还没有合适的文件夹，可以在选择窗口里新建一个。");
            return;
        }

        var relative = selected.Equals(rootNoSlash, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : Path.GetRelativePath(root, selected).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        row.DestinationSubfolder = relative;
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var added = 0;
        var skipped = 0;

        foreach (var rawPath in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(rawPath);
            }
            catch (Exception)
            {
                skipped++;
                continue;
            }

            var isFolder = Directory.Exists(fullPath);
            var isFile = File.Exists(fullPath);
            if (!isFolder && !isFile)
            {
                skipped++;
                continue;
            }

            if (Settings.Sources.Any(source => source.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }

            var targetRoot = ConnectedTarget?.RootPath
                             ?? (Settings.TargetDevice?.LastDriveLetter is { Length: > 1 } letter ? letter + "\\" : null);
            if (targetRoot is not null && PathGuard.IsWithin(fullPath, targetRoot))
            {
                ShowToast("不能把目标 U 盘内部的文件作为同步来源。", true);
                skipped++;
                continue;
            }

            var item = new SourceItem
            {
                Path = fullPath,
                IsFolder = isFolder,
                DestinationSubfolder = isFolder ? DefaultFolderDestination(fullPath) : null,
                Enabled = true
            };
            Settings.Sources.Add(item);
            added++;
        }

        if (added > 0)
        {
            PersistSettings();
            RefreshAll();
            ShowToast(added == 1 ? "已添加 1 个同步项。" : $"已添加 {added} 个同步项。");
        }
        else if (skipped > 0)
        {
            ShowToast("没有添加新内容：路径不存在、重复，或位于目标 U 盘内。", true);
        }
    }

    private static string DefaultFolderDestination(string folderPath)
    {
        var trimmed = folderPath.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var root = Path.GetPathRoot(folderPath)?.TrimEnd('\\');
        return string.IsNullOrWhiteSpace(root) ? "同步内容" : $"磁盘 {root.TrimEnd(':')}";
    }

    private void RefreshDevices()
    {
        _coordinator.RefreshNow();
        RefreshAll();
        ShowToast("设备列表已刷新。");
    }

    private async Task PrimaryActionAsync()
    {
        if (IsSyncing)
        {
            _coordinator.CancelSync();
            ShowToast("正在停止同步…");
            return;
        }

        if (!HasTarget || TargetAmbiguous)
        {
            Navigate("devices");
            return;
        }

        if (!TargetConnected)
        {
            Navigate("devices");
            return;
        }

        if (!HasSources)
        {
            Navigate("sources");
            return;
        }

        try
        {
            await _coordinator.SyncNowAsync();
        }
        catch (SyncStickException ex)
        {
            ShowToast(ex.Message, true);
        }
        catch (InvalidOperationException ex)
        {
            ShowToast(ex.Message, true);
        }
        catch (Exception ex)
        {
            ShowToast("同步失败：" + ex.Message, true);
        }
        finally
        {
            RefreshAll();
        }
    }

    private void ClearActivities()
    {
        if (!DialogService.Confirm(
                "清空活动记录？",
                "这只会删除本机保存的同步历史，不会影响 U 盘或电脑上的任何文件。",
                "清空记录",
                danger: true))
        {
            return;
        }

        _coordinator.ClearActivities();
        RefreshAll();
        ShowToast("活动记录已清空。");
    }

    private void OpenDataFolder()
    {
        try
        {
            var directory = _coordinator.DataDirectory;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowToast("无法打开数据目录：" + ex.Message, true);
        }
    }

    private void PersistSettings()
    {
        if (!_coordinator.TrySaveSettings(out var error) && !string.IsNullOrWhiteSpace(error))
        {
            ShowToast(error, true);
        }
    }

    private void RefreshFromCoordinator()
    {
        if (!TargetConnected)
        {
            _sessionCompletedAt = null;
        }

        RefreshAll();
    }

    private void RefreshAll()
    {
        SyncSourceRows();
        RebuildDeviceRows();
        RebuildActivities();
        UpdateNavBadges();
        foreach (var source in Sources)
        {
            source.RefreshDerived();
        }

        RefreshStatus();
    }

    private void RefreshStatus()
    {
        OnPropertyChanged(string.Empty);
    }

    private void SyncSourceRows()
    {
        var existingIds = Sources.Select(row => row.Source.Id).ToArray();
        var desiredIds = Settings.Sources.Select(source => source.Id).ToArray();
        if (existingIds.SequenceEqual(desiredIds))
        {
            return;
        }

        Sources.Clear();
        foreach (var source in Settings.Sources)
        {
            Sources.Add(new SourceRowViewModel(this, source));
        }
    }

    private void RebuildDeviceRows()
    {
        RemovableDevices.Clear();
        FixedDevices.Clear();

        foreach (var volume in _coordinator.Volumes)
        {
            var row = new VolumeRowViewModel(this, volume)
            {
                IsTarget = IsCurrentTarget(volume)
            };

            if (volume.IsRemovable)
            {
                RemovableDevices.Add(row);
            }
            else
            {
                FixedDevices.Add(row);
            }
        }
    }

    private bool IsCurrentTarget(VolumeInfo volume)
    {
        if (Settings.TargetDevice is null)
        {
            return false;
        }

        return DeviceMatcher.Match(Settings.TargetDevice, new[] { volume }).Device is not null;
    }

    private void RebuildActivities()
    {
        Activities.Clear();
        RecentActivities.Clear();

        foreach (var record in _coordinator.Activities)
        {
            var row = new ActivityRowViewModel(record);
            Activities.Add(row);
            if (RecentActivities.Count < 3)
            {
                RecentActivities.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasActivities));
        OnPropertyChanged(nameof(ActivityCountText));
    }

    private void UpdateNavBadges()
    {
        foreach (var item in NavItems)
        {
            item.Badge = item.Key switch
            {
                "sources" => SourceCount > 0 ? SourceCount.ToString() : null,
                "devices" => HasTarget ? TargetConnected ? TargetLetter : "!" : null,
                _ => null
            };
        }
    }

    private void OnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }
    }

    private static string FormatWhen(DateTime local)
    {
        var today = DateTime.Today;
        if (local.Date == today)
        {
            return "今天 " + local.ToString("HH:mm");
        }

        if (local.Date == today.AddDays(-1))
        {
            return "昨天 " + local.ToString("HH:mm");
        }

        return local.ToString("M 月 d 日 HH:mm");
    }
}
