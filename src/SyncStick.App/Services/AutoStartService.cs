using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SyncStick.App.Localization;

namespace SyncStick.App.Services;

/// <summary>
/// Registers SyncStick to start with Windows.
///
/// The current user's Run key is the preferred mechanism. Some managed
/// machines, security tools and restricted process contexts deny that key,
/// so we fall back to a shortcut in the current user's Startup folder.
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SyncStick";
    private const string ShortcutFileName = "SyncStick.lnk";

    public static bool IsEnabled()
    {
        if (IsRegistryEnabled())
        {
            return true;
        }

        return IsStartupShortcutValid();
    }

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            Disable();
            return;
        }

        var executable = GetExecutablePath();

        // Preferred: HKCU\...\Run.
        var registryError = TryWriteRegistry(executable);
        if (registryError is null)
        {
            TryDeleteStartupShortcut();
            return;
        }

        // Fallback: %APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup.
        var shortcutError = TryWriteStartupShortcut(executable);
        if (shortcutError is null)
        {
            return;
        }

        throw new InvalidOperationException(
            "系统拒绝了开机启动项的写入。当前程序可能运行在受限权限或沙箱中，也可能是安全软件或系统策略拦截。",
            new AggregateException(registryError, shortcutError));
    }

    private static bool IsRegistryEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsStartupShortcutValid()
    {
        try
        {
            var shortcutPath = GetStartupShortcutPath();
            if (shortcutPath is null || !File.Exists(shortcutPath))
            {
                return false;
            }

            var target = TryReadShortcutTarget(shortcutPath);
            if (string.IsNullOrWhiteSpace(target))
            {
                // The shortcut exists; if we cannot inspect it, do not flip
                // the switch back off for the user.
                return true;
            }

            var currentExecutable = TryGetExecutablePath();
            if (currentExecutable is null)
            {
                return true;
            }

            return PathsEqual(target, currentExecutable);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Disable()
    {
        var errors = new List<Exception>();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }

        try
        {
            var shortcutPath = GetStartupShortcutPath();
            if (shortcutPath is not null && File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }

        if (IsEnabled())
        {
            throw new InvalidOperationException(
                "无法关闭开机启动项，可能被系统策略或安全软件拦截。",
                errors.Count > 0 ? new AggregateException(errors) : null);
        }
    }

    private static Exception? TryWriteRegistry(string executable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("无法打开 Windows 启动项注册表。");
            key.SetValue(ValueName, $"\"{executable}\" --minimized", RegistryValueKind.String);

            if (!IsRegistryEnabled())
            {
                throw new InvalidOperationException("启动项写入后无法读回。");
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static Exception? TryWriteStartupShortcut(string executable)
    {
        try
        {
            var shortcutPath = GetStartupShortcutPath()
                ?? throw new InvalidOperationException("无法获取 Windows 启动文件夹。");
            var directory = Path.GetDirectoryName(shortcutPath)
                ?? throw new InvalidOperationException("启动文件夹路径无效。");
            Directory.CreateDirectory(directory);

            object? shell = null;
            object? shortcut = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell")
                    ?? throw new InvalidOperationException("系统没有可用的 Windows Script Host。");
                shell = Activator.CreateInstance(shellType)
                    ?? throw new InvalidOperationException("无法创建 Windows Script Host 对象。");
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    binder: null,
                    target: shell,
                    args: new object[] { shortcutPath });

                if (shortcut is null)
                {
                    throw new InvalidOperationException("无法创建启动快捷方式。");
                }

                var shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { executable });
                shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { "--minimized" });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(executable) ?? string.Empty });
                shortcutType.InvokeMember(
                "Description",
                BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { Loc.IsEnglish ? "SyncStick auto-sync at sign-in" : "SyncStick 开机自动同步" });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { executable + ",0" });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                ReleaseComObject(shortcut);
                ReleaseComObject(shell);
            }

            if (!File.Exists(shortcutPath))
            {
                throw new InvalidOperationException("启动快捷方式没有生成。");
            }

            var savedTarget = TryReadShortcutTarget(shortcutPath);
            if (string.IsNullOrWhiteSpace(savedTarget) || !PathsEqual(savedTarget, executable))
            {
                throw new InvalidOperationException("启动快捷方式写入后无法验证目标路径。");
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static void TryDeleteStartupShortcut()
    {
        try
        {
            var shortcutPath = GetStartupShortcutPath();
            if (shortcutPath is not null && File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }
        catch (Exception)
        {
            // A stale fallback shortcut is harmless; single-instance handling
            // keeps a second launch from opening a second window.
        }
    }

    private static string? TryGetExecutablePath()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)
                || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return executable;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string GetExecutablePath() => TryGetExecutablePath()
        ?? throw new InvalidOperationException("请先将 SyncStick 发布为独立的 SyncStick.exe，再开启开机启动。");

    private static string? TryReadShortcutTarget(string shortcutPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return null;
            }

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: new object[] { shortcutPath });
            if (shortcut is null)
            {
                return null;
            }

            return shortcut.GetType().InvokeMember(
                "TargetPath",
                BindingFlags.GetProperty,
                binder: null,
                target: shortcut,
                args: null) as string;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static string? GetStartupShortcutPath()
    {
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (string.IsNullOrWhiteSpace(startupFolder))
        {
            return null;
        }

        return Path.Combine(startupFolder, ShortcutFileName);
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(value);
        }
        catch (Exception)
        {
            // The COM object may already have been released.
        }
    }
}
