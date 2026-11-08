namespace SyncStick.Engine;

public interface IDeviceService
{
    IReadOnlyList<VolumeInfo> EnumerateVolumes();
}
