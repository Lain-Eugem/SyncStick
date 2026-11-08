using SyncStick.Core.Models;

namespace SyncStick.Engine;

/// <summary>
/// Raised when the target device arrives (or the app starts with it connected)
/// and the user should choose which rule set to sync.
/// </summary>
public sealed class RuleSelectionRequestedEventArgs : EventArgs
{
    public RuleSelectionRequestedEventArgs(
        IReadOnlyList<SyncRule> rules,
        string activeRuleId,
        string? lastRuleId,
        string trigger)
    {
        Rules = rules;
        ActiveRuleId = activeRuleId;
        LastRuleId = lastRuleId;
        Trigger = trigger;
    }

    public IReadOnlyList<SyncRule> Rules { get; }

    public string ActiveRuleId { get; }

    /// <summary>Rule used last time with this device, if any.</summary>
    public string? LastRuleId { get; }

    /// <summary>Trigger text, e.g. 插入 U 盘 or 启动时同步.</summary>
    public string Trigger { get; }
}
