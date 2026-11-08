using System.Text.Json;
using SyncStick.Core.Models;

namespace SyncStick.Core.Services;

/// <summary>Persists the last 60 sync summaries for the activity page.</summary>
public sealed class ActivityStore
{
    private const int MaxRecords = 60;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();

    public ActivityStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(SettingsStore.AppDataDirectory, "activity.json");
    }

    public string FilePath { get; }

    public List<SyncRecord> Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return new List<SyncRecord>();
                }

                var json = File.ReadAllText(FilePath);
                var records = JsonSerializer.Deserialize<List<SyncRecord>>(json, JsonOptions);
                return records?.Where(record => record is not null).ToList() ?? new List<SyncRecord>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return new List<SyncRecord>();
            }
        }
    }

    public List<SyncRecord> Add(SyncRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            var records = Load();
            records.Insert(0, record);
            if (records.Count > MaxRecords)
            {
                records.RemoveRange(MaxRecords, records.Count - MaxRecords);
            }

            Save(records);
            return records;
        }
    }

    public List<SyncRecord> Clear()
    {
        lock (_gate)
        {
            Save(new List<SyncRecord>());
            return new List<SyncRecord>();
        }
    }

    private void Save(List<SyncRecord> records)
    {
        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("活动记录路径无效。");
        Directory.CreateDirectory(directory);

        var temporaryPath = FilePath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(records, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Nothing useful to do if cleanup fails.
                }
            }
        }
    }
}
