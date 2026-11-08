using System.Text.Json.Serialization;

namespace SyncStick.Core.Models;

/// <summary>
/// One file or folder the user wants to keep mirrored on the target device.
/// </summary>
public sealed class SourceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Absolute path on this PC.</summary>
    public string Path { get; set; } = string.Empty;

    public bool IsFolder { get; set; }

    /// <summary>
    /// Optional destination folder, relative to the device root. Empty means
    /// "put a folder under its own name" for folders and "put the file in the
    /// root" for files.
    /// </summary>
    public string? DestinationSubfolder { get; set; }

    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public string Name
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                return "未命名项目";
            }

            var trimmed = Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var name = System.IO.Path.GetFileName(trimmed);
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            return Path.TrimEnd(':', '\\', '/');
        }
    }

    public SourceItem Clone() => (SourceItem)MemberwiseClone();
}
