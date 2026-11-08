using SyncStick.Core;
using SyncStick.Core.Models;
using SyncStick.Core.Services;

namespace SyncStick.Tests;

internal static class Program
{
    private static readonly List<string> Failures = new();
    private static int _passed;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("SyncStick Core tests");
        Console.WriteLine("====================");

        await Run("folder sync writes nested files", FolderSyncWritesNestedFiles);
        await Run("smart overwrite skips unchanged files", SmartOverwriteSkipsUnchanged);
        await Run("smart overwrite rewrites changed files", SmartOverwriteRewritesChanged);
        await Run("full overwrite rewrites identical files", FullOverwriteRewritesIdentical);
        await Run("file source lands in root", FileSourceLandsInRoot);
        await Run("destination subfolder is honored", DestinationSubfolderIsHonored);
        await Run("system junk files are ignored", SystemJunkFilesAreIgnored);
        await Run("timestamps are preserved", TimestampsArePreserved);
        await Run("mirror delete removes stale files", MirrorDeleteRemovesStaleFiles);
        await Run("mirror delete is disabled for overlapping scopes", MirrorDeleteDisabledForOverlap);
        await Run("path traversal is rejected", PathTraversalIsRejected);
        await Run("source inside target is rejected", SourceInsideTargetIsRejected);
        await Run("missing source fails before copying", MissingSourceFailsBeforeCopying);
        await Run("invalid destination root fails cleanly", InvalidDestinationRootFails);
        await Run("cancel stops a sync and leaves no temp files", CancelLeavesNoTempFiles);
        await Run("settings round-trip", SettingsRoundTrip);
        await Run("corrupt settings file is quarantined", CorruptSettingsFileIsQuarantined);
        await Run("activity store trims to 60", ActivityStoreTrims);

        if (args.Length >= 2 && args[0].Equals("--usb", StringComparison.OrdinalIgnoreCase))
        {
            await Run($"real USB write/readback ({args[1]})", () => UsbRoundTrip(args[1]));
        }

        Console.WriteLine();
        Console.WriteLine($"Passed: {_passed}, failed: {Failures.Count}");
        foreach (var failure in Failures)
        {
            Console.WriteLine($"  FAIL {failure}");
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
        var path = Path.Combine(Path.GetTempPath(), "syncstick-tests", $"{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTree(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static SourceItem FolderSource(string path, string? subfolder = null) => new()
    {
        Path = path,
        IsFolder = true,
        DestinationSubfolder = subfolder
    };

    private static SourceItem FileSource(string path, string? subfolder = null) => new()
    {
        Path = path,
        IsFolder = false,
        DestinationSubfolder = subfolder
    };

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string ReadFile(string path) => File.ReadAllText(path);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertFileEquals(string path, string expected)
    {
        Assert(File.Exists(path), $"Expected file to exist: {path}");
        Assert(ReadFile(path) == expected, $"Unexpected content in {path}");
    }

    private static async Task FolderSyncWritesNestedFiles()
    {
        var root = NewTempDirectory("nested");
        try
        {
            var source = Path.Combine(root, "Project");
            WriteFile(Path.Combine(source, "README.md"), "hello");
            WriteFile(Path.Combine(source, "src", "main.cs"), "code");
            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(target);
            var engine = new SyncEngine();
            var plan = engine.BuildPlan(new[] { FolderSource(source) }, target, new SyncSettings());
            Assert(plan.FilesToCopy == 2, "expected 2 planned files");
            var result = await engine.ExecuteAsync(plan, new SyncSettings());
            Assert(result.Success, "sync should succeed");
            Assert(result.FilesCopied == 2, "expected 2 copied files");
            Assert(result.BytesCopied == 9, $"expected 9 bytes copied, got {result.BytesCopied}");
            AssertFileEquals(Path.Combine(target, "Project", "README.md"), "hello");
            AssertFileEquals(Path.Combine(target, "Project", "src", "main.cs"), "code");
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static async Task SmartOverwriteSkipsUnchanged()
    {
        var root = NewTempDirectory("unchanged");
        try
        {
            var source = Path.Combine(root, "Project");
            WriteFile(Path.Combine(source, "a.txt"), "same");
            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(target);
            var engine = new SyncEngine();
            var settings = new SyncSettings { SmartOverwrite = true };

            var first = engine.BuildPlan(new[] { FolderSource(source) }, target, settings);
            await engine.ExecuteAsync(first, settings);

            var second = engine.BuildPlan(new[] { FolderSource(source) }, target, settings);
            Assert(second.FilesToCopy == 0, "unchanged file should not be copied");
            var result = await engine.ExecuteAsync(second, settings);
            Assert(result.Success, "no-work sync should succeed");
            Assert(result.FilesSkipped == 1, "skipped count should be 1");
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static async Task SmartOverwriteRewritesChanged()
    {
        var root = NewTempDirectory("changed");
        try
        {
            var source = Path.Combine(root, "Project");
            WriteFile(Path.Combine(source, "a.txt"), "first");
            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(target);
            var engine = new SyncEngine();
            var settings = new SyncSettings { SmartOverwrite = true };

            await engine.ExecuteAsync(engine.BuildPlan(new[] { FolderSource(source) }, target, settings), settings);
            await Task.Delay(20);
            WriteFile(Path.Combine(source, "a.txt"), "second-version");
            File.SetLastWriteTimeUtc(Path.Combine(source, "a.txt"), DateTime.UtcNow.AddMinutes(1));

            var plan = engine.BuildPlan(new[] { FolderSource(source) }, target, settings);
            Assert(plan.FilesToCopy == 1, "changed file should be planned");
            await engine.ExecuteAsync(plan, settings);
            AssertFileEquals(Path.Combine(target, "Project", "a.txt"), "second-version");
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static async Task FullOverwriteRewritesIdentical()
    {
        var root = NewTempDirectory("full-overwrite");
        try
        {
            var source = Path.Combine(root, "Project");
            WriteFile(Path.Combine(source, "a.txt"), "same");
            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(target);
            var engine = new SyncEngine();
            var settings = new SyncSettings { SmartOverwrite = false };

            await engine.ExecuteAsync(engine.BuildPlan(new[] { FolderSource(source) }, target, settings), settings);
            var plan = engine.BuildPlan(new[] { FolderSource(source) }, target, settings);
            Assert(plan.FilesToCopy == 1, "full overwrite should always copy");
            await engine.ExecuteAsync(plan, settings);

            AssertFileEquals(Path.Combine(target, "Project", "a.txt"), "same");
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static async Task FileSourceLandsInRoot()
    {
        var root = NewTempDirectory("file-source");
        try
        {
            var file = Path.Combine(root, "notes.txt");
            File.WriteAllText(file, "notes");
            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(target);
            var engine = new SyncEngine();
            var settings = new SyncSettings();
            var plan = engine.BuildPlan(new[] { FileSource(file) }, target, settings);
            await engine.ExecuteAsync(plan, settings);
            AssertFileEquals(Path.Combine(target, "notes.txt"), "notes");
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static async Task DestinationSubfolderIsHonored()
    {
        var root = NewTempDirectory("subfolder");
        try
        {
            var source = Path.Combine(root, "Project");
            WriteFile(Path.Combine(source, "a.txt"), "a");
            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(target);
            var engine = new SyncEngine();
            var settings = new SyncSettings();
            var plan = engine.BuildPlan(
                new[] { FolderSource(source, @"Backup\2026\Project") },
                target,
                settings);
            await engine.ExecuteAsync(plan, settings);
            AssertFileEquals(Path.Combine(target, "Backup", "2026", "Project", "a.txt"), "a");
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static async Task SystemJunkFilesAreIgnored()
    {
        var root = NewTempDirectory("junk");
        try
        {
            var source = Path.Combine(root, "Project");
            WriteFile(Path.Combine(source, "keep.txt"), "keep");
            WriteFile(Path.Combine(source, "Thumbs.db"), "junk");
            WriteFile(Path.Combine(source, "~$notes.docx"), "junk");
            WriteFile(Path.Combine(source, ".DS_Store"), "junk");
            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(target);
            var engine = new SyncEngine();
            var settings = new SyncSettings { IgnoreSystemFiles = true };
            var plan = engine.BuildPlan(new[] { FolderSource(source) }, target, settings);
            Assert(plan.FilesToCopy == 1, "only one real file should be planned");
            await engine.ExecuteAsync(plan, settings);
            Assert(File.Exists(Path.Combine(target, "Project", "keep.txt")), "real file missing");
            Assert(!File.Exists(Path.Combine(target, "Project", "Thumbs.db")), "junk file was copied");
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static async Task TimestampsArePreserved()
    {
        var root = NewTempDirectory("timestamps");
        try
        {
            var source = Path.Combine(root, "Project");
            var file = Path.Combine(source, "a.txt");
            WriteFile(file, "content");
            var stamp = new DateTime(2024, 5, 6, 7, 8, 10, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(file, stamp);
            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(target);
            var engine = new SyncEngine();
            var settings = new SyncSettings { CopyTimestamps = true };
            await engine.ExecuteAsync(engine.BuildPlan(new[] { FolderSource(source) }, target, settings), settings);
            var copied = File.GetLastWriteTimeUtc(Path.Combine(target, "Project", "a.txt"));
            Assert(Math.Abs((copied - stamp).TotalSeconds) <= 3, $"timestamp drifted: {copied:O}");
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static async Task MirrorDeleteRemovesStaleFiles()
    {
        var root = NewTempDirectory("mirror");
        try
        {
            var source = Path.Combine(root, "Project");
            WriteFile(Path.Combine(source, "keep.txt"), "keep");
            WriteFile(Path.Combine(source, "remove.txt"), "remove");
            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(target);
            var engine = new SyncEngine();
            var settings = new SyncSettings { MirrorDelete = true };

            await engine.ExecuteAsync(engine.BuildPlan(new[] { FolderSource(source) }, target, settings), settings);
            File.Delete(Path.Combine(source, "remove.txt"));

            var plan = engine.BuildPlan(new[] { FolderSource(source) }, target, settings);
            var result = await engine.ExecuteAsync(plan, settings);
            Assert(result.Success, "mirror sync should succeed");
            Assert(!File.Exists(Path.Combine(target, "Project", "remove.txt")), "stale file should be deleted");
            Assert(File.Exists(Path.Combine(target, "Project", "keep.txt")), "kept file missing");
            Assert(result.FilesDeleted == 1, $"expected 1 deletion, got {result.FilesDeleted}");
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static async Task MirrorDeleteDisabledForOverlap()
    {
        var root = NewTempDirectory("mirror-overlap");
        try
        {
            var parentA = Path.Combine(root, "a");
            var parentB = Path.Combine(root, "b");
            var sourceA = Path.Combine(parentA, "Project");
            var sourceB = Path.Combine(parentB, "Project");
            WriteFile(Path.Combine(sourceA, "a.txt"), "a");
            WriteFile(Path.Combine(sourceB, "b.txt"), "b");
            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(Path.Combine(target, "Project"));
            File.WriteAllText(Path.Combine(target, "Project", "stale.txt"), "stale");

            var engine = new SyncEngine();
            var settings = new SyncSettings { MirrorDelete = true };
            var plan = engine.BuildPlan(new[] { FolderSource(sourceA), FolderSource(sourceB) }, target, settings);
            Assert(plan.MirrorScopes.Count == 2, "expected two mirror scopes");
            Assert(plan.MirrorScopes.All(scope => !scope.IsSafe), "overlapping scopes must be unsafe");
            var result = await engine.ExecuteAsync(plan, settings);
            Assert(File.Exists(Path.Combine(target, "Project", "stale.txt")), "stale file must not be deleted when safety is off");
            Assert(result.Warnings.Count > 0, "expected a safety warning");
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static Task PathTraversalIsRejected()
    {
        var root = NewTempDirectory("traversal");
        try
        {
            var source = Path.Combine(root, "Project");
            WriteFile(Path.Combine(source, "a.txt"), "a");
            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(target);
            var engine = new SyncEngine();
            try
            {
                engine.BuildPlan(new[] { FolderSource(source, @"..\outside") }, target, new SyncSettings());
                throw new InvalidOperationException("expected SyncStickException");
            }
            catch (SyncStickException ex)
            {
                Assert(ex.Message.Contains("目标子目录"), $"unexpected message: {ex.Message}");
            }

            return Task.CompletedTask;
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static Task SourceInsideTargetIsRejected()
    {
        var root = NewTempDirectory("source-inside-target");
        try
        {
            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(target);
            var source = Path.Combine(target, "Project");
            WriteFile(Path.Combine(source, "a.txt"), "a");
            var engine = new SyncEngine();
            try
            {
                engine.BuildPlan(new[] { FolderSource(source) }, target, new SyncSettings());
                throw new InvalidOperationException("expected SyncStickException");
            }
            catch (SyncStickException ex)
            {
                Assert(ex.Message.Contains("目标设备内部"), $"unexpected message: {ex.Message}");
            }

            return Task.CompletedTask;
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static Task MissingSourceFailsBeforeCopying()
    {
        var root = NewTempDirectory("missing-source");
        try
        {
            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(target);
            var engine = new SyncEngine();
            try
            {
                engine.BuildPlan(
                    new[] { FolderSource(Path.Combine(root, "gone")) },
                    target,
                    new SyncSettings());
                throw new InvalidOperationException("expected SyncStickException");
            }
            catch (SyncStickException ex)
            {
                Assert(ex.Message.Contains("来源不存在"), $"unexpected message: {ex.Message}");
            }

            return Task.CompletedTask;
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static Task InvalidDestinationRootFails()
    {
        var engine = new SyncEngine();
        var missing = Path.Combine(Path.GetTempPath(), "syncstick-missing-" + Guid.NewGuid().ToString("N"));
        try
        {
            engine.BuildPlan(Array.Empty<SourceItem>(), missing, new SyncSettings());
            throw new InvalidOperationException("expected DirectoryNotFoundException");
        }
        catch (DirectoryNotFoundException)
        {
            return Task.CompletedTask;
        }
    }

    private static async Task CancelLeavesNoTempFiles()
    {
        var root = NewTempDirectory("cancel");
        try
        {
            var source = Path.Combine(root, "Project");
            Directory.CreateDirectory(source);
            var bigFile = Path.Combine(source, "big.bin");
            await using (var stream = File.Create(bigFile))
            {
                stream.SetLength(64 * 1024 * 1024);
            }

            var target = Path.Combine(root, "usb");
            Directory.CreateDirectory(target);
            var engine = new SyncEngine();
            var settings = new SyncSettings();
            var plan = engine.BuildPlan(new[] { FolderSource(source) }, target, settings);
            using var cts = new CancellationTokenSource();
            var progress = new InlineProgress<SyncProgress>(_ => cts.Cancel());
            var result = await engine.ExecuteAsync(plan, settings, progress, cancellationToken: cts.Token);
            Assert(result.Cancelled, "result should be cancelled");
            var tempFiles = Directory.Exists(Path.Combine(target, "Project"))
                ? Directory.GetFiles(Path.Combine(target, "Project"), "*.tmp", SearchOption.AllDirectories)
                : Array.Empty<string>();
            Assert(tempFiles.Length == 0, $"temporary files left behind: {tempFiles.Length}");
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static Task SettingsRoundTrip()
    {
        var directory = NewTempDirectory("settings");
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            var settings = new AppSettings
            {
                AutoSyncEnabled = false,
                TargetDevice = new DeviceIdentity
                {
                    VolumeSerial = "ABC123",
                    Label = "TESTUSB",
                    TotalSizeBytes = 32_000_000_000
                }
            };
            settings.Sources.Add(FolderSource(@"C:\Some\Project", "Backup"));
            store.Save(settings);
            var loaded = store.Load();
            Assert(!loaded.AutoSyncEnabled, "auto sync flag did not persist");
            Assert(loaded.Sources.Count == 1, "source did not persist");
            Assert(loaded.TargetDevice?.VolumeSerial == "ABC123", "device did not persist");
            return Task.CompletedTask;
        }
        finally
        {
            DeleteTree(directory);
        }
    }

    private static Task CorruptSettingsFileIsQuarantined()
    {
        var directory = NewTempDirectory("corrupt-settings");
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{ this is not json");
            var store = new SettingsStore(path);
            var settings = store.Load();
            Assert(settings.Sources.Count == 0, "should fall back to defaults");
            Assert(Directory.GetFiles(directory, "settings.json.corrupt-*").Length == 1, "corrupt file should be quarantined");
            return Task.CompletedTask;
        }
        finally
        {
            DeleteTree(directory);
        }
    }

    private static Task ActivityStoreTrims()
    {
        var directory = NewTempDirectory("activity");
        try
        {
            var store = new ActivityStore(Path.Combine(directory, "activity.json"));
            for (var i = 0; i < 65; i++)
            {
                store.Add(new SyncRecord
                {
                    StartedAt = DateTimeOffset.Now,
                    FinishedAt = DateTimeOffset.Now,
                    DeviceLabel = "USB",
                    DriveLetter = "E:",
                    DestinationRoot = @"E:\",
                    FilesCopied = i,
                    Success = true
                });
            }

            var records = store.Load();
            Assert(records.Count == 60, $"expected 60 records, got {records.Count}");
            Assert(records[0].FilesCopied == 64, "newest record should be first");
            return Task.CompletedTask;
        }
        finally
        {
            DeleteTree(directory);
        }
    }

    private static async Task UsbRoundTrip(string driveRoot)
    {
        var destinationRoot = Path.GetFullPath(driveRoot);
        var drive = new DriveInfo(Path.GetPathRoot(destinationRoot)!);
        Assert(drive.DriveType is DriveType.Removable or DriveType.Fixed, "target drive is not writable storage");
        Assert(drive.IsReady, "target drive is not ready");

        var temp = NewTempDirectory("usb-source");
        var marker = "SyncStick_Test_" + Guid.NewGuid().ToString("N")[..8];
        var createdDestination = Path.Combine(destinationRoot, marker);
        try
        {
            var source = Path.Combine(temp, "Project");
            WriteFile(Path.Combine(source, "hello.txt"), "hello from SyncStick");
            WriteFile(Path.Combine(source, "nested", "data.bin"), "0123456789");
            var engine = new SyncEngine();
            var settings = new SyncSettings { SmartOverwrite = true };
            var plan = engine.BuildPlan(
                new[] { FolderSource(source, marker) },
                destinationRoot,
                settings);
            var result = await engine.ExecuteAsync(plan, settings, trigger: "USB integration test");
            Assert(result.Success, "USB sync failed: " + string.Join(" | ", result.Errors));
            AssertFileEquals(Path.Combine(createdDestination, "hello.txt"), "hello from SyncStick");
            AssertFileEquals(Path.Combine(createdDestination, "nested", "data.bin"), "0123456789");

            var second = engine.BuildPlan(new[] { FolderSource(source, marker) }, destinationRoot, settings);
            Assert(second.FilesToCopy == 0, "second pass should find no changes on the real volume");
            Console.WriteLine($"        wrote and verified 2 files under {createdDestination}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(createdDestination))
                {
                    Directory.Delete(createdDestination, recursive: true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"        WARNING: could not remove test folder {createdDestination}: {ex.Message}");
            }

            DeleteTree(temp);
        }
    }
}

internal sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public InlineProgress(Action<T> handler)
    {
        _handler = handler;
    }

    public void Report(T value) => _handler(value);
}
