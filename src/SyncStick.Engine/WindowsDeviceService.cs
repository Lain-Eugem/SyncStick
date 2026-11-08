using System.Runtime.InteropServices;
using System.Text;

namespace SyncStick.Engine;

/// <summary>
/// Enumerates mounted volumes without requiring WMI. Removable drives are
/// preferred; fixed drives other than the system drive are also returned so
/// USB hard disks and SSDs (which Windows reports as fixed) can be used.
/// </summary>
public sealed class WindowsDeviceService : IDeviceService
{
    public IReadOnlyList<VolumeInfo> EnumerateVolumes()
    {
        var volumes = new List<VolumeInfo>();
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception)
        {
            return volumes;
        }

        foreach (var drive in drives)
        {
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                var driveType = drive.DriveType;
                if (driveType is not (DriveType.Removable or DriveType.Fixed))
                {
                    continue;
                }

                var root = drive.RootDirectory.FullName;
                if (string.Equals(root, systemRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var label = string.Empty;
                var fileSystem = string.Empty;
                long total = 0;
                long free = 0;
                try
                {
                    label = drive.VolumeLabel?.Trim() ?? string.Empty;
                }
                catch (Exception)
                {
                    // Label access can fail on flaky removable media.
                }

                try
                {
                    fileSystem = drive.DriveFormat ?? string.Empty;
                }
                catch (Exception)
                {
                }

                try
                {
                    total = drive.TotalSize;
                    free = drive.AvailableFreeSpace;
                }
                catch (Exception)
                {
                }

                volumes.Add(new VolumeInfo
                {
                    RootPath = root,
                    DriveLetter = root.TrimEnd('\\'),
                    Label = label,
                    FileSystem = fileSystem,
                    TotalSizeBytes = total,
                    FreeSpaceBytes = free,
                    IsRemovable = driveType == DriveType.Removable,
                    VolumeSerial = TryGetVolumeSerial(root)
                });
            }
            catch (Exception)
            {
                // A volume can disappear between enumeration and inspection.
            }
        }

        return volumes
            .OrderByDescending(volume => volume.IsRemovable)
            .ThenBy(volume => volume.DriveLetter, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string TryGetVolumeSerial(string rootPath)
    {
        try
        {
            var volumeName = new StringBuilder(261);
            var fileSystemName = new StringBuilder(261);
            if (GetVolumeInformationW(
                    rootPath,
                    volumeName,
                    volumeName.Capacity,
                    out var serial,
                    out _,
                    out _,
                    fileSystemName,
                    fileSystemName.Capacity))
            {
                return serial.ToString("X8");
            }
        }
        catch (Exception)
        {
            // Serial is a convenience; label + size still work as a fallback.
        }

        return string.Empty;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string rootPathName,
        StringBuilder volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder fileSystemNameBuffer,
        int fileSystemNameSize);
}
