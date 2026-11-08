using System.Text.Json.Serialization;

namespace SyncStick.Core.Models;

/// <summary>A persisted summary of one sync run shown on the activity page.</summary>
public sealed class SyncRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    public string DeviceLabel { get; set; } = string.Empty;

    public string DriveLetter { get; set; } = string.Empty;

    public string DestinationRoot { get; set; } = string.Empty;

    public string Trigger { get; set; } = "手动同步";

    public int FilesCopied { get; set; }

    public int FilesSkipped { get; set; }

    public int FilesDeleted { get; set; }

    public long BytesCopied { get; set; }

    public bool Success { get; set; }

    public bool Cancelled { get; set; }

    public string? Error { get; set; }

    [JsonIgnore]
    public TimeSpan Duration => FinishedAt - StartedAt;

    [JsonIgnore]
    public string DurationText => Duration.TotalSeconds < 1
        ? "<1 秒"
        : Duration.TotalMinutes < 1
            ? $"{Duration.TotalSeconds:0} 秒"
            : $"{Duration.TotalMinutes:0} 分 {Duration.Seconds:00} 秒";

    [JsonIgnore]
    public string BytesText => ByteSize.Format(BytesCopied);

    [JsonIgnore]
    public string Summary => Success
        ? $"写入 {FilesCopied} 个文件 · {BytesText}"
        : Cancelled
            ? "同步已取消"
            : $"有文件未能写入 · {FilesCopied} 个成功";
}
