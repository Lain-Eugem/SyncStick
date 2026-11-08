namespace SyncStick.Core.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    public List<SourceItem> Sources { get; set; } = new();

    public DeviceIdentity? TargetDevice { get; set; }

    public bool AutoSyncEnabled { get; set; } = true;

    public bool NotifyOnComplete { get; set; } = true;

    public bool MinimizeToTray { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool AutoSyncOnLaunch { get; set; }

    public SyncSettings Sync { get; set; } = new();

    /// <summary>
    /// Preferred UI language: "system" (follow Windows), "zh" or "en".
    /// Stored as plain text so older builds simply ignore it.
    /// </summary>
    public string UiLanguage { get; set; } = "system";

    public AppSettings Normalize()
    {
        Sources = (Sources ?? new List<SourceItem>())
            .Where(source => source is not null)
            .ToList();
        Sync ??= new SyncSettings();
        UiLanguage = NormalizeLanguage(UiLanguage);

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Id) || !seenIds.Add(source.Id))
            {
                source.Id = Guid.NewGuid().ToString("N");
                seenIds.Add(source.Id);
            }

            source.Path ??= string.Empty;
            source.DestinationSubfolder = string.IsNullOrWhiteSpace(source.DestinationSubfolder)
                ? null
                : source.DestinationSubfolder.Trim();
        }

        return this;
    }

    private static string NormalizeLanguage(string? value)
    {
        var language = value?.Trim().ToLowerInvariant();
        return language switch
        {
            "zh" or "zh-cn" or "zh-hans" => "zh",
            "en" or "en-us" or "en-gb" => "en",
            _ => "system"
        };
    }
}
