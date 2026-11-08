namespace SyncStick.Core.Models;

public sealed class SyncResult
{
    public required string DestinationRoot { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset FinishedAt { get; set; }

    public string Trigger { get; init; } = "手动同步";

    public bool Success { get; set; }

    public bool Cancelled { get; set; }

    public int FilesCopied { get; set; }

    public int FilesSkipped { get; set; }

    public int FilesDeleted { get; set; }

    public long BytesCopied { get; set; }

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();

    public TimeSpan Duration => FinishedAt - StartedAt;

    public string Summary
    {
        get
        {
            if (Cancelled)
            {
                return "同步已取消";
            }

            if (Success)
            {
                return FilesCopied == 0
                    ? "已是最新，无需写入"
                    : $"写入 {FilesCopied} 个文件 · {ByteSize.Format(BytesCopied)}";
            }

            return $"同步未完成 · {Errors.Count} 个错误";
        }
    }
}
