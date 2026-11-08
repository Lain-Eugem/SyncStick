using System.Globalization;
using System.Text.RegularExpressions;

namespace SyncStick.App.Localization;

public enum AppLanguage
{
    System,
    Chinese,
    English
}

/// <summary>
/// Runtime localization service for the desktop app.  The product ships with
/// Simplified Chinese as the default language; English copy is supplied by
/// <see cref="ZhEnDictionary"/> (exact strings) and <see cref="TranslationRules"/>
/// (strings composed at runtime).  The service is display-only: settings, engine
/// and file-system values keep their original Chinese text so stored data and the
/// behavior of the core stay untouched.
/// </summary>
public static class Loc
{
    private static readonly Regex CjkPattern = new("[\u4e00-\u9fff]", RegexOptions.Compiled);

    private static readonly (Regex Pattern, string Replacement)[] SingularForms =
    {
        (new Regex(@"\b1 sync items\b", RegexOptions.Compiled), "1 sync item"),
        (new Regex(@"\b1 more sync items\b", RegexOptions.Compiled), "1 more sync item"),
        (new Regex(@"\b1 unchanged files\b", RegexOptions.Compiled), "1 unchanged file"),
        (new Regex(@"\b1 files\b", RegexOptions.Compiled), "1 file"),
        (new Regex(@"\b1 items\b", RegexOptions.Compiled), "1 item"),
        (new Regex(@"\b1 runs\b", RegexOptions.Compiled), "1 run"),
        (new Regex(@"\b1 errors\b", RegexOptions.Compiled), "1 error"),
        (new Regex(@"\b1 devices\b", RegexOptions.Compiled), "1 device")
    };

    /// <summary>Language requested by the user (CLI or persisted setting).</summary>
    public static AppLanguage RequestedLanguage { get; private set; } = AppLanguage.System;

    /// <summary>Language actually rendered after resolving "system".</summary>
    public static AppLanguage EffectiveLanguage { get; private set; } = AppLanguage.Chinese;

    public static bool IsEnglish => EffectiveLanguage == AppLanguage.English;

    public static CultureInfo Culture => IsEnglish
        ? CultureInfo.GetCultureInfo("en-US")
        : CultureInfo.GetCultureInfo("zh-CN");

    /// <summary>
    /// Resolves the UI language.  Precedence: command line override, persisted
    /// setting, operating-system UI language (Chinese for zh-*, otherwise English).
    /// </summary>
    public static void Initialize(string? commandLineLanguage, string? settingsLanguage)
    {
        RequestedLanguage = ParseLanguage(commandLineLanguage)
                             ?? ParseLanguage(settingsLanguage)
                             ?? AppLanguage.System;

        EffectiveLanguage = RequestedLanguage switch
        {
            AppLanguage.Chinese => AppLanguage.Chinese,
            AppLanguage.English => AppLanguage.English,
            _ => IsChineseOperatingSystem() ? AppLanguage.Chinese : AppLanguage.English
        };
    }

    public static AppLanguage? ParseLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "system" or "auto" or "跟随系统" => AppLanguage.System,
            "zh" or "zh-cn" or "zh-hans" or "chinese" or "中文" => AppLanguage.Chinese,
            "en" or "en-us" or "en-gb" or "english" or "英文" => AppLanguage.English,
            _ => null
        };
    }

    public static string ToSettingValue(AppLanguage language) => language switch
    {
        AppLanguage.Chinese => "zh",
        AppLanguage.English => "en",
        _ => "system"
    };

    /// <summary>Translates a display string when the UI is English; otherwise returns it unchanged.</summary>
    public static string T(string? text) => Translate(text, 0);

    /// <summary>Display name for a rule set; the migrated default set has a Chinese storage name.</summary>
    public static string RuleName(string? name)
        => IsEnglish && name is "默认规则" or "新规则"
            ? (name == "默认规则" ? "Default rule" : "New rule")
            : name ?? string.Empty;

    /// <summary>Formats a Chinese copy template after translating it (used when a template needs arguments).</summary>
    public static string F(string template, params object?[] args)
        => string.Format(Culture, T(template), args);

    internal static string Translate(string? text, int depth)
    {
        if (string.IsNullOrEmpty(text) || !IsEnglish)
        {
            return text ?? string.Empty;
        }

        if (depth > 4 || !CjkPattern.IsMatch(text))
        {
            return text;
        }

        if (ZhEnDictionary.Map.TryGetValue(text, out var exact))
        {
            return Normalize(exact);
        }

        var patterned = TranslationRules.Apply(text, depth);
        return patterned is null ? text : Normalize(patterned);
    }

    internal static string TranslateCapture(string text, int depth) => Translate(text, depth + 1);

    private static string Normalize(string english)
    {
        foreach (var (pattern, replacement) in SingularForms)
        {
            english = pattern.Replace(english, replacement);
        }

        return english;
    }

    private static bool IsChineseOperatingSystem()
    {
        var culture = CultureInfo.CurrentUICulture;
        return culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
    }

    public static string TodayText()
        => DateTime.Now.ToString(IsEnglish ? "MMM d · dddd" : "M 月 d 日 · dddd", Culture);

    public static string FormatWhen(DateTime local)
    {
        var today = DateTime.Today;
        if (local.Date == today)
        {
            return IsEnglish
                ? "Today " + local.ToString("HH:mm", Culture)
                : "今天 " + local.ToString("HH:mm", Culture);
        }

        if (local.Date == today.AddDays(-1))
        {
            return IsEnglish
                ? "Yesterday " + local.ToString("HH:mm", Culture)
                : "昨天 " + local.ToString("HH:mm", Culture);
        }

        return local.ToString(IsEnglish ? "MMM d HH:mm" : "M 月 d 日 HH:mm", Culture);
    }

    public static string FormatFeedTime(DateTime local)
    {
        if (local.Date == DateTime.Today)
        {
            return local.ToString("HH:mm", Culture);
        }

        if (local.Date == DateTime.Today.AddDays(-1))
        {
            return IsEnglish ? "Yesterday" : "昨天";
        }

        return local.ToString("MM-dd", Culture);
    }

    public static string FormatActivityDate(DateTime local)
        => local.ToString(IsEnglish ? "MMM d" : "MM月dd日", Culture);
}
