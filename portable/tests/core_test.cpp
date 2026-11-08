#include "core.h"

#include <filesystem>
#include <fstream>
#include <iostream>
#include <thread>

namespace {

int failures = 0;
int checks = 0;

void Check(bool condition, const char* label) {
    ++checks;
    if (!condition) {
        ++failures;
        std::wcerr << L"FAIL: " << label << L"\n";
    } else {
        std::wcout << L"ok   " << label << L"\n";
    }
}

bool WriteText(const std::wstring& path, const std::string& text) {
    std::ofstream stream(std::filesystem::path(path), std::ios::binary | std::ios::trunc);
    if (!stream) {
        return false;
    }
    stream.write(text.data(), static_cast<std::streamsize>(text.size()));
    return stream.good();
}

std::string ReadText(const std::wstring& path) {
    std::ifstream stream(std::filesystem::path(path), std::ios::binary);
    std::string text((std::istreambuf_iterator<char>(stream)), std::istreambuf_iterator<char>());
    return text;
}

std::string ToUtf8(const std::wstring& text) {
    if (text.empty()) {
        return std::string();
    }
    int wide = static_cast<int>(text.size());
    int needed =
        WideCharToMultiByte(CP_UTF8, 0, text.c_str(), wide, nullptr, 0, nullptr, nullptr);
    std::string result(static_cast<size_t>(needed), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.c_str(), wide, result.data(), needed, nullptr, nullptr);
    return result;
}

bool SetTimestamp(const std::wstring& path, const FILETIME& time) {
    HANDLE file = CreateFileW(path.c_str(), FILE_WRITE_ATTRIBUTES,
                              FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        return false;
    }
    bool ok = SetFileTime(file, nullptr, nullptr, &time) != FALSE;
    CloseHandle(file);
    return ok;
}

FILETIME MakeTimestamp(int secondsOffset) {
    FILETIME time{};
    GetSystemTimeAsFileTime(&time);
    ULARGE_INTEGER value{};
    value.LowPart = time.dwLowDateTime;
    value.HighPart = time.dwHighDateTime;
    value.QuadPart += static_cast<unsigned long long>(secondsOffset) * 10000000ULL;
    time.dwLowDateTime = value.LowPart;
    time.dwHighDateTime = value.HighPart;
    return time;
}

void ResetProgress(ss::SyncProgress& progress) {
    progress.running = false;
    progress.cancel = false;
    progress.filesDone = 0;
    progress.totalFiles = 0;
    progress.bytesDone = 0;
    progress.totalBytes = 0;
    std::lock_guard<std::mutex> lock(progress.mutex);
    progress.phase.clear();
    progress.currentFile.clear();
    progress.lastMessage.clear();
}

void RemoveTree(const std::wstring& path) {
    std::error_code ec;
    std::filesystem::remove_all(std::filesystem::path(path), ec);
}

} // namespace

int wmain() {
    std::wcout << L"SyncStick Portable core tests\n";
    std::wstring root = ss::JoinPath(ss::GetAppDirectory(), L"test-data");
    RemoveTree(root);
    std::wstring pcRoot = ss::JoinPath(root, L"pc");
    std::wstring usbRoot = ss::JoinPath(root, L"usb");
    ss::EnsureDirectory(ss::JoinPath(pcRoot, L"project"));
    ss::EnsureDirectory(ss::JoinPath(pcRoot, L"project\\sub"));
    ss::EnsureDirectory(ss::JoinPath(usbRoot, L"project"));
    ss::EnsureDirectory(ss::JoinPath(usbRoot, L"project\\sub"));
    WriteText(ss::JoinPath(pcRoot, L"project\\a.txt"), "hello");
    WriteText(ss::JoinPath(pcRoot, L"project\\sub\\b.txt"), "world");
    SetTimestamp(ss::JoinPath(pcRoot, L"project\\a.txt"), MakeTimestamp(-60));
    SetTimestamp(ss::JoinPath(pcRoot, L"project\\sub\\b.txt"), MakeTimestamp(-50));

    ss::DeviceIdentity identity = ss::GetCurrentDevice();
    Check(!identity.id.empty(), "device id non-empty");
    Check(identity.id == ss::GetCurrentDevice().id, "device id stable");
    Check(!identity.name.empty(), "device name non-empty");

    ss::DeviceRules device;
    device.id = L"TEST";
    device.name = L"测试电脑";
    ss::Rule folderRule;
    folderRule.name = L"项目资料";
    folderRule.kind = L"folder";
    folderRule.mode = L"both";
    folderRule.pcPath = ss::JoinPath(pcRoot, L"project");
    folderRule.usbPath = ss::JoinPath(usbRoot, L"project");
    device.rules.push_back(folderRule);

    ss::SyncStats stats;
    ss::SyncProgress progress;
    std::wstring logPath = ss::JoinPath(root, L"test.log");
    bool ok = ss::SyncDevice(device, root, logPath, &progress, stats);
    Check(ok, "first sync succeeds");
    std::wcout << L"first stats: total=" << stats.total << L" copied=" << stats.copied
               << L" skipped=" << stats.skipped << L" conflicts=" << stats.conflicts
               << L" errors=" << stats.errors << L"\n";
    for (const auto& message : stats.messages) {
        std::wcout << L"  " << message << L"\n";
    }
    Check(stats.copied == 2, "first sync copied two files");
    Check(stats.errors == 0, "first sync no errors");
    Check(ReadText(ss::JoinPath(usbRoot, L"project\\a.txt")) == "hello", "a.txt copied to USB");
    Check(ReadText(ss::JoinPath(usbRoot, L"project\\sub\\b.txt")) == "world",
          "b.txt copied to USB");

    stats = ss::SyncStats{};
    ResetProgress(progress);
    ok = ss::SyncDevice(device, root, logPath, &progress, stats);
    Check(ok, "second sync succeeds");
    Check(stats.copied == 0, "second sync copies nothing");
    Check(stats.skipped == 2, "second sync skips both files");

    WriteText(ss::JoinPath(pcRoot, L"project\\a.txt"), "hello updated");
    SetTimestamp(ss::JoinPath(pcRoot, L"project\\a.txt"), MakeTimestamp(60));
    stats = ss::SyncStats{};
    ResetProgress(progress);
    ok = ss::SyncDevice(device, root, logPath, &progress, stats);
    Check(ok, "pc update sync succeeds");
    Check(stats.copied == 1, "pc update copies one file");
    Check(ReadText(ss::JoinPath(usbRoot, L"project\\a.txt")) == "hello updated",
          "pc update reaches USB");

    WriteText(ss::JoinPath(usbRoot, L"project\\sub\\b.txt"), "world from usb");
    SetTimestamp(ss::JoinPath(usbRoot, L"project\\sub\\b.txt"), MakeTimestamp(120));
    stats = ss::SyncStats{};
    ResetProgress(progress);
    ok = ss::SyncDevice(device, root, logPath, &progress, stats);
    Check(ok, "usb update sync succeeds");
    Check(stats.copied == 1, "usb update copies one file");
    Check(ReadText(ss::JoinPath(pcRoot, L"project\\sub\\b.txt")) == "world from usb",
          "usb update reaches PC");

    WriteText(ss::JoinPath(usbRoot, L"project\\extra.txt"), "extra");
    SetTimestamp(ss::JoinPath(usbRoot, L"project\\extra.txt"), MakeTimestamp(30));
    stats = ss::SyncStats{};
    ResetProgress(progress);
    ss::SyncDevice(device, root, logPath, &progress, stats);
    Check(ReadText(ss::JoinPath(pcRoot, L"project\\extra.txt")) == "extra",
          "usb-only file reaches PC");

    // No deletion: a one-way PC->USB rule leaves a USB-only file alone.
    ss::DeviceRules oneWay;
    oneWay.id = L"TEST";
    oneWay.name = L"测试电脑";
    ss::Rule oneWayRule = folderRule;
    oneWayRule.name = L"单向";
    oneWayRule.mode = L"pc2usb";
    oneWay.rules.push_back(oneWayRule);
    WriteText(ss::JoinPath(usbRoot, L"project\\keep.txt"), "keep");
    stats = ss::SyncStats{};
    ResetProgress(progress);
    ss::SyncDevice(oneWay, root, logPath, &progress, stats);
    Check(ss::PathExists(ss::JoinPath(usbRoot, L"project\\keep.txt")),
          "one-way sync never deletes USB files");

    // Same-size conflict is intentionally metadata-based; a size mismatch with
    // identical timestamps must be reported as a conflict and not copied.
    WriteText(ss::JoinPath(pcRoot, L"project\\conflict.txt"), "12345");
    WriteText(ss::JoinPath(usbRoot, L"project\\conflict.txt"), "123456");
    FILETIME conflictTime = MakeTimestamp(240);
    SetTimestamp(ss::JoinPath(pcRoot, L"project\\conflict.txt"), conflictTime);
    SetTimestamp(ss::JoinPath(usbRoot, L"project\\conflict.txt"), conflictTime);
    stats = ss::SyncStats{};
    ResetProgress(progress);
    ss::SyncDevice(device, root, logPath, &progress, stats);
    Check(stats.conflicts >= 1, "same-time size mismatch reports conflict");
    Check(ReadText(ss::JoinPath(usbRoot, L"project\\conflict.txt")) == "123456",
          "conflict keeps USB copy");

    ss::AppConfig config;
    ss::DeviceRules& savedDevice = config.FindOrCreateDevice(L"TEST", L"测试电脑");
    ss::RuleSet& savedSet = config.FindOrCreateRuleSet(L"rs1", L"携带规则");
    ss::Rule savedRule = folderRule;
    savedRule.usbPath = ss::JoinPath(ss::GetAppDirectory(), L"test-data\\usb\\project");
    savedSet.rules.push_back(savedRule);
    config.activeRuleSetId = L"rs1";
    savedDevice.lastRuleSetId = L"rs1";
    config.language = L"en";
    std::wstring configPath = ss::JoinPath(root, L"SyncStickPortable.ini");
    std::wstring error;
    Check(ss::SaveConfig(configPath, config, error), "config save succeeds");
    std::string savedText = ReadText(configPath);
    Check(savedText.find("[ruleset.rs1]") != std::string::npos, "config stores rule sets");
    Check(savedText.find("[rule.TEST.") == std::string::npos,
          "config stops writing legacy rule sections");
    ss::AppConfig loaded;
    Check(ss::LoadConfig(configPath, loaded, error), "config load succeeds");
    ss::DeviceRules* loadedDevice = loaded.FindDevice(L"TEST");
    Check(loadedDevice != nullptr, "config roundtrips device");
    Check(loadedDevice != nullptr && loadedDevice->lastRuleSetId == L"rs1",
          "config roundtrips last rule set");
    Check(loaded.activeRuleSetId == L"rs1", "config roundtrips active rule set");
    ss::RuleSet* loadedSet = loaded.FindRuleSet(L"rs1");
    Check(loadedSet != nullptr && loadedSet->name == L"携带规则",
          "config roundtrips rule set name");
    Check(loadedSet != nullptr && loadedSet->rules.size() == 1, "config roundtrips rule");
    if (loadedSet != nullptr && !loadedSet->rules.empty()) {
        Check(loadedSet->rules[0].mode == L"both", "config roundtrips mode");
        Check(ss::IsSamePath(loadedSet->rules[0].pcPath, folderRule.pcPath),
              "config roundtrips pc path");
        Check(ss::IsSamePath(loadedSet->rules[0].usbPath, savedRule.usbPath),
              "config roundtrips usb path");
        Check(loadedSet->rules[0].kind == L"folder", "config roundtrips kind");
    }

    // A v1.0.x ini stores rules per device. Loading it must migrate those rules
    // into a rule set named after the device, without losing anything.
    std::wstring legacyPath = ss::JoinPath(root, L"legacy.ini");
    std::wstring legacyText =
        std::wstring(L"# SyncStick Portable\n\n[device.TEST]\nname=测试电脑\n\n") +
        L"[rule.TEST.1]\nname=旧规则\npc=" + folderRule.pcPath +
        L"\nusb=旧目录\\项目\nkind=folder\nmode=both\nenabled=1\n";
    Check(WriteText(legacyPath, ToUtf8(legacyText)), "legacy config written");
    ss::AppConfig migrated;
    Check(ss::LoadConfig(legacyPath, migrated, error), "legacy config loads");
    Check(migrated.ruleSets.size() == 1, "legacy rules migrate into one rule set");
    if (migrated.ruleSets.size() == 1) {
        const ss::RuleSet& migratedSet = migrated.ruleSets[0];
        Check(migratedSet.name == L"测试电脑", "migrated rule set takes device name");
        Check(migratedSet.rules.size() == 1 && migratedSet.rules[0].name == L"旧规则",
              "migration keeps rules");
        Check(migrated.activeRuleSetId == migratedSet.id, "migration activates the rule set");
        ss::DeviceRules* migratedDevice = migrated.FindDevice(L"TEST");
        Check(migratedDevice != nullptr && migratedDevice->lastRuleSetId == migratedSet.id,
              "migration records per-device last rule set");
        std::wstring migratedPath = ss::JoinPath(root, L"migrated.ini");
        Check(ss::SaveConfig(migratedPath, migrated, error), "migrated config saves");
        ss::AppConfig reloaded;
        Check(ss::LoadConfig(migratedPath, reloaded, error), "migrated config reloads");
        Check(reloaded.ruleSets.size() == 1 && reloaded.ruleSets[0].rules.size() == 1 &&
                  reloaded.ruleSets[0].rules[0].name == L"旧规则",
              "migration survives save and reload");
        std::string migratedText = ReadText(migratedPath);
        Check(migratedText.find("[rule.TEST.") == std::string::npos,
              "migrated config writes no legacy sections");
    }

    std::wcout << L"\n" << checks - failures << L"/" << checks << L" checks passed\n";
    if (failures != 0) {
        std::wcerr << failures << L" checks failed\n";
        return 1;
    }
    return 0;
}


