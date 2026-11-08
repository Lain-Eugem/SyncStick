namespace SyncStick.Core;

public class SyncStickException : Exception
{
    public SyncStickException(string message) : base(message)
    {
    }

    public SyncStickException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class InsufficientSpaceException : SyncStickException
{
    public InsufficientSpaceException(long requiredBytes, long availableBytes)
        : base($"U 盘剩余空间不足：需要 {ByteSize.Format(requiredBytes)}，可用 {ByteSize.Format(availableBytes)}。")
    {
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
    }

    public long RequiredBytes { get; }

    public long AvailableBytes { get; }
}

public sealed class DeviceUnavailableException : SyncStickException
{
    public DeviceUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException ?? new IOException(message))
    {
    }
}
