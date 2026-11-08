namespace SyncStick.Engine;

public sealed class NoticeEventArgs : EventArgs
{
    public NoticeEventArgs(string message, bool isError = false)
    {
        Message = message;
        IsError = isError;
    }

    public string Message { get; }

    public bool IsError { get; }
}
