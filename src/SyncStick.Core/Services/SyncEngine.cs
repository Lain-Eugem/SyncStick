using System.Buffers;
using System.Diagnostics;
using SyncStick.Core.Models;

namespace SyncStick.Core.Services;

/// <summary>
/// Scans sources into a read-only plan, then writes that plan to the target
/// using temporary files and atomic renames. No target file is modified while
/// the plan is being built.
/// </summary>
public sealed class SyncEngine
{
    private const int CopyBufferSize = 1024 * 1024;
    private const long FreeSpaceHeadroom = 8L * 1024 * 1024;
    private const int MaxWarnings = 30;
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(3);

    public SyncPlan BuildPlan(
        IEnumerable<SourceItem> sources,
        string destinationRoot,
        SyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);

        var root = Path.GetFullPath(destinationRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"找不到目标设备：{root}");
        }

        var plan = new SyncPlan(root);
        var warningKeys = new HashSet<string>(StringComparer.Ordinal);
        var plannedDestinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceErrors = new List<string>();

        void SourceError(string message)
        {
            sourceErrors.Add(message);
        }

        void Warn(string message)
        {
            if (plan.Warnings.Count < MaxWarnings && warningKeys.Add(message))
            {
                plan.Warnings.Add(message);
            }
        }

        foreach (var source in sources.Where(item => item.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(source.Path))
            {
                Warn($"“{source.Name}”没有有效的路径，已跳过。");
                SourceError($"“{source.Name}”没有有效路径。");
                continue;
            }

            string sourceFullPath;
            try
            {
                sourceFullPath = Path.GetFullPath(source.Path.Trim());
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Warn($"路径格式无效，已跳过：{source.Path}");
                SourceError($"“{source.Name}”的路径格式无效。");
                continue;
            }

            var isFolder = Directory.Exists(sourceFullPath);
            var isFile = File.Exists(sourceFullPath);
            if (!isFolder && !isFile)
            {
                Warn($"来源不存在，已跳过：{sourceFullPath}");
                SourceError($"来源不存在：{sourceFullPath}");
                continue;
            }

            if (PathGuard.IsWithin(sourceFullPath, root))
            {
                Warn($"来源位于目标设备内部，已跳过：{sourceFullPath}");
                SourceError($"来源位于目标设备内部：{sourceFullPath}");
                continue;
            }

            if (isFolder && PathGuard.IsWithin(root, sourceFullPath))
            {
                Warn($"目标设备位于来源文件夹内部，已跳过：{sourceFullPath}");
                SourceError($"目标设备位于来源文件夹内部：{sourceFullPath}");
                continue;
            }

            string subfolder;
            try
            {
                subfolder = PathGuard.NormalizeSubfolder(source.DestinationSubfolder);
            }
            catch (FormatException ex)
            {
                Warn($"“{source.Name}”的目标子目录无效：{ex.Message}");
                SourceError($"“{source.Name}”的目标子目录无效：{ex.Message}");
                continue;
            }

            if (isFile)
            {
                AddFile(
                    plan,
                    new FileInfo(sourceFullPath),
                    Path.Combine(subfolder, Path.GetFileName(sourceFullPath)),
                    settings,
                    plannedDestinations,
                    Warn,
                    sourceError: SourceError,
                    mirrorOwner: null);
                continue;
            }

            string mirrorRelativeRoot;
            if (!string.IsNullOrEmpty(subfolder))
            {
                mirrorRelativeRoot = subfolder;
            }
            else
            {
                var folderName = Path.GetFileName(sourceFullPath.TrimEnd('\\', '/'));
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    Warn($"“{sourceFullPath}”是磁盘根目录，请为它指定一个目标子目录。");
                    SourceError($"“{sourceFullPath}”是磁盘根目录，需要指定目标子目录。");
                    continue;
                }

                mirrorRelativeRoot = folderName;
            }

            string destinationFolder;
            try
            {
                destinationFolder = PathGuard.CombineUnderRoot(root, mirrorRelativeRoot);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                Warn($"“{source.Name}”的目标位置无效：{ex.Message}");
                SourceError($"“{source.Name}”的目标位置无效：{ex.Message}");
                continue;
            }

            var scope = new MirrorScope
            {
                SourceRoot = sourceFullPath,
                DestinationRoot = destinationFolder,
                SourceName = source.Name
            };

            EnumerateFolder(plan, scope, settings, cancellationToken, plannedDestinations, Warn, SourceError, mirrorRelativeRoot);

            if (settings.MirrorDelete)
            {
                plan.MirrorScopes.Add(scope);
            }
        }

        ValidateMirrors(plan, Warn);
        EnsureEnoughFreeSpace(plan, root, Warn);

        if (sourceErrors.Count > 0)
        {
            plan.SourceErrors.AddRange(sourceErrors);
            var first = sourceErrors[0];
            var extra = sourceErrors.Count > 1 ? $"（另有 {sourceErrors.Count - 1} 个同步项不可用）" : string.Empty;
            throw new SyncStickException($"同步未开始：{first}{extra}");
        }

        return plan;
    }

    public async Task<SyncResult> ExecuteAsync(
        SyncPlan plan,
        SyncSettings settings,
        IProgress<SyncProgress>? progress = null,
        string trigger = "手动同步",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);

        if (!Directory.Exists(plan.DestinationRoot))
        {
            throw new DeviceUnavailableException("目标设备已经断开。");
        }

        var startedAt = DateTimeOffset.Now;
        var result = new SyncResult
        {
            DestinationRoot = plan.DestinationRoot,
            StartedAt = startedAt,
            FinishedAt = startedAt,
            Trigger = trigger,
            FilesSkipped = plan.FilesUnchanged
        };
        result.Warnings.AddRange(plan.Warnings);

        var copyFiles = plan.Files.Where(file => file.NeedsCopy).ToList();
        var totalBytes = plan.BytesToCopy;
        var filesDone = 0;
        var bytesDone = 0L;
        var reportClock = Stopwatch.StartNew();
        var lastFile = string.Empty;

        void Report(SyncStage stage, string? currentFile = null, bool force = false)
        {
            if (!force && reportClock.ElapsedMilliseconds < 55)
            {
                return;
            }

            reportClock.Restart();
            progress?.Report(new SyncProgress(
                stage,
                currentFile ?? lastFile,
                filesDone,
                plan.FilesToCopy,
                bytesDone,
                totalBytes));
        }

        try
        {
            Report(SyncStage.Copying, null, force: true);

            foreach (var file in copyFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lastFile = Path.GetRelativePath(plan.DestinationRoot, file.DestinationPath);
                Report(SyncStage.Copying, lastFile, force: true);
                var fileBytes = 0L;

                try
                {
                    await CopyOneAsync(
                        file,
                        settings,
                        chunk =>
                        {
                            fileBytes += chunk;
                            bytesDone += chunk;
                            Report(SyncStage.Copying, lastFile);
                        },
                        cancellationToken).ConfigureAwait(false);

                    result.FilesCopied++;
                    result.BytesCopied += fileBytes;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    if (!Directory.Exists(plan.DestinationRoot))
                    {
                        throw new DeviceUnavailableException("同步过程中 U 盘被拔出，已停止写入。", ex);
                    }

                    result.Errors.Add($"{lastFile}：{FriendlyError(ex)}");
                }

                filesDone++;
                Report(SyncStage.Copying, lastFile, force: true);
            }

            if (settings.MirrorDelete && result.Errors.Count == 0)
            {
                Report(SyncStage.Cleaning, "正在清理目标中多余的文件…", force: true);
                CleanMirrors(plan, result, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!Directory.Exists(plan.DestinationRoot))
            {
                throw new DeviceUnavailableException("同步过程中 U 盘被拔出，已停止写入。", ex);
            }

            result.Errors.Add(FriendlyError(ex));
        }

        result.FinishedAt = DateTimeOffset.Now;
        result.Success = !result.Cancelled && result.Errors.Count == 0;

        progress?.Report(new SyncProgress(
            SyncStage.Completed,
            null,
            filesDone,
            plan.FilesToCopy,
            bytesDone,
            totalBytes));

        return result;
    }

    private static void EnumerateFolder(
        SyncPlan plan,
        MirrorScope scope,
        SyncSettings settings,
        CancellationToken cancellationToken,
        Dictionary<string, string> plannedDestinations,
        Action<string> warn,
        Action<string> sourceError,
        string mirrorRelativeRoot)
    {
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(scope.SourceRoot));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = stack.Pop();

            try
            {
                foreach (var file in directory.EnumerateFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            continue;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    if (settings.IgnoreSystemFiles && IsSystemJunk(file.Name))
                    {
                        continue;
                    }

                    var relative = Path.GetRelativePath(scope.SourceRoot, file.FullName);
                    var destinationRelative = Path.Combine(mirrorRelativeRoot, relative);
                    AddFile(plan, file, destinationRelative, settings, plannedDestinations, warn, sourceError, scope);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                scope.HasEnumerationErrors = true;
                var message = $"无法完整读取文件夹“{scope.SourceName}”：{FriendlyError(ex)}";
                warn(message);
                sourceError(message);
            }

            try
            {
                foreach (var child in directory.EnumerateDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    stack.Push(child);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                scope.HasEnumerationErrors = true;
                var message = $"无法完整读取文件夹“{scope.SourceName}”：{FriendlyError(ex)}";
                warn(message);
                sourceError(message);
            }
        }
    }

    private static void AddFile(
        SyncPlan plan,
        FileInfo source,
        string destinationRelative,
        SyncSettings settings,
        Dictionary<string, string> plannedDestinations,
        Action<string> warn,
        Action<string>? sourceError,
        MirrorScope? mirrorOwner)
    {
        string destination;
        try
        {
            destination = PathGuard.CombineUnderRoot(plan.DestinationRoot, destinationRelative);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            var message = $"跳过文件“{source.FullName}”：{ex.Message}";
            warn(message);
            sourceError?.Invoke(message);
            if (mirrorOwner is not null)
            {
                mirrorOwner.HasEnumerationErrors = true;
            }

            return;
        }

        if (plannedDestinations.TryGetValue(destination, out var firstOwner))
        {
            warn($"两个来源会写入同一个文件“{Path.GetRelativePath(plan.DestinationRoot, destination)}”（{firstOwner}、{source.FullName}），后一个将覆盖前一个。");
        }
        else
        {
            plannedDestinations[destination] = source.FullName;
        }

        var needsCopy = true;
        if (settings.SmartOverwrite)
        {
            try
            {
                var destinationInfo = new FileInfo(destination);
                if (destinationInfo.Exists)
                {
                    var sameSize = destinationInfo.Length == source.Length;
                    var sameAge = Math.Abs((destinationInfo.LastWriteTimeUtc - source.LastWriteTimeUtc).TotalSeconds) <= TimestampTolerance.TotalSeconds;
                    needsCopy = !(sameSize && sameAge);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                needsCopy = true;
                warn($"无法读取目标文件信息，将重新写入：{Path.GetRelativePath(plan.DestinationRoot, destination)}");
            }
        }

        var relativeDisplay = Path.GetRelativePath(plan.DestinationRoot, destination);
        plan.Files.Add(new PlannedFile
        {
            SourcePath = source.FullName,
            DestinationPath = destination,
            RelativePath = relativeDisplay,
            Length = source.Length,
            LastWriteTimeUtc = source.LastWriteTimeUtc,
            NeedsCopy = needsCopy,
            MirrorOwner = mirrorOwner
        });

        mirrorOwner?.ExpectedFiles.Add(destination);
    }

    private static void ValidateMirrors(SyncPlan plan, Action<string> warn)
    {
        if (plan.MirrorScopes.Count == 0)
        {
            return;
        }

        foreach (var scope in plan.MirrorScopes)
        {
            if (scope.HasEnumerationErrors)
            {
                scope.IsSafe = false;
                warn($"“{scope.SourceName}”未能完整读取，本次不会删除 U 盘上的旧文件。");
                continue;
            }

            foreach (var other in plan.MirrorScopes)
            {
                if (ReferenceEquals(scope, other))
                {
                    continue;
                }

                if (PathGuard.Overlaps(scope.DestinationRoot, other.DestinationRoot))
                {
                    scope.IsSafe = false;
                    other.IsSafe = false;
                    warn($"“{scope.SourceName}”与“{other.SourceName}”的目标目录重叠，为避免误删，已关闭它们的镜像清理。");
                    break;
                }
            }

            foreach (var file in plan.Files)
            {
                if (ReferenceEquals(file.MirrorOwner, scope))
                {
                    continue;
                }

                if (PathGuard.IsWithin(file.DestinationPath, scope.DestinationRoot))
                {
                    scope.IsSafe = false;
                    warn($"“{scope.SourceName}”的目标目录中还有来自其他来源的文件，已关闭它的镜像清理。");
                    break;
                }
            }
        }
    }

    private static void EnsureEnoughFreeSpace(SyncPlan plan, string destinationRoot, Action<string> warn)
    {
        if (plan.BytesToCopy <= 0)
        {
            return;
        }

        try
        {
            var rootPath = Path.GetPathRoot(destinationRoot);
            if (string.IsNullOrEmpty(rootPath))
            {
                return;
            }

            var drive = new DriveInfo(rootPath);
            if (!drive.IsReady)
            {
                throw new IOException("设备尚未就绪。");
            }

            var available = drive.AvailableFreeSpace;
            if (plan.BytesToCopy + FreeSpaceHeadroom > available)
            {
                throw new InsufficientSpaceException(plan.BytesToCopy + FreeSpaceHeadroom, available);
            }
        }
        catch (SyncStickException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            warn($"无法检查 U 盘剩余空间：{FriendlyError(ex)}");
        }
    }

    private static async Task CopyOneAsync(
        PlannedFile file,
        SyncSettings settings,
        Action<int> onBytes,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetDirectoryName(file.DestinationPath)
            ?? throw new IOException("无法解析目标路径。");
        Directory.CreateDirectory(destinationDirectory);

        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".syncstick-{Guid.NewGuid():N}.tmp");

        try
        {
            var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
            try
            {
                await using var source = new FileStream(
                    file.SourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    CopyBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var destination = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    CopyBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, CopyBufferSize), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    onBytes(read);
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (settings.CopyTimestamps)
            {
                try
                {
                    File.SetLastWriteTimeUtc(temporaryPath, file.LastWriteTimeUtc);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Timestamps are a convenience; never fail a sync for them.
                }
            }

            if (File.Exists(file.DestinationPath))
            {
                try
                {
                    var attributes = File.GetAttributes(file.DestinationPath);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(file.DestinationPath, attributes & ~FileAttributes.ReadOnly);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Let the move below produce the meaningful error.
                }
            }

            File.Move(temporaryPath, file.DestinationPath, overwrite: true);
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
                    // Orphaned temp files are cleaned up on the next successful run.
                }
            }
        }
    }

    private static void CleanMirrors(SyncPlan plan, SyncResult result, CancellationToken cancellationToken)
    {
        foreach (var scope in plan.MirrorScopes.Where(item => item.IsSafe))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(scope.DestinationRoot))
            {
                continue;
            }

            var deletedDirectories = new List<string>();
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(
                             scope.DestinationRoot,
                             "*",
                             SearchOption.AllDirectories))
                {
                    deletedDirectories.Add(directory);
                }

                foreach (var file in Directory.EnumerateFiles(
                             scope.DestinationRoot,
                             "*",
                             SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!PathGuard.IsWithin(file, scope.DestinationRoot))
                    {
                        continue;
                    }

                    if (scope.ExpectedFiles.Contains(Path.GetFullPath(file)))
                    {
                        continue;
                    }

                    try
                    {
                        var attributes = File.GetAttributes(file);
                        if ((attributes & FileAttributes.ReadOnly) != 0)
                        {
                            File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                        }

                        File.Delete(file);
                        result.FilesDeleted++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        result.Errors.Add($"清理 {Path.GetRelativePath(plan.DestinationRoot, file)}：{FriendlyError(ex)}");
                    }
                }

                foreach (var directory in deletedDirectories.OrderByDescending(path => path.Length))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (PathGuard.IsWithin(directory, scope.DestinationRoot) &&
                        !string.Equals(directory, scope.DestinationRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                            {
                                Directory.Delete(directory);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            result.Errors.Add($"清理空目录 {Path.GetFileName(directory)}：{FriendlyError(ex)}");
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Errors.Add($"清理“{scope.SourceName}”：{FriendlyError(ex)}");
            }
        }
    }

    private static bool IsSystemJunk(string fileName)
    {
        return fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase)
               || fileName.StartsWith("~$", StringComparison.Ordinal)
               || fileName.StartsWith(".syncstick-", StringComparison.OrdinalIgnoreCase)
               || fileName.EndsWith(".syncstick.tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static string FriendlyError(Exception exception)
    {
        var message = exception.Message.Trim();
        if (message.Length > 220)
        {
            message = message[..217] + "…";
        }

        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }
}
