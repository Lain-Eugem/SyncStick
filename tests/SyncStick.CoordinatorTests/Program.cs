using SyncStick.Core.Models;
using SyncStick.Core.Services;
using SyncStick.Engine;

namespace SyncStick.CoordinatorTests;

internal static class Program
{
    private static readonly List<string> Failures = new();
    private static int _passed;

    private static async Task<int> Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("SyncStick coordinator / device tests");
        Console.WriteLine("====================================");

        await Run("device serial match", DeviceSerialMatch);
        await Run("different serial never uses label fallback", DifferentSerialDoesNotFallback);
        await Run("device fallback match", DeviceFallbackMatch);
        await Run("ambiguous fallback match", AmbiguousFallbackMatch);
        await Run("legacy settings migrate to a default rule", LegacySettingsMigrateToDefaultRule);
        await Run("auto sync after simulated insertion", AutoSyncAfterInsertion);
        await Run("multiple rules prompt before auto sync", MultipleRulesPromptOnInsert);
        await Run("multiple rules use the last rule when not asking", MultipleRulesUseLastRuleWhenNotAsking);
        await Run("auto sync respects paused setting", AutoSyncPaused);
        await Run("missing source records a failure", MissingSourceRecordsFailure);

        Console.WriteLine();
        Console.WriteLine($"Passed: {_passed}, failed: {Failures.Count}");
        foreach (var failure in Failures)
        {
            Console.WriteLine("  FAIL " + failure);
        }

        return Failures.Count == 0 ? 0 : 1;
    }

    private static async Task Run(string name, Func<Task> test)
    {
        try
        {
            await test();
            _passed++;
            Console.WriteLine($"  PASS  {name}");
        }
        catch (Exception ex)
        {
            Failures.Add($"{name}: {ex.Message}");
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string NewTempDirectory(string label)
    {
        var path = Path.Combine(Path.GetTempPath(), "syncstick-coordinator-tests", $"{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTree(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static VolumeInfo CreateVolume(string rootPath, string label, string serial)
    {
        var drive = new DriveInfo(Path.GetPathRoot(rootPath)!);
        return new VolumeInfo
        {
            RootPath = rootPath,
            DriveLetter = rootPath.TrimEnd('\\'),
            Label = label,
            FileSystem = "NTFS",
            TotalSizeBytes = Math.Max(drive.TotalSize, 1024L * 1024 * 1024),
            FreeSpaceBytes = Math.Max(drive.AvailableFreeSpace, 8L * 1024 * 1024 * 1024),
            IsRemovable = true,
            VolumeSerial = serial
        };
    }

    private static Task DeviceSerialMatch()
    {
        var volume = new VolumeInfo
        {
            RootPath = @"E:\",
            DriveLetter = "E:",
            Label = "STICK",
            FileSystem = "FAT32",
            TotalSizeBytes = 64_000_000_000,
            FreeSpaceBytes = 32_000_000_000,
            IsRemovable = true,
            VolumeSerial = "A1B2C3D4"
        };

        var identity = new DeviceIdentity
        {
            VolumeSerial = "a1b2c3d4",
            Label = "different-label",
            TotalSizeBytes = 1
        };

        var match = DeviceMatcher.Match(identity, new[] { volume });
        Assert(match.Device?.RootPath == @"E:\", "serial should win over label");
        return Task.CompletedTask;
    }

    private static Task DifferentSerialDoesNotFallback()
    {
        var volume = new VolumeInfo
        {
            RootPath = @"E:\",
            DriveLetter = "E:",
            Label = "STICK",
            FileSystem = "FAT32",
            TotalSizeBytes = 64_000_000_000,
            FreeSpaceBytes = 32_000_000_000,
            IsRemovable = true,
            VolumeSerial = "BBBBBBBB"
        };

        var identity = new DeviceIdentity
        {
            VolumeSerial = "AAAAAAAA",
            Label = "STICK",
            TotalSizeBytes = 64_000_000_000,
            LastDriveLetter = "E:"
        };

        var match = DeviceMatcher.Match(identity, new[] { volume });
        Assert(match.Device is null, "a different serial must not fall back to label + size");
        return Task.CompletedTask;
    }

    private static Task DeviceFallbackMatch()
    {
        var volume = new VolumeInfo
        {
            RootPath = @"E:\",
            DriveLetter = "E:",
            Label = "STICK",
            FileSystem = "FAT32",
            TotalSizeBytes = 64_000_000_000,
            FreeSpaceBytes = 32_000_000_000,
            IsRemovable = true,
            VolumeSerial = string.Empty
        };

        var identity = new DeviceIdentity
        {
            Label = "STICK",
            TotalSizeBytes = 64_000_000_000,
            LastDriveLetter = "E:"
        };

        var match = DeviceMatcher.Match(identity, new[] { volume });
        Assert(match.Device?.RootPath == @"E:\", "label + size fallback should match");
        return Task.CompletedTask;
    }

    private static Task AmbiguousFallbackMatch()
    {
        var volumes = new[]
        {
            new VolumeInfo
            {
                RootPath = @"E:\",
                DriveLetter = "E:",
                Label = "STICK",
                FileSystem = "FAT32",
                TotalSizeBytes = 64_000_000_000,
                FreeSpaceBytes = 1,
                IsRemovable = true,
                VolumeSerial = string.Empty
            },
            new VolumeInfo
            {
                RootPath = @"F:\",
                DriveLetter = "F:",
                Label = "STICK",
                FileSystem = "FAT32",
                TotalSizeBytes = 64_000_000_000,
                FreeSpaceBytes = 1,
                IsRemovable = true,
                VolumeSerial = string.Empty
            }
        };

        var identity = new DeviceIdentity
        {
            Label = "STICK",
            TotalSizeBytes = 64_000_000_000
        };

        var match = DeviceMatcher.Match(identity, volumes);
        Assert(match.Device is null, "two identical fallback candidates must be ambiguous");
        Assert(match.IsAmbiguous && match.AmbiguousCandidates.Count == 2, "expected both candidates");
        return Task.CompletedTask;
    }

    private static Task LegacySettingsMigrateToDefaultRule()
    {
        var root = NewTempDirectory("rule-migration");
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");
            File.WriteAllText(settingsPath, """
            {
              "schemaVersion": 1,
              "sources": [
                { "id": "abc123", "path": "C:\\data", "isFolder": true, "destinationSubfolder": "Backup", "enabled": true }
              ],
              "autoSyncEnabled": true,
              "notifyOnComplete": true,
              "minimizeToTray": true,
              "startWithWindows": false,
              "autoSyncOnLaunch": false,
              "sync": { "smartOverwrite": true, "mirrorDelete": false, "ignoreSystemFiles": true, "copyTimestamps": false },
              "uiLanguage": "system"
            }
            """);

            var settings = new SettingsStore(settingsPath).Load();
            Assert(settings.Rules.Count == 1, $"expected one migrated rule, got {settings.Rules.Count}");
            var rule = settings.Rules[0];
            Assert(rule.Name == SyncRule.DefaultName, $"unexpected rule name: {rule.Name}");
            Assert(rule.Sources.Count == 1, "migrated rule should keep the source list");
            Assert(rule.Sources[0].Path == @"C:\data", "source path changed during migration");
            Assert(rule.Sources[0].DestinationSubfolder == "Backup", "destination changed during migration");
            Assert(settings.ActiveRuleId == rule.Id, "active rule should point at the migrated rule");
            Assert(settings.Sources.Count == 1 && settings.Sources[0].Path == @"C:\data",
                "top-level sources should mirror the active rule");
        }
        finally
        {
            DeleteTree(root);
        }

        return Task.CompletedTask;
    }

    private static async Task MultipleRulesPromptOnInsert()
    {
        var root = NewTempDirectory("rule-prompt");
        var sourceA = Path.Combine(root, "Alpha");
        var sourceB = Path.Combine(root, "Beta");
        var target = Path.Combine(root, "usb");
        Directory.CreateDirectory(sourceA);
        Directory.CreateDirectory(sourceB);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(sourceA, "alpha.txt"), "alpha");
        File.WriteAllText(Path.Combine(sourceB, "beta.txt"), "beta");

        var volume = CreateVolume(target, "TESTUSB", "CAFEABCD");
        var settings = new AppSettings
        {
            AutoSyncEnabled = true,
            AskRuleOnInsert = true,
            TargetDevice = DeviceMatcher.CreateIdentity(volume)
        };
        var ruleA = new SyncRule { Name = "Alpha" };
        ruleA.Sources.Add(new SourceItem { Path = sourceA, IsFolder = true, DestinationSubfolder = "Alpha" });
        var ruleB = new SyncRule { Name = "Beta" };
        ruleB.Sources.Add(new SourceItem { Path = sourceB, IsFolder = true, DestinationSubfolder = "Beta" });
        settings.Rules.Add(ruleA);
        settings.Rules.Add(ruleB);
        settings.ActiveRuleId = ruleA.Id;

        var settingsStore = new SettingsStore(Path.Combine(root, "settings.json"));
        settingsStore.Save(settings);
        var fake = new FakeDeviceService();
        using var coordinator = new SyncCoordinator(settingsStore, new ActivityStore(Path.Combine(root, "activity.json")), fake);

        var requested = new TaskCompletionSource<RuleSelectionRequestedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.RuleSelectionRequested += (_, args) => requested.TrySetResult(args);

        try
        {
            coordinator.Start();
            await Task.Delay(250);
            fake.SetVolumes(volume);

            var args = await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert(args.Rules.Count == 2, $"expected two rules in the prompt, got {args.Rules.Count}");
            Assert(args.ActiveRuleId == ruleA.Id, "prompt should carry the active rule");
            Assert(args.LastRuleId is null, $"no last rule expected, got {args.LastRuleId}");
            Assert(args.Trigger == "插入 U 盘", $"unexpected trigger: {args.Trigger}");
            Assert(coordinator.IsRuleSelectionPending, "coordinator should wait for a rule choice");

            await Task.Delay(3000);
            Assert(!File.Exists(Path.Combine(target, "Alpha", "alpha.txt")), "nothing should sync before choosing a rule");
            Assert(!File.Exists(Path.Combine(target, "Beta", "beta.txt")), "nothing should sync before choosing a rule");

            coordinator.CompleteRuleSelection(ruleB.Id);
            Assert(!coordinator.IsRuleSelectionPending, "pending state should clear after the choice");
            Assert(coordinator.ActiveRule?.Id == ruleB.Id, "chosen rule should become active");
            Assert(coordinator.Settings.TargetDevice?.LastRuleId == ruleB.Id,
                "chosen rule should be remembered for the device");

            var record = await coordinator.SyncNowAsync("插入 U 盘");
            Assert(record?.Success == true, "manual sync after the choice should succeed");
            Assert(File.Exists(Path.Combine(target, "Beta", "beta.txt")), "chosen rule should write its file");
            Assert(!File.Exists(Path.Combine(target, "Alpha", "alpha.txt")), "other rules must not write");
        }
        finally
        {
            coordinator.Dispose();
            DeleteTree(root);
        }
    }

    private static async Task MultipleRulesUseLastRuleWhenNotAsking()
    {
        var root = NewTempDirectory("rule-last");
        var sourceA = Path.Combine(root, "Alpha");
        var sourceB = Path.Combine(root, "Beta");
        var target = Path.Combine(root, "usb");
        Directory.CreateDirectory(sourceA);
        Directory.CreateDirectory(sourceB);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(sourceA, "alpha.txt"), "alpha");
        File.WriteAllText(Path.Combine(sourceB, "beta.txt"), "beta");

        var volume = CreateVolume(target, "TESTUSB", "CAFEBEEF");
        var settings = new AppSettings
        {
            AutoSyncEnabled = true,
            AskRuleOnInsert = false,
            TargetDevice = DeviceMatcher.CreateIdentity(volume)
        };
        var ruleA = new SyncRule { Name = "Alpha" };
        ruleA.Sources.Add(new SourceItem { Path = sourceA, IsFolder = true, DestinationSubfolder = "Alpha" });
        var ruleB = new SyncRule { Name = "Beta" };
        ruleB.Sources.Add(new SourceItem { Path = sourceB, IsFolder = true, DestinationSubfolder = "Beta" });
        settings.Rules.Add(ruleA);
        settings.Rules.Add(ruleB);
        settings.ActiveRuleId = ruleA.Id;
        settings.TargetDevice!.LastRuleId = ruleB.Id;

        var settingsStore = new SettingsStore(Path.Combine(root, "settings.json"));
        settingsStore.Save(settings);
        var fake = new FakeDeviceService();
        using var coordinator = new SyncCoordinator(settingsStore, new ActivityStore(Path.Combine(root, "activity.json")), fake);

        var finished = new TaskCompletionSource<SyncRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompted = false;
        coordinator.RuleSelectionRequested += (_, _) => prompted = true;
        coordinator.SyncFinished += (_, record) =>
        {
            if (record.Success)
            {
                finished.TrySetResult(record);
            }
        };

        try
        {
            coordinator.Start();
            await Task.Delay(250);
            fake.SetVolumes(volume);

            var record = await finished.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert(!prompted, "no prompt expected when asking is off");
            Assert(record.FilesCopied == 1, $"expected one copied file, got {record.FilesCopied}");
            Assert(coordinator.ActiveRule?.Id == ruleB.Id, "last rule for the device should be used");
            Assert(File.Exists(Path.Combine(target, "Beta", "beta.txt")), "last rule should write its file");
            Assert(!File.Exists(Path.Combine(target, "Alpha", "alpha.txt")), "other rules must not write");
            Assert(coordinator.Settings.TargetDevice?.LastRuleId == ruleB.Id,
                "last rule should stay remembered for the device");
        }
        finally
        {
            coordinator.Dispose();
            DeleteTree(root);
        }
    }

    private static async Task AutoSyncAfterInsertion()
    {
        var root = NewTempDirectory("auto-sync");
        var settingsPath = Path.Combine(root, "settings.json");
        var activityPath = Path.Combine(root, "activity.json");
        var source = Path.Combine(root, "Project");
        var target = Path.Combine(root, "usb");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "hello.txt"), "hello");
        File.WriteAllText(Path.Combine(source, "nested", "data.txt"), "data");

        var volume = CreateVolume(target, "TESTUSB", "CAFE1234");
        var settings = new AppSettings
        {
            AutoSyncEnabled = true,
            TargetDevice = DeviceMatcher.CreateIdentity(volume)
        };
        settings.Sources.Add(new SourceItem
        {
            Path = source,
            IsFolder = true,
            DestinationSubfolder = "Backup"
        });

        var settingsStore = new SettingsStore(settingsPath);
        settingsStore.Save(settings);
        var fake = new FakeDeviceService();
        using var coordinator = new SyncCoordinator(settingsStore, new ActivityStore(activityPath), fake);

        var finished = new TaskCompletionSource<SyncRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notices = new List<string>();
        coordinator.SyncFinished += (_, record) =>
        {
            if (record.Success)
            {
                finished.TrySetResult(record);
            }
        };
        coordinator.Notice += (_, args) => notices.Add(args.Message);

        try
        {
            coordinator.Start();
            await Task.Delay(250);
            fake.SetVolumes(volume);

            var record = await finished.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert(record.FilesCopied == 2, $"expected 2 copied files, got {record.FilesCopied}");
            Assert(record.BytesCopied > 0, "expected a non-zero byte count");
            Assert(File.Exists(Path.Combine(target, "Backup", "hello.txt")), "hello.txt missing after auto sync");
            Assert(File.Exists(Path.Combine(target, "Backup", "nested", "data.txt")), "nested file missing after auto sync");

            for (var i = 0; i < 30 && !notices.Any(message => message.Contains("同步完成")); i++)
            {
                await Task.Delay(100);
            }

            Assert(notices.Any(message => message.Contains("同步完成")), "expected completion notice");

            await Task.Delay(2200);
            var records = new ActivityStore(activityPath).Load();
            Assert(records.Count == 1, $"expected exactly one auto sync record, got {records.Count}");
        }
        finally
        {
            coordinator.Dispose();
            DeleteTree(root);
        }
    }

    private static async Task AutoSyncPaused()
    {
        var root = NewTempDirectory("auto-paused");
        var settingsPath = Path.Combine(root, "settings.json");
        var activityPath = Path.Combine(root, "activity.json");
        var source = Path.Combine(root, "Project");
        var target = Path.Combine(root, "usb");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "hello.txt"), "hello");

        var volume = CreateVolume(target, "TESTUSB", "CAFE5678");
        var settings = new AppSettings
        {
            AutoSyncEnabled = false,
            TargetDevice = DeviceMatcher.CreateIdentity(volume)
        };
        settings.Sources.Add(new SourceItem
        {
            Path = source,
            IsFolder = true,
            DestinationSubfolder = "Backup"
        });

        var settingsStore = new SettingsStore(settingsPath);
        settingsStore.Save(settings);
        var fake = new FakeDeviceService();
        using var coordinator = new SyncCoordinator(settingsStore, new ActivityStore(activityPath), fake);

        try
        {
            coordinator.Start();
            await Task.Delay(200);
            fake.SetVolumes(volume);
            await Task.Delay(3000);
            Assert(!File.Exists(Path.Combine(target, "Backup", "hello.txt")), "paused auto sync must not copy");

            var record = await coordinator.SyncNowAsync("手动同步");
            Assert(record?.Success == true, "manual sync should succeed");
            Assert(File.Exists(Path.Combine(target, "Backup", "hello.txt")), "manual sync did not copy");
        }
        finally
        {
            coordinator.Dispose();
            DeleteTree(root);
        }
    }

    private static async Task MissingSourceRecordsFailure()
    {
        var root = NewTempDirectory("missing-source");
        var target = Path.Combine(root, "usb");
        Directory.CreateDirectory(target);
        var volume = CreateVolume(target, "TESTUSB", "CAFE9999");

        var settings = new AppSettings
        {
            AutoSyncEnabled = false,
            TargetDevice = DeviceMatcher.CreateIdentity(volume)
        };
        settings.Sources.Add(new SourceItem
        {
            Path = Path.Combine(root, "gone"),
            IsFolder = true,
            DestinationSubfolder = "Backup"
        });

        var settingsStore = new SettingsStore(Path.Combine(root, "settings.json"));
        settingsStore.Save(settings);
        var fake = new FakeDeviceService();
        fake.SetVolumes(volume);
        using var coordinator = new SyncCoordinator(settingsStore, new ActivityStore(Path.Combine(root, "activity.json")), fake);
        coordinator.Start();
        await Task.Delay(300);

        try
        {
            var record = await coordinator.SyncNowAsync("手动同步");
            Assert(record is not null, "expected a failure record");
            Assert(record!.Success == false, "missing source should be recorded as failure");
            Assert(record.Error?.Contains("来源不存在") == true, $"unexpected error: {record.Error}");
            Assert(!File.Exists(Path.Combine(target, "Backup", "hello.txt")), "nothing should be copied");
        }
        finally
        {
            coordinator.Dispose();
            DeleteTree(root);
        }
    }
}

internal sealed class FakeDeviceService : IDeviceService
{
    private readonly object _gate = new();
    private IReadOnlyList<VolumeInfo> _volumes = Array.Empty<VolumeInfo>();

    public void SetVolumes(params VolumeInfo[] volumes)
    {
        lock (_gate)
        {
            _volumes = volumes.ToArray();
        }
    }

    public IReadOnlyList<VolumeInfo> EnumerateVolumes()
    {
        lock (_gate)
        {
            return _volumes.ToArray();
        }
    }
}
