namespace SyncStick.Core.Models;

/// <summary>
/// The result of scanning every source before any byte is written. Building a
/// plan is read-only and always completes before the target is touched.
/// </summary>
public sealed class SyncPlan
{
    public SyncPlan(string destinationRoot)
    {
        DestinationRoot = destinationRoot;
    }

    public string DestinationRoot { get; }

    public List<PlannedFile> Files { get; } = new();

    public List<MirrorScope> MirrorScopes { get; } = new();

    public List<string> Warnings { get; } = new();

    /// <summary>
    /// Sources that cannot be used at all. A plan containing these must not be
    /// executed: silently skipping a missing project would look like success.
    /// </summary>
    public List<string> SourceErrors { get; } = new();

    public int FilesToCopy => Files.Count(file => file.NeedsCopy);

    public int FilesUnchanged => Files.Count - FilesToCopy;

    public long BytesToCopy => Files.Where(file => file.NeedsCopy).Sum(file => file.Length);

    public long BytesSelected => Files.Sum(file => file.Length);

    public bool HasWork => FilesToCopy > 0;
}

public sealed class PlannedFile
{
    public required string SourcePath { get; init; }

    public required string DestinationPath { get; init; }

    public required string RelativePath { get; init; }

    public required long Length { get; init; }

    public required DateTime LastWriteTimeUtc { get; init; }

    public required bool NeedsCopy { get; init; }

    /// <summary>The folder mirror scope this file came from, if any.</summary>
    public MirrorScope? MirrorOwner { get; init; }
}

/// <summary>
/// A folder that may be cleaned after a successful copy when mirror mode is on.
/// ExpectedFiles holds absolute destination paths, using a case-insensitive
/// comparer so FAT32/NTFS behave consistently.
/// </summary>
public sealed class MirrorScope
{
    public required string SourceRoot { get; init; }

    public required string DestinationRoot { get; init; }

    public required string SourceName { get; init; }

    public HashSet<string> ExpectedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasEnumerationErrors { get; set; }

    public bool IsSafe { get; set; } = true;
}
