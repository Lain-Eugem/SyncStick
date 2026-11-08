using System.Text;

namespace SyncStick.Core;

/// <summary>
/// Path helpers that keep every write inside the selected device root.
/// </summary>
public static class PathGuard
{
    private static readonly char[] InvalidRelativeChars = { '<', '>', ':', '"', '|', '?', '*' };

    /// <summary>
    /// Cleans a user-entered destination subfolder. Empty is allowed; rooted
    /// paths, parent traversal and invalid filename characters are rejected.
    /// </summary>
    public static string NormalizeSubfolder(string? subfolder)
    {
        if (string.IsNullOrWhiteSpace(subfolder))
        {
            return string.Empty;
        }

        var text = subfolder.Trim().Replace('/', '\\');
        if (text.Length >= 2 && text[1] == ':')
        {
            throw new FormatException("目标子目录必须是相对路径，不能包含盘符。");
        }

        if (text.StartsWith('\\'))
        {
            throw new FormatException("目标子目录必须是相对路径，不能以反斜杠开头。");
        }

        var parts = text.Split('\\');
        var clean = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var segment = part.Trim();
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                throw new FormatException("目标子目录不能包含“..”。");
            }

            if (segment.IndexOfAny(InvalidRelativeChars) >= 0 || segment.Any(char.IsControl))
            {
                throw new FormatException($"目标子目录包含非法字符：{segment}");
            }

            clean.Add(segment);
        }

        return string.Join('\\', clean);
    }

    /// <summary>
    /// Combines a relative path with a trusted root and verifies the result
    /// stays underneath that root.
    /// </summary>
    public static string CombineUnderRoot(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullRoot = Path.GetFullPath(root);
        var normalizedRelative = relativePath.Replace('/', '\\').TrimStart('\\');

        if (normalizedRelative.Split('\\').Any(part => part == ".."))
        {
            throw new FormatException("目标路径不能包含“..”。");
        }

        var combined = Path.GetFullPath(Path.Combine(fullRoot, normalizedRelative));
        if (!IsWithin(combined, fullRoot))
        {
            throw new FormatException("目标路径超出了设备根目录。");
        }

        return combined;
    }

    /// <summary>True when path is root itself or a descendant of root.</summary>
    public static bool IsWithin(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path).TrimEnd('\\');
        var fullRoot = Path.GetFullPath(root).TrimEnd('\\');

        if (string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fullPath.StartsWith(fullRoot + '\\', StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when either path is inside the other.</summary>
    public static bool Overlaps(string left, string right) =>
        IsWithin(left, right) || IsWithin(right, left);

    public static string MakeSafeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "未命名设备";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(char.IsControl(ch) ? ' ' : ch);
        }

        return builder.ToString();
    }
}
