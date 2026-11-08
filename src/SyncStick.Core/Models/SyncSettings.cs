namespace SyncStick.Core.Models;

/// <summary>How files are written onto the target device.</summary>
public sealed class SyncSettings
{
    /// <summary>
    /// Recommended. Only copies files that are new or changed. When false,
    /// every selected file is written on every sync.
    /// </summary>
    public bool SmartOverwrite { get; set; } = true;

    /// <summary>
    /// DANGEROUS. Deletes files inside a synced folder that no longer exist in
    /// the source folder. Top-level file sources are never deleted.
    /// </summary>
    public bool MirrorDelete { get; set; }

    public bool IgnoreSystemFiles { get; set; } = true;

    public bool CopyTimestamps { get; set; } = true;
}
