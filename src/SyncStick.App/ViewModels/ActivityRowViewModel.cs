using SyncStick.App.Localization;
using SyncStick.Core;
using SyncStick.Core.Models;

namespace SyncStick.App.ViewModels;

public sealed class ActivityRowViewModel
{
    public ActivityRowViewModel(SyncRecord record)
    {
        Record = record;
    }

    public SyncRecord Record { get; }

    public string TimeText => Record.StartedAt.LocalDateTime.ToString("HH:mm");

    public string FeedTimeText => Loc.FormatFeedTime(Record.StartedAt.LocalDateTime);

    public string DateText => Loc.FormatActivityDate(Record.StartedAt.LocalDateTime);

    public string FullTimeText => Record.StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string StatusText => Record.Success ? "完成" : Record.Cancelled ? "已取消" : "未完成";

    public string StatusTone => Record.Success ? "success" : Record.Cancelled ? "cancelled" : "error";

    public bool Failed => !Record.Success && !Record.Cancelled;

    public string DeviceText => string.IsNullOrWhiteSpace(Record.DriveLetter)
        ? Record.DeviceLabel
        : $"{Record.DriveLetter} · {Record.DeviceLabel}";

    public string SummaryText => Record.Success
        ? Record.FilesCopied == 0
            ? "已是最新，无需写入"
            : $"写入 {Record.FilesCopied} 个文件"
        : Record.Cancelled
            ? "同步被中断"
            : "有文件未能写入";

    public string MetaText => $"{ByteSize.Format(Record.BytesCopied)} · {Record.DurationText}";

    public string DetailText => Record.FilesSkipped > 0
        ? $"跳过 {Record.FilesSkipped} 个未变化的文件"
        : Record.Trigger;

    public string TriggerText => Record.Trigger;

    public bool HasError => !string.IsNullOrWhiteSpace(Record.Error);

    public string ErrorText => Record.Error ?? string.Empty;

    public bool HasDeletions => Record.FilesDeleted > 0;

    public string DeletionText => $"清理 {Record.FilesDeleted} 项";
}
