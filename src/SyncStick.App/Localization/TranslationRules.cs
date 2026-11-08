using System.Globalization;
using System.Text.RegularExpressions;

namespace SyncStick.App.Localization;

/// <summary>
/// Pattern based translations for copy that is composed at runtime (counts, file
/// names, device letters, coordinator and engine messages).  Captured groups are
/// translated recursively, so messages that embed another Chinese message come out
/// fully localized.  Rules are matched in order; put specific rules first.
/// </summary>
internal static class TranslationRules
{
    private static readonly (Regex Pattern, string Template)[] Rules = BuildRules();

    internal static string? Apply(string text, int depth)
    {
        foreach (var (pattern, template) in Rules)
        {
            var match = pattern.Match(text);
            if (!match.Success)
            {
                continue;
            }

            return Format(template, match, depth);
        }

        return null;
    }

    private static string Format(string template, Match match, int depth)
    {
        var args = new object[match.Groups.Count - 1];
        for (var index = 1; index < match.Groups.Count; index++)
        {
            args[index - 1] = Loc.TranslateCapture(match.Groups[index].Value, depth);
        }

        return string.Format(CultureInfo.InvariantCulture, template, args);
    }

    private static (Regex, string)[] BuildRules()
    {
        static (Regex, string) Rule(string pattern, string template, RegexOptions options = RegexOptions.None) =>
            (new Regex(pattern, RegexOptions.Compiled | options), template);

        return new[]
        {
            // ---------------------------------------------------- multi-line dialogs
            Rule(
                "^(.+) 被 Windows 识别为固定磁盘，它可能是内置硬盘，也可能是 USB 硬盘盒。\n\n选择后，SyncStick 会在它接入时写入文件。请确认它不是系统盘或重要工作盘。$",
                "{0} is reported by Windows as a fixed disk. It may be an internal drive or a USB enclosure.\n\nSyncStick will write to it whenever it is connected. Make sure this is not your system drive or an important work disk.",
                RegexOptions.Singleline),

            // -------------------------------------------------------- dashboard copy
            Rule("^已记住 (.+)。插入后会先比较文件，再覆盖有变化的内容。$",
                "{0} is remembered. When it is plugged in, SyncStick compares files and overwrites what changed."),
            Rule("^自动同步已就绪。也可以现在手动写入 (.+)。$",
                "Auto-sync is ready. You can also sync {0} manually now."),
            Rule("^写入 (\\d+) 个文件，跳过 (\\d+) 个未变化文件。$",
                "Wrote {0} files, skipped {1} unchanged files."),
            Rule("^(\\d+) 个文件 · (.+)。$", "{0} files · {1}."),
            Rule("^(.+)可以安全拔出 (.+)。$", "{0} You can safely remove {1}."),
            Rule("^(.+) 等 (\\d+) 项$", "{0} and {1} more"),
            Rule("^([^、]+)、([^、]+)$", "{0}, {1}"),
            Rule("^正在写入 (.+)$", "Writing to {0}"),
            Rule("^(.+?) 已就绪$", "{0} is ready"),
            Rule("^剩余 (.+)$", "{0} free"),
            Rule("^默认：(.+)$", "Default: {0}"),
            Rule("^(.+) · 剩余 (.+)$", "{0} · {1} free"),
            Rule("^(.+) · 单文件$", "{0} · single file"),
            Rule("^已启用 (\\d+) 个同步项，共 (\\d+) 个。$", "{0} of {1} sync items enabled."),
            Rule("^最近 (\\d+) 次同步$", "Last {0} runs"),
            Rule("^已添加 (\\d+) 个同步项。$", "Added {0} sync items."),
            Rule("^(\\d+) 个同步项$", "{0} sync items"),
            Rule("^(\\d+) / (\\d+) 个文件$", "{0} / {1} files"),
            Rule("^(\\d+) 个$", "{0} devices"),
            Rule("^已把 (.+) 设为同步目标。$", "Set {0} as the sync target."),
            Rule("^磁盘 (.+)$", "Disk {0}"),

            // ---------------------------------------------------------- activity copy
            Rule("^写入 (\\d+) 个文件$", "Wrote {0} files"),
            Rule("^跳过 (\\d+) 个未变化的文件$", "Skipped {0} unchanged files"),
            Rule("^清理 (\\d+) 项$", "{0} items removed"),
            Rule("^(.+?)；另有 (\\d+) 个错误$", "{0}; {1} more errors"),
            Rule("^；另有 (\\d+) 个错误$", "; {0} more errors"),
            Rule("^有文件未能写入 · (\\d+) 个成功$", "Some files could not be written · {0} succeeded"),
            Rule("^同步未完成 · (\\d+) 个错误$", "Sync incomplete · {0} errors"),
            Rule("^写入 (\\d+) 个文件 · (.+)$", "Wrote {0} files · {1}"),
            Rule("^(\\d+) 个文件 · (.+)$", "{0} files · {1}"),

            // ------------------------------------------------------ durations (Core)
            Rule("^<1 秒$", "<1 s"),
            Rule("^(\\d+) 秒$", "{0} s"),
            Rule("^(\\d+) 分 (\\d+) 秒$", "{0} min {1} s"),

            // ----------------------------------------------------- coordinator toasts
            Rule("^同步完成：(\\d+) 个文件 · (.+)（清理 (\\d+) 项）$",
                "Sync complete: {0} files · {1} ({2} items removed)"),
            Rule("^同步完成：(\\d+) 个文件 · (.+)$", "Sync complete: {0} files · {1}"),
            Rule("^同步未开始：(.+)$", "Sync did not start: {0}"),
            Rule("^同步未完成：(.+)$", "Sync did not complete: {0}"),
            Rule("^同步失败：(.+)$", "Sync failed: {0}"),
            Rule("^操作失败：(.+)$", "Operation failed: {0}"),
            Rule("^设置保存失败：(.+)$", "Failed to save settings: {0}"),
            Rule("^读取设备列表失败：(.+)$", "Failed to read the device list: {0}"),
            Rule("^自动同步未开始：(.+)$", "Auto-sync did not start: {0}"),
            Rule("^无法打开数据目录：(.+)$", "Could not open the data folder: {0}"),
            Rule("^路径无效：(.+)$", "Invalid path: {0}"),
            Rule("^读写失败：(.+)$", "Read/write failed: {0}"),
            Rule("^（另有 (\\d+) 个同步项不可用）$", " ({0} more sync items unavailable)"),

            // --------------------------------------------------------- engine errors
            Rule("^U 盘剩余空间不足：需要 (.+)，可用 (.+)。$",
                "Not enough space on the USB drive: {0} required, {1} available."),
            Rule("^“(.+?)”没有有效的路径，已跳过。$", "\"{0}\" has no valid path; skipped."),
            Rule("^“(.+?)”没有有效路径。$", "\"{0}\" has no valid path."),
            Rule("^路径格式无效，已跳过：(.+)$", "Invalid path format; skipped: {0}"),
            Rule("^“(.+?)”的路径格式无效。$", "\"{0}\" has an invalid path format."),
            Rule("^来源不存在，已跳过：(.+)$", "Source does not exist; skipped: {0}"),
            Rule("^来源不存在：(.+)$", "Source does not exist: {0}"),
            Rule("^来源位于目标设备内部，已跳过：(.+)$", "Source is inside the target device; skipped: {0}"),
            Rule("^来源位于目标设备内部：(.+)$", "Source is inside the target device: {0}"),
            Rule("^目标设备位于来源文件夹内部，已跳过：(.+)$",
                "Target device is inside the source folder; skipped: {0}"),
            Rule("^目标设备位于来源文件夹内部：(.+)$", "Target device is inside the source folder: {0}"),
            Rule("^“(.+?)”的目标子目录无效：(.+)$",
                "\"{0}\" has an invalid destination subfolder: {1}"),
            Rule("^“(.+?)”的目标位置无效：(.+)$", "\"{0}\" has an invalid destination: {1}"),
            Rule("^“(.+?)”是磁盘根目录，请为它指定一个目标子目录。$",
                "\"{0}\" is a drive root; specify a destination subfolder for it."),
            Rule("^“(.+?)”是磁盘根目录，需要指定目标子目录。$",
                "\"{0}\" is a drive root and needs a destination subfolder."),
            Rule("^无法完整读取文件夹“(.+?)”：(.*)$", "Could not fully read folder \"{0}\": {1}"),
            Rule("^跳过文件“(.+?)”：(.*)$", "Skipped file \"{0}\": {1}"),
            Rule("^两个来源会写入同一个文件“(.+?)”（(.+?)、(.+?)），后一个将覆盖前一个。$",
                "Two sources write the same file \"{0}\" ({1}, {2}); the latter overwrites the former."),
            Rule("^无法读取目标文件信息，将重新写入：(.+)$",
                "Could not read target file info; rewriting: {0}"),
            Rule("^“(.+?)”未能完整读取，本次不会删除 U 盘上的旧文件。$",
                "\"{0}\" could not be fully read; old files on the USB drive will not be deleted this time."),
            Rule("^“(.+?)”与“(.+?)”的目标目录重叠，为避免误删，已关闭它们的镜像清理。$",
                "\"{0}\" and \"{1}\" have overlapping destination folders; mirror cleanup is disabled for both to avoid mistaken deletions."),
            Rule("^“(.+?)”的目标目录中还有来自其他来源的文件，已关闭它的镜像清理。$",
                "\"{0}\" shares its destination folder with files from other sources; mirror cleanup is disabled for it."),
            Rule("^无法检查 U 盘剩余空间：(.+)$", "Could not check free space on the USB drive: {0}"),
            Rule("^清理空目录 (.+?)：(.*)$", "Clean up empty folder {0}: {1}"),
            Rule("^清理“(.+?)”：(.*)$", "Clean up \"{0}\": {1}"),
            Rule("^清理 (.+?)：(.*)$", "Clean up {0}: {1}"),
            Rule("^目标子目录包含非法字符：(.+)$",
                "The destination subfolder contains invalid characters: {0}"),

            // --------------------------------------------------------------- tray text
            Rule("^SyncStick · (.+) 已连接$", "SyncStick · {0} connected"),

            // ------------------------------------------------------------ rule names
            Rule("^规则 (\\d+)$", "Rule {0}"),

            // ------------------------------------------------------------ last resort
            Rule("^([^·]+?) · (.+)$", "{0} · {1}"),
        };
    }
}
