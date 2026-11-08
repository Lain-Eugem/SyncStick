using System.Text.Json.Serialization;

namespace SyncStick.Core.Models;

/// <summary>
/// A stable fingerprint of the device the user chose. The volume serial is the
/// primary key; label and size are kept as a human-readable fallback for
/// volumes that do not expose a serial (some network/virtual volumes).
/// </summary>
public sealed class DeviceIdentity
{
    public string VolumeSerial { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public long TotalSizeBytes { get; set; }

    public string FileSystem { get; set; } = string.Empty;

    /// <summary>Last drive letter seen, useful for display only.</summary>
    public string? LastDriveLetter { get; set; }

    /// <summary>
    /// Rule set used last time with this device. Remembered per device so the
    /// rule picker can preselect it when the drive is inserted again.
    /// </summary>
    public string? LastRuleId { get; set; }

    [JsonIgnore]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? "未命名设备" : Label;

    [JsonIgnore]
    public string SizeText => ByteSize.Format(TotalSizeBytes);

    [JsonIgnore]
    public string ShortSerial => string.IsNullOrWhiteSpace(VolumeSerial)
        ? "无序列号"
        : VolumeSerial.Trim().ToUpperInvariant();
}
