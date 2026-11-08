using SyncStick.App.Infrastructure;
using SyncStick.Core;
using SyncStick.Engine;

namespace SyncStick.App.ViewModels;

public sealed class VolumeRowViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _isTarget;

    public VolumeRowViewModel(MainViewModel owner, VolumeInfo volume)
    {
        _owner = owner;
        Volume = volume;
        SelectCommand = new RelayCommand(() => _owner.SelectDevice(Volume));
    }

    public VolumeInfo Volume { get; }

    public string DriveLetter => Volume.DriveLetter;

    public string Label => Volume.DisplayLabel;

    public string KindText => Volume.IsRemovable ? "可移动设备" : "固定磁盘";

    public string KindBadge => Volume.IsRemovable ? "USB" : "本地";

    public string CapacityText => Volume.CapacityText;

    public string FileSystemText => string.IsNullOrWhiteSpace(Volume.FileSystem) ? "未知格式" : Volume.FileSystem;

    public string MetaText => $"{FileSystemText} · {ByteSize.Format(Volume.TotalSizeBytes)} · 剩余 {ByteSize.Format(Volume.FreeSpaceBytes)}";

    public string SerialText => Volume.VolumeSerial;

    public double UsedPercent => Volume.UsedFraction * 100;

    public bool IsRemovable => Volume.IsRemovable;

    public bool IsTarget
    {
        get => _isTarget;
        set
        {
            if (SetProperty(ref _isTarget, value))
            {
                OnPropertyChanged(nameof(IsNotTarget));
                OnPropertyChanged(nameof(ActionText));
            }
        }
    }

    public bool IsNotTarget => !IsTarget;

    public string ActionText => IsTarget ? "当前目标" : "设为目标";

    public RelayCommand SelectCommand { get; }
}
