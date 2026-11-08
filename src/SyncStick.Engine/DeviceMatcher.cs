using SyncStick.Core.Models;

namespace SyncStick.Engine;

public sealed record DeviceMatch(VolumeInfo? Device, IReadOnlyList<VolumeInfo> AmbiguousCandidates)
{
    public static DeviceMatch None { get; } = new(null, Array.Empty<VolumeInfo>());

    public bool IsAmbiguous => Device is null && AmbiguousCandidates.Count > 0;
}

/// <summary>
/// Matches the saved device fingerprint against currently mounted volumes.
/// Volume serial is authoritative; label + capacity is the fallback for
/// volumes that do not expose a serial.
/// </summary>
public static class DeviceMatcher
{
    public static DeviceMatch Match(DeviceIdentity? identity, IReadOnlyList<VolumeInfo> volumes)
    {
        if (identity is null)
        {
            return DeviceMatch.None;
        }

        var candidates = volumes
            .Where(volume => volume.TotalSizeBytes > 0)
            .ToList();

        if (!string.IsNullOrWhiteSpace(identity.VolumeSerial))
        {
            var serialMatches = candidates
                .Where(volume => !string.IsNullOrWhiteSpace(volume.VolumeSerial)
                                 && volume.VolumeSerial.Equals(identity.VolumeSerial, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (serialMatches.Count == 1)
            {
                return new DeviceMatch(serialMatches[0], Array.Empty<VolumeInfo>());
            }

            if (serialMatches.Count > 1)
            {
                return new DeviceMatch(null, serialMatches);
            }

            // Only volumes without any serial may use the label + capacity
            // fallback. A different serialized device is never "close enough".
            var seriallessCandidates = candidates
                .Where(volume => string.IsNullOrWhiteSpace(volume.VolumeSerial))
                .ToList();
            return MatchWithoutSerial(identity, seriallessCandidates);
        }

        return MatchWithoutSerial(identity, candidates);
    }

    private static DeviceMatch MatchWithoutSerial(DeviceIdentity identity, IReadOnlyList<VolumeInfo> candidates)
    {
        var sameLabel = candidates
            .Where(volume => !string.IsNullOrWhiteSpace(identity.Label)
                             && volume.Label.Equals(identity.Label, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sameLabel.Count == 0)
        {
            return DeviceMatch.None;
        }

        var tolerance = Math.Max(1024L * 1024, (long)(identity.TotalSizeBytes * 0.005));
        var sizeMatches = sameLabel
            .Where(volume => Math.Abs(volume.TotalSizeBytes - identity.TotalSizeBytes) <= tolerance)
            .ToList();

        if (sizeMatches.Count == 0)
        {
            return DeviceMatch.None;
        }

        // If the drive letter still matches, prefer it when two identical
        // sticks happen to be plugged in at once.
        if (!string.IsNullOrWhiteSpace(identity.LastDriveLetter))
        {
            var preferred = sizeMatches
                .Where(volume => volume.DriveLetter.Equals(identity.LastDriveLetter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (preferred.Count == 1)
            {
                return new DeviceMatch(preferred[0], Array.Empty<VolumeInfo>());
            }
        }

        return sizeMatches.Count == 1
            ? new DeviceMatch(sizeMatches[0], Array.Empty<VolumeInfo>())
            : new DeviceMatch(null, sizeMatches);
    }

    public static DeviceIdentity CreateIdentity(VolumeInfo volume) => new()
    {
        VolumeSerial = volume.VolumeSerial,
        Label = volume.Label,
        TotalSizeBytes = volume.TotalSizeBytes,
        FileSystem = volume.FileSystem,
        LastDriveLetter = volume.DriveLetter
    };
}
