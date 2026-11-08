using System.Text.Json;
using System.Text.Json.Serialization;
using SyncStick.Core.Models;

namespace SyncStick.Core.Services;

/// <summary>
/// Loads and atomically saves settings. Data lives in
/// %APPDATA%\SyncStick by default; set SYNCSTICK_DATA_DIR to relocate it
/// (useful for demos and automated tests).
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _gate = new();

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(AppDataDirectory, "settings.json");
    }

    public string FilePath { get; }

    public static string AppDataDirectory
    {
        get
        {
            var overrideDirectory = Environment.GetEnvironmentVariable("SYNCSTICK_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDirectory))
            {
                return Path.GetFullPath(overrideDirectory);
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SyncStick");
        }
    }

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return new AppSettings();
                }

                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                return settings.Normalize();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                TryBackupCorruptFile();
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            settings.Normalize();
            var directory = Path.GetDirectoryName(FilePath)
                ?? throw new InvalidOperationException("配置文件路径无效。");
            Directory.CreateDirectory(directory);

            var temporaryPath = FilePath + ".tmp";
            try
            {
                var json = JsonSerializer.Serialize(settings, JsonOptions);
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
                        // Ignore cleanup failures; the real save already succeeded or failed.
                    }
                }
            }
        }
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            var backup = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(FilePath, backup, overwrite: false);
        }
        catch
        {
            // A read-only or locked settings file should not crash startup.
        }
    }
}
