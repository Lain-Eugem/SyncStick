namespace SyncStick.Core.Models;

/// <summary>
/// A named set of sync items. Different devices can use different rule sets,
/// and the user can pick one when a USB drive is inserted.
/// </summary>
public sealed class SyncRule
{
    public const string DefaultName = "默认规则";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public List<SourceItem> Sources { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public SyncRule Clone(string? name = null, bool regenerateSourceIds = false)
    {
        var copy = new SyncRule
        {
            Name = name ?? Name,
            UpdatedAt = DateTimeOffset.Now,
            Sources = Sources.Select(source =>
            {
                var clone = source.Clone();
                if (regenerateSourceIds)
                {
                    clone.Id = Guid.NewGuid().ToString("N");
                }

                return clone;
            }).ToList()
        };

        return copy;
    }
}
