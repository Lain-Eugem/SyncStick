using SyncStick.Core;

namespace SyncStick.Engine;

/// <summary>A mounted volume that could serve as a sync target.</summary>
public sealed record VolumeInfo
{
    public required string RootPath { get; init; }

    public required string DriveLetter { get; init; }

    public required string Label { get; init; }

    public required string FileSystem { get; init; }

    public required long TotalSizeBytes { get; init; }

    public required long FreeSpaceBytes { get; init; }

    public required bool IsRemovable { get; init; }

    public required string VolumeSerial { get; init; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? "未命名 U 盘" : Label;

    public string DisplayName => $"{DisplayLabel}（{DriveLetter}）";

    public string CapacityText => $"{ByteSize.Format(TotalSizeBytes)} · 剩余 {ByteSize.Format(FreeSpaceBytes)}";

    public string KindText => IsRemovable ? "可移动磁盘" : "固定磁盘";

    public double UsedFraction => TotalSizeBytes <= 0
        ? 0
        : Math.Clamp((double)(TotalSizeBytes - FreeSpaceBytes) / TotalSizeBytes, 0, 1);

    public string ShortSerial => string.IsNullOrWhiteSpace(VolumeSerial)
        ? "无序列号"
        : VolumeSerial.Length > 8 ? VolumeSerial[^8..] : VolumeSerial;
}
