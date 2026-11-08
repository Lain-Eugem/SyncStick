namespace SyncStick.Core.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;

    /// <summary>
    /// Sources of the active rule set. Kept at the top level so older builds
    /// and the existing sync path keep working; <see cref="Normalize"/> keeps
    /// it pointing at the active rule's list.
    /// </summary>
    public List<SourceItem> Sources { get; set; } = new();

    /// <summary>Named rule sets. Always contains at least one entry.</summary>
    public List<SyncRule> Rules { get; set; } = new();

    /// <summary>Id of the rule set currently used for manual and automatic syncs.</summary>
    public string ActiveRuleId { get; set; } = string.Empty;

    /// <summary>
    /// When more than one rule set exists, ask which one to sync after the
    /// target device is inserted instead of syncing the active one right away.
    /// </summary>
    public bool AskRuleOnInsert { get; set; } = true;

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

        Rules = (Rules ?? new List<SyncRule>())
            .Where(rule => rule is not null)
            .ToList();

        // Settings written by 1.0.x only have the flat Sources list. Move that
        // list into a default rule set without touching the items themselves.
        if (Rules.Count == 0)
        {
            Rules.Add(new SyncRule
            {
                Name = SyncRule.DefaultName,
                Sources = Sources,
                UpdatedAt = DateTimeOffset.Now
            });
        }
        else if (Sources.Count > 0 && Rules.All(rule => rule.Sources.Count == 0))
        {
            Rules[0].Sources = Sources;
        }

        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Rules.Count; index++)
        {
            var rule = Rules[index];
            if (string.IsNullOrWhiteSpace(rule.Id) || !ruleIds.Add(rule.Id))
            {
                rule.Id = Guid.NewGuid().ToString("N");
                ruleIds.Add(rule.Id);
            }

            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                rule.Name = index == 0 ? SyncRule.DefaultName : $"规则 {index + 1}";
            }

            rule.Name = rule.Name.Trim();
            rule.Sources = (rule.Sources ?? new List<SourceItem>())
                .Where(source => source is not null)
                .ToList();
            if (rule.UpdatedAt == default)
            {
                rule.UpdatedAt = DateTimeOffset.Now;
            }

            foreach (var source in rule.Sources)
            {
                if (string.IsNullOrWhiteSpace(source.Id) || !sourceIds.Add(source.Id))
                {
                    source.Id = Guid.NewGuid().ToString("N");
                    sourceIds.Add(source.Id);
                }

                source.Path ??= string.Empty;
                source.DestinationSubfolder = string.IsNullOrWhiteSpace(source.DestinationSubfolder)
                    ? null
                    : source.DestinationSubfolder.Trim();
            }
        }

        var active = Rules.FirstOrDefault(rule =>
                         string.Equals(rule.Id, ActiveRuleId, StringComparison.Ordinal))
                     ?? Rules[0];
        ActiveRuleId = active.Id;
        Sources = active.Sources;

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
