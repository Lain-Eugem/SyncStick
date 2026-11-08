// SyncStick Portable - small UI localization helper.
//
// The dictionary is keyed by the Simplified-Chinese source string that also
// appears in the UI code, so a missing translation falls back to Chinese
// instead of showing an empty label. To add a string: write T(L"中文") (or
// TF(L"中文 %s", value)) in the UI code and add the English text below.
//
// This header is header-only on purpose: the portable build script
// (build.ps1) compiles only main.cpp and core.cpp, and localization must not
// require changing that script.
//
// TF()/Format() implement their own left-to-right %s substitution instead of
// using vswprintf: the MinGW MSVCRT runtime does not support passing wchar_t*
// through varargs reliably, and dynamic strings (paths, device names) may
// contain '%' characters. Values are inserted once and never re-scanned.

#pragma once

#include "core.h"

#include <cwctype>
#include <string>
#include <type_traits>
#include <unordered_map>
#include <utility>
#include <vector>

namespace ss {

enum class Language {
    Zh,
    En,
};

namespace i18n_detail {

inline Language& CurrentLanguage() {
    // Function-local static in an inline function: exactly one instance for
    // the whole program even with several translation units.
    static Language language = Language::Zh;
    return language;
}

inline std::wstring TrimLower(std::wstring text) {
    size_t begin = 0;
    while (begin < text.size() && std::iswspace(text[begin])) {
        ++begin;
    }
    size_t end = text.size();
    while (end > begin && std::iswspace(text[end - 1])) {
        --end;
    }
    text = text.substr(begin, end - begin);
    for (wchar_t& ch : text) {
        ch = static_cast<wchar_t>(std::towlower(ch));
    }
    return text;
}

inline const std::unordered_map<std::wstring, const wchar_t*>& Dictionary() {
    static const std::unordered_map<std::wstring, const wchar_t*> dictionary = {
        // Header and window title.
        {L"SyncStick Portable · U 盘双向同步", L"SyncStick Portable · USB two-way sync"},
        {L"U 盘随身同步", L"Portable USB sync"},
        {L"这台电脑 · %s", L"This PC · %s"},
        {L"规则保存在这支 U 盘里", L"Rules are stored on this USB drive"},
        {L"同步中", L"Syncing"},
        {L"已插入", L"Inserted"},
        {L"U 盘已就绪", L"USB drive ready"},
        {L"中文", L"中文"},

        // Toolbar and counts.
        {L"立即同步", L"Sync now"},
        {L"停止同步", L"Stop sync"},
        {L"添加同步项", L"Add sync item"},
        {L"打开日志", L"Open log"},
        {L"%s 个同步项", L"%s sync items"},
        {L"%s 个启用", L"%s enabled"},

        // Status card.
        {L"正在同步", L"Syncing"},
        {L"正在比较两边的文件", L"Comparing both sides"},
        {L"已停止", L"Stopped"},
        {L"同步遇到问题", L"Sync problem"},
        {L"同步完成", L"Sync complete"},
        {L"还没有同步项", L"No sync items yet"},
        {L"先添加这台电脑上需要随身带走的文件夹或文件", L"Add folders or files to carry from this PC"},
        {L"准备好了", L"Ready"},
        {L"启动时会自动比较一次两边的内容", L"Both sides are compared automatically at startup"},
        {L"上次同步 %s", L"Last sync %s"},
        {L"正在准备", L"Preparing"},
        {L"已按规则完成一次同步", L"One sync completed by the rules"},
        {L"同步没有完成", L"The sync did not complete"},

        // Rules section and cards.
        {L"同步项", L"Sync items"},
        {L"这台电脑的规则 · 双击一次启动", L"Rules for this PC · double-click once to start"},
        {L"提示：插上 U 盘后双击一次，程序会自动识别这台电脑并同步这里的规则。", L"Tip: plug in the USB drive and double-click once; the app recognizes this PC and syncs its rules"},
        {L"U 盘 · %s", L"USB · %s"},
        {L"改", L"Edit"},
        {L"删", L"Del"},
        {L"已暂停", L"Paused"},
        {L"选一个文件夹或文件，规则会按这台电脑记住", L"Pick a folder or file; rules are remembered per PC"},

        // Footer.
        {L"桌面版正在运行 · 便携版已暂停自动同步", L"Desktop app is running · portable auto sync paused"},
        {L"本机运行 · 规则保存在 U 盘 · 双击一次启动", L"Runs locally · rules stored on the USB drive · double-click once to start"},

        // Rule dialog.
        {L"编辑同步项", L"Edit sync item"},
        {L"名称", L"Name"},
        {L"电脑上的文件或文件夹", L"File or folder on this PC"},
        {L"浏览…", L"Browse…"},
        {L"类型", L"Type"},
        {L"文件夹", L"Folder"},
        {L"单个文件", L"Single file"},
        {L"同步方向", L"Sync direction"},
        {L"双向同步", L"Two-way sync"},
        {L"电脑 → U 盘", L"PC → USB"},
        {L"U 盘 → 电脑", L"USB → PC"},
        {L"U 盘内位置（可填子文件夹）", L"Location on the USB drive (subfolder allowed)"},
        {L"默认位置", L"Default"},
        {L"填相对路径会自动跟随 U 盘盘符，例如：我的项目\\资料。留空时放进 SyncStickData\\这台电脑\\规则名。", L"A relative path follows the USB drive letter, e.g. MyProject\\Data. Empty means SyncStickData\\ThisPC\\RuleName"},
        {L"启用这一项", L"Enable this item"},
        {L"取消", L"Cancel"},
        {L"保存", L"Save"},
        {L"新同步项", L"New sync item"},
        {L"选择电脑上的文件夹", L"Choose a folder on this PC"},
        {L"选择电脑上的文件", L"Choose a file on this PC"},
        {L"选择 U 盘上的文件夹", L"Choose a folder on the USB drive"},

        // Rule dialog messages.
        {L"请选择电脑上的文件或文件夹。", L"Choose a file or folder on this PC"},
        {L"还差一步", L"One more step"},
        {L"这个电脑路径不存在，请重新选择。", L"This PC path does not exist; choose it again"},
        {L"路径无效", L"Invalid path"},
        {L"这是文件夹，请把类型改成“文件夹”。", L"This is a folder; change Type to Folder"},
        {L"类型不匹配", L"Type mismatch"},
        {L"这是文件，请把类型改成“单个文件”。", L"This is a file; change Type to Single file"},
        {L"U 盘内位置不能为空。", L"The USB location cannot be empty"},
        {L"路径重复", L"Duplicate path"},
        {L"电脑路径和 U 盘路径不能相同。", L"The PC path and USB path must be different"},
        {L"路径重叠", L"Overlapping paths"},
        {L"电脑文件夹和 U 盘文件夹不能互相包含。", L"The PC folder and USB folder cannot contain each other"},
        {L"路径不匹配", L"Path mismatch"},
        {L"文件类型的 U 盘位置必须是一个文件路径，不是文件夹。", L"For a single-file rule the USB location must be a file path, not a folder"},

        // Main window messages.
        {L"配置文件读取失败", L"Could not read the config file"},
        {L"无法读取 U 盘上的规则文件：\n%s\n\n原文件不会被自动覆盖。", L"Could not read the rules file on the USB drive:\n%s\n\nThe original file will not be overwritten automatically"},
        {L"规则文件有问题", L"Rules file problem"},
        {L"正在同步，要停止并退出吗？", L"A sync is running. Stop it and exit?"},
        {L"规则没能写入 U 盘：\n%s", L"Could not write the rules to the USB drive:\n%s"},
        {L"保存失败", L"Save failed"},
        {L"这个电脑路径已经有一条相同的同步项了。", L"A sync item with this PC path already exists"},
        {L"重复的同步项", L"Duplicate sync item"},
        {L"删除“%s”这条规则吗？\nU 盘里的文件不会被删除。", L"Delete the rule “%s”?\nFiles on the USB drive will not be deleted"},
        {L"删除同步项", L"Delete sync item"},
        {L"这个电脑路径已经不存在了，可以编辑或删除这条规则。", L"This PC path no longer exists; edit or delete this rule"},
        {L"路径不存在", L"Path missing"},
        {L"还没有生成日志文件。先同步一次吧。", L"No log file yet. Run a sync first"},
        {L"暂无日志", L"No log yet"},
        {L"检测到桌面版 SyncStick 正在运行。\n\n两个程序同时写同一支 U 盘可能互相覆盖。便携版这次不会自动同步；请先退出桌面版，再回到这里点“立即同步”。", L"The desktop SyncStick is running.\n\nTwo programs writing the same USB drive can overwrite each other. The portable app will not auto sync this time; quit the desktop app first, then come back and click “Sync now”"},
        {L"检测到桌面版 SyncStick", L"Desktop SyncStick detected"},
        {L"还没有同步项，先添加一个文件夹或文件吧。", L"No sync items yet. Add a folder or file first"},
        {L"所有同步项都暂停了。先打开其中一项再同步。", L"All sync items are paused. Enable one before syncing"},
        {L"没有启用的同步项", L"No enabled sync items"},

        // Sync engine.
        {L"读取配置失败：%s", L"Could not read the config: %s"},
        {L"创建目录失败：%s", L"Could not create the folder: %s"},
        {L"写入配置失败：%s", L"Could not write the config: %s"},
        {L"替换配置失败：%s", L"Could not replace the config: %s"},
        {L"无法创建目录：%s", L"Could not create the folder: %s"},
        {L"错误 %s", L"Error %s"},
        {L"这台电脑", L"This PC"},
        {L"（根目录）", L"(root)"},
        {L"两边大小相同但修改时间完全相同，内容可能不同", L"Same size and identical modification time; contents may differ"},
        {L"电脑上的版本更新", L"The PC copy is newer"},
        {L"U 盘上的版本更新", L"The USB copy is newer"},
        {L"两边修改时间相差不到 3 秒，无法安全判断哪边更新", L"Modification times differ by less than 3 seconds; cannot tell which side is newer"},
        {L"冲突：%s — %s", L"Conflict: %s — %s"},
        {L"（电脑 %s，U 盘 %s）", L"(PC %s, USB %s)"},
        {L"两边的文件都不存在", L"The file does not exist on either side"},
        {L"电脑上的文件不存在", L"The file does not exist on this PC"},
        {L"U 盘上的文件不存在", L"The file does not exist on the USB drive"},
        {L"无法读取电脑文件夹：%s", L"Could not read the PC folder: %s"},
        {L"无法读取 U 盘文件夹：%s", L"Could not read the USB folder: %s"},
        {L"规则没有名称", L"The rule has no name"},
        {L"电脑路径或 U 盘路径为空", L"The PC path or USB path is empty"},
        {L"电脑路径和 U 盘路径相同", L"The PC path and USB path are identical"},
        {L"电脑路径不是文件", L"The PC path is not a file"},
        {L"电脑路径不是文件夹", L"The PC path is not a folder"},
        {L"U 盘路径不是文件", L"The USB path is not a file"},
        {L"U 盘路径不是文件夹", L"The USB path is not a folder"},
        {L"电脑路径和 U 盘路径互相包含，无法安全同步", L"The PC and USB paths contain each other; cannot sync safely"},
        {L"两边的路径都不存在", L"The path does not exist on either side"},
        {L"电脑上的源文件或文件夹不存在", L"The source file or folder does not exist on this PC"},
        {L"U 盘上的源文件或文件夹不存在", L"The source file or folder does not exist on the USB drive"},
        {L"同步方向无效", L"Invalid sync direction"},
        {L"源文件已经不存在：%s", L"The source file no longer exists: %s"},
        {L"源文件无法读取：%s", L"The source file cannot be read: %s"},
        {L"无法判断目标磁盘：%s", L"Could not determine the destination disk: %s"},
        {L"目标磁盘剩余空间不足（需要 %s，可用 %s）", L"Not enough free space on the destination disk (need %s, available %s)"},
        {L"复制失败：%s", L"Copy failed: %s"},
        {L"替换目标文件失败：%s", L"Could not replace the destination file: %s"},
        {L"开始同步设备：%s（%s）", L"Starting sync for device: %s (%s)"},
        {L"正在检查规则", L"Checking rules"},
        {L"规则「%s」：%s", L"Rule “%s”: %s"},
        {L"规则「%s」%s", L"Rule “%s”: %s"},
        {L"同步已取消", L"Sync cancelled"},
        {L"正在检查文件", L"Checking files"},
        {L"准备写入", L"Preparing to write"},
        {L"检查完成，没有需要同步的文件", L"Check complete; nothing to sync"},
        {L"正在写入 U 盘", L"Writing to the USB drive"},
        {L"正在写回电脑", L"Writing back to this PC"},
        {L"复制到 U 盘：%s", L"Copy to USB: %s"},
        {L"复制到电脑：%s", L"Copy to PC: %s"},
        {L"复制失败：%s — %s", L"Copy failed: %s — %s"},
        {L"同步已取消，已写入 %s 个文件", L"Sync cancelled; %s file(s) written"},
        {L"同步结束，写入 %s 个文件，%s 个错误", L"Sync finished; %s file(s) written, %s error(s)"},
        {L"同步完成，写入 %s 个文件", L"Sync complete; %s file(s) written"},
        {L"，跳过 %s 个未变化文件", L", skipped %s unchanged file(s)"},
        {L"，保留 %s 处冲突", L", kept %s conflict(s)"},
        {L"规则集", L"Rule sets"},
        {L"规则集 · %s", L"Rule set · %s"},
        {L"选择规则集", L"Choose a rule set"},
        {L"这台电脑启动时使用哪一套规则", L"Which rule set this PC uses on startup"},
        {L"使用", L"Use"},
        {L"新建", L"New"},
        {L"重命名", L"Rename"},
        {L"删除", L"Delete"},
        {L"新建规则集", L"New rule set"},
        {L"重命名规则集", L"Rename rule set"},
        {L"删除规则集「%s」吗？\n其中的规则也会一起删除；U 盘里的文件不会被删除。", L"Delete rule set “%s”?\nIts rules will be removed too; files on the USB drive are not deleted"},
        {L"删除规则集", L"Delete rule set"},
        {L"规则集名称不能为空", L"A rule set name is required"},
        {L"已经有同名的规则集了", L"A rule set with that name already exists"},
        {L"至少需要保留一个规则集", L"Keep at least one rule set"},
        {L"默认规则", L"Default rules"},
        {L"新建的规则集", L"New rule set"},
        {L"确定", L"OK"},
    };
    return dictionary;
}

inline std::wstring Text(const std::wstring& value) {
    return value;
}

inline std::wstring Text(const wchar_t* value) {
    return value != nullptr ? std::wstring(value) : std::wstring();
}

template <typename T, std::enable_if_t<std::is_arithmetic_v<T>, int> = 0>
inline std::wstring Text(T value) {
    return std::to_wstring(value);
}

} // namespace i18n_detail

inline Language GetLanguage() {
    return i18n_detail::CurrentLanguage();
}

inline void SetLanguage(Language language) {
    i18n_detail::CurrentLanguage() = language;
}

inline bool IsEnglish() {
    return GetLanguage() == Language::En;
}

inline const wchar_t* LanguageTag() {
    return IsEnglish() ? L"en" : L"zh";
}

// Accepts "en", "EN", "en-US", "zh", "zh-CN", "chinese", "cn", ... Returns
// the canonical tag ("en" or "zh") when the text is recognized.
inline bool ParseLanguageTag(const std::wstring& text, std::wstring& tag) {
    const std::wstring value = i18n_detail::TrimLower(text);
    if (value.empty()) {
        return false;
    }
    if (value.rfind(L"en", 0) == 0 || value == L"english") {
        tag = L"en";
        return true;
    }
    if (value.rfind(L"zh", 0) == 0 || value == L"chinese" || value == L"cn" ||
        value == L"chs") {
        tag = L"zh";
        return true;
    }
    return false;
}

// First run: Chinese Windows gets Chinese, everything else gets English.
inline Language DetectSystemLanguage() {
    const LANGID id = GetUserDefaultUILanguage();
    return PRIMARYLANGID(id) == LANG_CHINESE ? Language::Zh : Language::En;
}

// Translates a Chinese source string. Unknown keys fall back to the source.
inline std::wstring T(const wchar_t* chinese) {
    if (chinese == nullptr) {
        return std::wstring();
    }
    const auto& dictionary = i18n_detail::Dictionary();
    const auto it = dictionary.find(chinese);
    if (!IsEnglish() || it == dictionary.end()) {
        return std::wstring(chinese);
    }
    return std::wstring(it->second);
}

inline std::wstring T(const std::wstring& chinese) {
    return T(chinese.c_str());
}

inline std::wstring Format(const std::wstring& format) {
    return format;
}

template <typename... Args>
inline std::wstring Format(const std::wstring& format, Args&&... args) {
    std::vector<std::wstring> values;
    values.reserve(sizeof...(Args));
    (values.push_back(i18n_detail::Text(std::forward<Args>(args))), ...);
    std::wstring out;
    out.reserve(format.size() + 32);
    size_t next = 0;
    for (size_t i = 0; i < format.size(); ++i) {
        if (format[i] == L'%' && i + 1 < format.size() && format[i + 1] == L's' &&
            next < values.size()) {
            out += values[next++];
            ++i;
        } else {
            out += format[i];
        }
    }
    return out;
}

// Translate a Chinese format string, then substitute %s placeholders.
template <typename... Args>
inline std::wstring TF(const wchar_t* chineseFormat, Args&&... args) {
    return Format(T(chineseFormat), std::forward<Args>(args)...);
}

// Count with correct English plural ("1 sync item" / "2 sync items").
inline std::wstring CountText(int count, const wchar_t* chineseFormat,
                              const wchar_t* englishSingular, const wchar_t* englishPlural) {
    if (!IsEnglish()) {
        return TF(chineseFormat, count);
    }
    return std::to_wstring(count) + L" " + (count == 1 ? englishSingular : englishPlural);
}

// Name used when a rule has no name at all (never a translated heading).
inline std::wstring DefaultRuleNameText() {
    return IsEnglish() ? L"Sync item" : L"同步项";
}

// Name used when a rule set has no name at all (never a translated heading).
inline std::wstring DefaultRuleSetNameText() {
    return IsEnglish() ? L"Default rules" : L"默认规则";
}

namespace i18n_detail {

// MessageBox captions come from user32.dll in the OS UI language.  Rename the
// standard buttons while the box is being activated so they follow the app's
// language rather than the OS language.
inline LRESULT CALLBACK LocalizedMessageBoxHook(int code, WPARAM wparam, LPARAM lparam) {
    if (code == HCBT_ACTIVATE) {
        HWND window = reinterpret_cast<HWND>(wparam);
        struct ButtonLabel {
            int id;
            const wchar_t* chinese;
            const wchar_t* english;
        };
        static const ButtonLabel kLabels[] = {
            {IDOK, L"确定", L"OK"},
            {IDCANCEL, L"取消", L"Cancel"},
            {IDYES, L"是", L"Yes"},
            {IDNO, L"否", L"No"},
        };
        for (const ButtonLabel& label : kLabels) {
            HWND control = GetDlgItem(window, label.id);
            if (control != nullptr) {
                SetWindowTextW(control, IsEnglish() ? label.english : label.chinese);
            }
        }
    }
    return CallNextHookEx(nullptr, code, wparam, lparam);
}

} // namespace i18n_detail

// MessageBoxW with the standard buttons renamed to the active UI language.
inline int ShowMessageBox(HWND owner, const wchar_t* body, const wchar_t* title, UINT flags) {
    HHOOK hook = SetWindowsHookExW(WH_CBT, i18n_detail::LocalizedMessageBoxHook, nullptr,
                                   GetCurrentThreadId());
    int result = MessageBoxW(owner, body, title, flags);
    if (hook != nullptr) {
        UnhookWindowsHookEx(hook);
    }
    return result;
}

} // namespace ss
