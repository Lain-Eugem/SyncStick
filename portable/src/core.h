#pragma once

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <atomic>
#include <cstdint>
#include <functional>
#include <map>
#include <mutex>
#include <string>
#include <vector>

namespace ss {

struct Rule {
    std::wstring name;
    std::wstring pcPath;     // absolute path on this PC
    std::wstring usbPath;    // absolute resolved path on the current USB drive
    std::wstring kind;       // L"folder" or L"file"
    std::wstring mode;       // L"both", L"pc2usb", L"usb2pc"
    bool enabled = true;
};

// A named set of sync rules.  Rule sets live on the USB drive, not per PC, so
// the same rules can be reused on any computer.  A device remembers which set
// it used last in DeviceRules::lastRuleSetId.
struct RuleSet {
    std::wstring id;
    std::wstring name;
    std::vector<Rule> rules;
};

struct DeviceRules {
    std::wstring id;
    std::wstring name;
    std::wstring lastRuleSetId;  // rule set this PC used last (empty before first choice)
    std::vector<Rule> rules;     // legacy per-device rules; only populated when loading an
                                 // old config file, moved into a rule set by LoadConfig
};

struct AppConfig {
    std::vector<RuleSet> ruleSets;
    std::vector<DeviceRules> devices;
    std::wstring activeRuleSetId;  // fallback set when a device has no lastRuleSetId
    std::wstring language;         // L"zh", L"en", or empty when the language is not pinned yet

    RuleSet* FindRuleSet(const std::wstring& id);
    const RuleSet* FindRuleSet(const std::wstring& id) const;
    RuleSet& FindOrCreateRuleSet(const std::wstring& id, const std::wstring& name);
    DeviceRules* FindDevice(const std::wstring& id);
    const DeviceRules* FindDevice(const std::wstring& id) const;
    DeviceRules& FindOrCreateDevice(const std::wstring& id, const std::wstring& name);
};

// A rule set id that is not used yet ("rs1", "rs2", ...).
std::wstring NewRuleSetId(const AppConfig& config);

struct DeviceIdentity {
    std::wstring id;
    std::wstring name;
};

struct FileInfo {
    bool exists = false;
    bool isDirectory = false;
    uint64_t size = 0;
    FILETIME modified{};
};

struct SyncStats {
    int total = 0;
    int copied = 0;
    int skipped = 0;
    int conflicts = 0;
    int errors = 0;
    bool cancelled = false;
    uint64_t bytesCopied = 0;
    std::vector<std::wstring> messages;
    std::wstring summary;
};

struct SyncProgress {
    std::atomic<bool> running{false};
    std::atomic<bool> cancel{false};
    std::atomic<int> filesDone{0};
    std::atomic<int> totalFiles{0};
    std::atomic<uint64_t> bytesDone{0};
    std::atomic<uint64_t> totalBytes{0};
    std::mutex mutex;
    std::wstring phase;
    std::wstring currentFile;
    std::wstring lastMessage;
};

// Paths and files.
std::wstring GetAppDirectory();
std::wstring GetModulePath();
std::wstring JoinPath(const std::wstring& base, const std::wstring& child);
std::wstring NormalizePath(const std::wstring& path);
std::wstring SanitizeFileComponent(const std::wstring& value);
std::wstring GetFileName(const std::wstring& path);
bool PathExists(const std::wstring& path);
bool IsDirectory(const std::wstring& path);
bool IsSamePath(const std::wstring& a, const std::wstring& b);
bool IsSubPath(const std::wstring& parent, const std::wstring& child);
bool EnsureDirectory(const std::wstring& path);
bool GetFileInfo(const std::wstring& path, FileInfo& info);
std::wstring FormatBytes(uint64_t bytes);
std::wstring NowStamp();
std::wstring FormatWin32Error(DWORD error);

// Device identification.
DeviceIdentity GetCurrentDevice();
std::wstring ReadMachineGuid();

// Config. USB paths are stored relative to the app directory when possible,
// so changing the USB drive letter does not break the rules.
std::wstring GetConfigPath();
bool LoadConfig(const std::wstring& path, AppConfig& config, std::wstring& error);
bool SaveConfig(const std::wstring& path, const AppConfig& config, std::wstring& error);
std::wstring ReadStoredLanguage(const std::wstring& path);
std::wstring ResolvePortablePath(const std::wstring& value, const std::wstring& appDir);
std::wstring MakePortablePath(const std::wstring& value, const std::wstring& appDir);
std::wstring DefaultUsbRulePath(const std::wstring& appDir, const std::wstring& deviceName,
                                const std::wstring& ruleName, const std::wstring& kind,
                                const std::wstring& pcPath);

// Sync engine.
bool SyncDevice(const DeviceRules& device, const std::wstring& appDir,
                const std::wstring& logPath, SyncProgress* progress,
                SyncStats& stats);
// Builds the localized one-line summary shown after a sync run. Kept separate
// so the UI can rebuild it after a language switch.
std::wstring BuildSummary(const SyncStats& stats);

std::wstring DirectionLabel(const std::wstring& mode);

} // namespace ss
