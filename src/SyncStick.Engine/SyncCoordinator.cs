using SyncStick.Core;
using SyncStick.Core.Models;
using SyncStick.Core.Services;

namespace SyncStick.Engine;

/// <summary>
/// Owns the live sync state: device polling, insertion detection, automatic
/// sync scheduling, manual sync, activity history and settings persistence.
/// </summary>
public sealed class SyncCoordinator : IDisposable
{
    private static readonly TimeSpan AutoSyncDelay = TimeSpan.FromSeconds(1.8);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1200);

    private readonly SettingsStore _settingsStore;
    private readonly ActivityStore _activityStore;
    private readonly IDeviceService _deviceService;
    private readonly SyncEngine _engine = new();
    private readonly Timer _pollTimer;
    private readonly object _stateGate = new();

    private IReadOnlyList<VolumeInfo> _volumes = Array.Empty<VolumeInfo>();
    private DeviceMatch _targetMatch = DeviceMatch.None;
    private List<SyncRecord> _activities;
    private bool _isSyncing;
    private SyncProgress? _progress;
    private SyncRecord? _lastRecord;
    private CancellationTokenSource? _autoSyncDelayCts;
    private CancellationTokenSource? _syncCts;
    private bool _started;
    private bool _disposed;
    private bool _baselineReady;
    private bool _wasConnected;
    private bool _autoSyncDoneForConnection;
    private bool _launchAutoScheduled;
    private int _polling;

    public SyncCoordinator(
        SettingsStore settingsStore,
        ActivityStore activityStore,
        IDeviceService deviceService)
    {
        _settingsStore = settingsStore;
        _activityStore = activityStore;
        _deviceService = deviceService;
        Settings = _settingsStore.Load();
        _activities = _activityStore.Load();
        _lastRecord = _activities.FirstOrDefault();
        RefreshVolumesCore(raiseEvent: false);
        _pollTimer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler? StateChanged;

    public event EventHandler<SyncProgress>? ProgressChanged;

    public event EventHandler<SyncRecord>? SyncFinished;

    public event EventHandler<NoticeEventArgs>? Notice;

    public AppSettings Settings { get; }

    public IReadOnlyList<VolumeInfo> Volumes => _volumes;

    public DeviceMatch TargetMatch => _targetMatch;

    public IReadOnlyList<SyncRecord> Activities => _activities;

    public SyncRecord? LastRecord => _lastRecord;

    public SyncProgress? Progress => _progress;

    public bool IsSyncing => _isSyncing;

    public bool TargetConnected => _targetMatch.Device is not null;

    public string DataDirectory => SettingsStore.AppDataDirectory;

    public int EnabledSourceCount => Settings.Sources.Count(source => source.Enabled);

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _baselineReady = true;
        _wasConnected = _targetMatch.Device is not null;
        _autoSyncDoneForConnection = _wasConnected;
        _pollTimer.Change(PollInterval, PollInterval);
        RaiseStateChanged();

        if (_wasConnected
            && Settings.AutoSyncEnabled
            && Settings.AutoSyncOnLaunch
            && !_launchAutoScheduled
            && HasEnabledSources())
        {
            _launchAutoScheduled = true;
            ScheduleAutoSync(launch: true, trigger: "启动时同步");
        }
    }

    public void RefreshNow()
    {
        Poll(forceUiNotification: true);
    }

    public void RefreshVolumesOnly()
    {
        RefreshVolumesCore(raiseEvent: true);
    }

    public void SelectTarget(VolumeInfo volume)
    {
        ArgumentNullException.ThrowIfNull(volume);
        Settings.TargetDevice = DeviceMatcher.CreateIdentity(volume);
        TrySaveSettings(out _);
        RefreshVolumesCore(raiseEvent: false);
        _wasConnected = _targetMatch.Device is not null;
        _autoSyncDoneForConnection = _wasConnected;
        RaiseStateChanged();
    }

    public void ClearTarget()
    {
        Settings.TargetDevice = null;
        TrySaveSettings(out _);
        RefreshVolumesCore(raiseEvent: false);
        _wasConnected = false;
        _autoSyncDoneForConnection = false;
        RaiseStateChanged();
    }

    public bool TrySaveSettings(out string? error)
    {
        try
        {
            _settingsStore.Save(Settings);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "设置保存失败：" + ex.Message;
            return false;
        }
    }

    public void SetAutoSyncEnabled(bool enabled)
    {
        Settings.AutoSyncEnabled = enabled;
        if (!enabled)
        {
            CancelPendingAutoSync();
        }

        TrySaveSettings(out _);
        RaiseStateChanged();
    }

    public void CancelSync()
    {
        _syncCts?.Cancel();
    }

    public async Task<SyncRecord?> SyncNowAsync(string trigger = "手动同步")
    {
        if (_disposed)
        {
            throw new InvalidOperationException("应用正在退出。");
        }

        if (Settings.TargetDevice is null)
        {
            throw new SyncStickException("还没有选择目标设备。");
        }

        if (_targetMatch.IsAmbiguous)
        {
            throw new SyncStickException("检测到多块相似设备，请在“目标设备”中重新选择。");
        }

        if (_targetMatch.Device is null)
        {
            throw new SyncStickException("目标 U 盘当前未连接。");
        }

        var sources = Settings.Sources
            .Where(source => source.Enabled)
            .Select(source => source.Clone())
            .ToList();
        if (sources.Count == 0)
        {
            throw new SyncStickException("还没有选择要同步的内容。");
        }

        lock (_stateGate)
        {
            if (_isSyncing)
            {
                throw new InvalidOperationException("正在同步，请稍候。");
            }

            _isSyncing = true;
        }

        _autoSyncDoneForConnection = true;
        CancelPendingAutoSync();

        var device = _targetMatch.Device;
        var options = new SyncSettings
        {
            SmartOverwrite = Settings.Sync.SmartOverwrite,
            MirrorDelete = Settings.Sync.MirrorDelete,
            IgnoreSystemFiles = Settings.Sync.IgnoreSystemFiles,
            CopyTimestamps = Settings.Sync.CopyTimestamps
        };
        var startedAt = DateTimeOffset.Now;
        var cts = new CancellationTokenSource();
        _syncCts = cts;

        SyncResult? result = null;
        Exception? failure = null;
        SyncRecord? record = null;
        try
        {
            try
            {
                SetProgress(new SyncProgress(SyncStage.Preparing, "正在扫描来源…", 0, 0, 0, 0));

                var plan = await Task.Run(
                    () => _engine.BuildPlan(sources, device.RootPath, options, cts.Token),
                    cts.Token);

                var progress = new Progress<SyncProgress>(SetProgress);
                result = await Task.Run(
                    () => _engine.ExecuteAsync(plan, options, progress, trigger, cts.Token),
                    cts.Token);
            }
            catch (OperationCanceledException)
            {
                failure = null;
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            record = BuildRecord(device, startedAt, trigger, result, failure, cts.IsCancellationRequested);
            try
            {
                _activities = _activityStore.Add(record);
            }
            catch (Exception)
            {
                _activities = new[] { record }.Concat(_activities).Take(60).ToList();
            }

            _lastRecord = record;

            if (Settings.TargetDevice is not null && _targetMatch.Device is not null)
            {
                Settings.TargetDevice.LastDriveLetter = device.DriveLetter;
                Settings.TargetDevice.Label = device.Label;
                Settings.TargetDevice.FileSystem = device.FileSystem;
                TrySaveSettings(out _);
            }

            try
            {
                SyncFinished?.Invoke(this, record);
            }
            catch (Exception)
            {
                // A UI subscriber must never prevent the coordinator from resetting.
            }

            try
            {
                RaiseSyncNotice(record);
            }
            catch (Exception)
            {
                // Notifications are best-effort.
            }
        }
        finally
        {
            lock (_stateGate)
            {
                _isSyncing = false;
            }

            _syncCts = null;
            cts.Dispose();
            try
            {
                RefreshVolumesCore(raiseEvent: false);
                _wasConnected = TargetConnected;
                if (!_wasConnected)
                {
                    _autoSyncDoneForConnection = false;
                }
            }
            catch (Exception)
            {
                // Keep teardown best-effort.
            }

            _progress = null;
            try
            {
                RaiseStateChanged();
            }
            catch (Exception)
            {
                // A UI subscriber must not prevent sync teardown.
            }
        }

        return record;
    }

    public void ClearActivities()
    {
        try
        {
            _activities = _activityStore.Clear();
        }
        catch (Exception)
        {
            _activities = new List<SyncRecord>();
        }

        _lastRecord = null;
        RaiseStateChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollTimer.Dispose();
        CancelPendingAutoSync();
        _syncCts?.Cancel();
        _syncCts?.Dispose();
        _syncCts = null;
    }

    private void Poll(bool forceUiNotification = false)
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            var previousConnected = _wasConnected;
            var volumes = _deviceService.EnumerateVolumes();
            var match = DeviceMatcher.Match(Settings.TargetDevice, volumes);
            var changed = VolumeListsChanged(_volumes, volumes) || !MatchesEqual(_targetMatch, match);

            _volumes = volumes;
            _targetMatch = match;
            var connected = match.Device is not null;

            if (_baselineReady)
            {
                if (connected && !previousConnected)
                {
                    HandleArrival();
                }
                else if (!connected && previousConnected)
                {
                    HandleRemoval();
                }
            }

            _wasConnected = connected;

            if (changed || forceUiNotification)
            {
                RaiseStateChanged();
            }
        }
        catch (Exception ex)
        {
            RaiseNotice("读取设备列表失败：" + Describe(ex), isError: true);
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private void RefreshVolumesCore(bool raiseEvent)
    {
        var volumes = _deviceService.EnumerateVolumes();
        var match = DeviceMatcher.Match(Settings.TargetDevice, volumes);
        _volumes = volumes;
        _targetMatch = match;
        if (raiseEvent)
        {
            RaiseStateChanged();
        }
    }

    private void HandleArrival()
    {
        _autoSyncDoneForConnection = false;

        if (!Settings.AutoSyncEnabled)
        {
            RaiseNotice("U 盘已连接。自动同步已暂停，可在总览中手动同步。");
            return;
        }

        if (!HasEnabledSources())
        {
            RaiseNotice("U 盘已连接，但还没有选择要同步的内容。");
            return;
        }

        ScheduleAutoSync(launch: false, trigger: "插入 U 盘");
    }

    private void HandleRemoval()
    {
        CancelPendingAutoSync();
        _autoSyncDoneForConnection = false;

        if (_isSyncing)
        {
            _syncCts?.Cancel();
            RaiseNotice("目标设备已断开，正在停止同步。", isError: true);
        }
    }

    private void ScheduleAutoSync(bool launch, string trigger)
    {
        if (_disposed || !Settings.AutoSyncEnabled)
        {
            return;
        }

        CancelPendingAutoSync();
        var cts = new CancellationTokenSource();
        _autoSyncDelayCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AutoSyncDelay, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_disposed || !Settings.AutoSyncEnabled || _isSyncing)
            {
                return;
            }

            if (!launch && _autoSyncDoneForConnection)
            {
                return;
            }

            if (_targetMatch.Device is null || !HasEnabledSources())
            {
                return;
            }

            _autoSyncDoneForConnection = true;
            try
            {
                await SyncNowAsync(trigger).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!_isSyncing)
                {
                    RaiseNotice("自动同步未开始：" + Describe(ex), isError: true);
                }
            }
        });
    }

    private void CancelPendingAutoSync()
    {
        var cts = Interlocked.Exchange(ref _autoSyncDelayCts, null);
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool HasEnabledSources() => Settings.Sources.Any(source => source.Enabled);

    private void SetProgress(SyncProgress progress)
    {
        _progress = progress;
        ProgressChanged?.Invoke(this, progress);
    }

    private SyncRecord BuildRecord(
        VolumeInfo device,
        DateTimeOffset startedAt,
        string trigger,
        SyncResult? result,
        Exception? failure,
        bool cancellationRequested)
    {
        var finishedAt = DateTimeOffset.Now;
        var cancelled = result?.Cancelled ?? (failure is OperationCanceledException || cancellationRequested);
        var success = result?.Success ?? false;
        string? error = null;

        if (!success && !cancelled)
        {
            if (result is not null && result.Errors.Count > 0)
            {
                error = string.Join("；", result.Errors.Take(2));
                if (result.Errors.Count > 2)
                {
                    error += $"；另有 {result.Errors.Count - 2} 个错误";
                }
            }
            else if (failure is not null)
            {
                error = Describe(failure);
            }
            else if (result is not null)
            {
                error = "同步未能完成。";
            }
        }

        return new SyncRecord
        {
            StartedAt = result?.StartedAt ?? startedAt,
            FinishedAt = result?.FinishedAt ?? finishedAt,
            DeviceLabel = device.DisplayLabel,
            DriveLetter = device.DriveLetter,
            DestinationRoot = device.RootPath,
            Trigger = trigger,
            FilesCopied = result?.FilesCopied ?? 0,
            FilesSkipped = result?.FilesSkipped ?? 0,
            FilesDeleted = result?.FilesDeleted ?? 0,
            BytesCopied = result?.BytesCopied ?? 0,
            Success = success,
            Cancelled = cancelled,
            Error = error
        };
    }

    private void RaiseSyncNotice(SyncRecord record)
    {
        if (record.Cancelled)
        {
            RaiseNotice("同步已取消。");
            return;
        }

        if (record.Success)
        {
            var message = record.FilesCopied == 0
                ? "已是最新，无需写入。"
                : $"同步完成：{record.FilesCopied} 个文件 · {ByteSize.Format(record.BytesCopied)}";
            if (record.FilesDeleted > 0)
            {
                message += $"（清理 {record.FilesDeleted} 项）";
            }

            RaiseNotice(message);
            return;
        }

        RaiseNotice("同步未完成：" + (record.Error ?? "请查看活动记录。"), isError: true);
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void RaiseNotice(string message, bool isError = false) =>
        Notice?.Invoke(this, new NoticeEventArgs(message, isError));

    private static string Describe(Exception exception)
    {
        return exception switch
        {
            InsufficientSpaceException => exception.Message,
            DeviceUnavailableException => exception.Message,
            SyncStickException => exception.Message,
            DirectoryNotFoundException => "找不到目标设备或来源文件夹。",
            UnauthorizedAccessException => "没有权限写入目标设备，请检查 U 盘是否开启了写保护。",
            IOException => "读写失败：" + exception.Message,
            ArgumentException => "路径无效：" + exception.Message,
            _ => exception.Message
        };
    }

    private static bool VolumeListsChanged(IReadOnlyList<VolumeInfo> left, IReadOnlyList<VolumeInfo> right)
    {
        if (left.Count != right.Count)
        {
            return true;
        }

        for (var i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];
            if (!string.Equals(a.RootPath, b.RootPath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a.VolumeSerial, b.VolumeSerial, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a.Label, b.Label, StringComparison.Ordinal)
                || a.TotalSizeBytes != b.TotalSizeBytes
                || a.IsRemovable != b.IsRemovable)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesEqual(DeviceMatch left, DeviceMatch right)
    {
        if (!string.Equals(left.Device?.RootPath, right.Device?.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (left.AmbiguousCandidates.Count != right.AmbiguousCandidates.Count)
        {
            return false;
        }

        return left.AmbiguousCandidates
            .Select(volume => volume.RootPath)
            .SequenceEqual(
                right.AmbiguousCandidates.Select(volume => volume.RootPath),
                StringComparer.OrdinalIgnoreCase);
    }
}
