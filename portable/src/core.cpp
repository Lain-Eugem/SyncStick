#include "core.h"

#include "i18n.h"

#include <algorithm>
#include <cwctype>
#include <filesystem>
#include <set>
#include <sstream>
#include <vector>

namespace ss {
namespace {

constexpr LONGLONG kFileTimeTolerance = 3LL * 10000000LL; // 3 seconds in 100 ns units
constexpr uint64_t kCopyHeadroom = 8ULL * 1024ULL * 1024ULL;

std::wstring ToLower(std::wstring value) {
    for (auto& ch : value) {
        ch = static_cast<wchar_t>(std::towlower(ch));
    }
    return value;
}

std::wstring Trim(const std::wstring& value) {
    size_t begin = 0;
    while (begin < value.size() && std::iswspace(value[begin])) {
        ++begin;
    }
    size_t end = value.size();
    while (end > begin && std::iswspace(value[end - 1])) {
        --end;
    }
    return value.substr(begin, end - begin);
}

bool StartsWithI(const std::wstring& value, const std::wstring& prefix) {
    if (value.size() < prefix.size()) {
        return false;
    }
    return _wcsnicmp(value.c_str(), prefix.c_str(), prefix.size()) == 0;
}

bool EndsWithI(const std::wstring& value, const std::wstring& suffix) {
    if (value.size() < suffix.size()) {
        return false;
    }
    return _wcsnicmp(value.c_str() + value.size() - suffix.size(), suffix.c_str(), suffix.size()) == 0;
}

bool IsAbsolutePath(const std::wstring& value) {
    if (value.size() >= 2 && std::iswalpha(value[0]) && value[1] == L':') {
        return true;
    }
    return value.rfind(L"\\\\", 0) == 0;
}

std::string NarrowUtf8(const std::wstring& value) {
    if (value.empty()) {
        return {};
    }
    int needed = WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()),
                                     nullptr, 0, nullptr, nullptr);
    if (needed <= 0) {
        return {};
    }
    std::string result(static_cast<size_t>(needed), '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()),
                        result.data(), needed, nullptr, nullptr);
    return result;
}

std::wstring WidenUtf8(const std::string& value) {
    if (value.empty()) {
        return {};
    }
    int needed = MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()),
                                     nullptr, 0);
    if (needed <= 0) {
        return {};
    }
    std::wstring result(static_cast<size_t>(needed), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()),
                        result.data(), needed);
    return result;
}

bool ReadTextFileUtf8(const std::wstring& path, std::wstring& text, std::wstring& error) {
    text.clear();
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                              nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        DWORD code = GetLastError();
        if (code == ERROR_FILE_NOT_FOUND || code == ERROR_PATH_NOT_FOUND) {
            return true;
        }
        error = TF(L"读取配置失败：%s", FormatWin32Error(code));
        return false;
    }
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file, &size)) {
        error = TF(L"读取配置失败：%s", FormatWin32Error(GetLastError()));
        CloseHandle(file);
        return false;
    }
    std::string bytes(static_cast<size_t>(size.QuadPart), '\0');
    DWORD read = 0;
    bool ok = true;
    if (!bytes.empty()) {
        ok = ReadFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &read, nullptr) != FALSE;
        if (!ok) {
            error = TF(L"读取配置失败：%s", FormatWin32Error(GetLastError()));
        }
    }
    CloseHandle(file);
    if (!ok) {
        return false;
    }
    bytes.resize(read);
    if (bytes.size() >= 3 && static_cast<unsigned char>(bytes[0]) == 0xEF &&
        static_cast<unsigned char>(bytes[1]) == 0xBB &&
        static_cast<unsigned char>(bytes[2]) == 0xBF) {
        bytes.erase(0, 3);
    }
    text = WidenUtf8(bytes);
    return true;
}

bool WriteTextFileUtf8Atomic(const std::wstring& path, const std::wstring& text,
                             std::wstring& error) {
    std::wstring normalized = NormalizePath(path);
    std::filesystem::path fsPath(normalized);
    std::error_code ec;
    if (fsPath.has_parent_path()) {
        std::filesystem::create_directories(fsPath.parent_path(), ec);
        if (ec) {
            error = TF(L"创建目录失败：%s", WidenUtf8(ec.message()));
            return false;
        }
    }

    std::string bytes = NarrowUtf8(text);
    const char bom[3] = {static_cast<char>(0xEF), static_cast<char>(0xBB), static_cast<char>(0xBF)};
    std::wstring temp = normalized + L".syncstick-tmp";
    HANDLE file = CreateFileW(temp.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        error = TF(L"写入配置失败：%s", FormatWin32Error(GetLastError()));
        return false;
    }
    DWORD written = 0;
    bool ok = WriteFile(file, bom, 3, &written, nullptr) != FALSE && written == 3;
    if (ok && !bytes.empty()) {
        ok = WriteFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr) != FALSE &&
             written == bytes.size();
    }
    if (ok) {
        ok = FlushFileBuffers(file) != FALSE;
    }
    CloseHandle(file);
    if (!ok) {
        error = TF(L"写入配置失败：%s", FormatWin32Error(GetLastError()));
        DeleteFileW(temp.c_str());
        return false;
    }

    if (!MoveFileExW(temp.c_str(), normalized.c_str(),
                     MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        error = TF(L"替换配置失败：%s", FormatWin32Error(GetLastError()));
        DeleteFileW(temp.c_str());
        return false;
    }
    return true;
}

std::wstring LongPath(const std::wstring& path) {
    if (path.size() < 240 || path.rfind(L"\\\\?\\", 0) == 0) {
        return path;
    }
    if (path.rfind(L"\\\\", 0) == 0) {
        return L"\\\\?\\UNC\\" + path.substr(2);
    }
    return L"\\\\?\\" + path;
}

LONGLONG FileTimeTicks(const FILETIME& time) {
    ULARGE_INTEGER value{};
    value.LowPart = time.dwLowDateTime;
    value.HighPart = time.dwHighDateTime;
    return static_cast<LONGLONG>(value.QuadPart);
}

std::wstring FileTimeText(const FILETIME& time) {
    if (time.dwLowDateTime == 0 && time.dwHighDateTime == 0) {
        return L"—";
    }
    FILETIME local{};
    SYSTEMTIME system{};
    FileTimeToLocalFileTime(&time, &local);
    FileTimeToSystemTime(&local, &system);
    wchar_t buffer[64]{};
    swprintf_s(buffer, L"%04u-%02u-%02u %02u:%02u:%02u",
               system.wYear, system.wMonth, system.wDay,
               system.wHour, system.wMinute, system.wSecond);
    return buffer;
}

class LogWriter {
public:
    ~LogWriter() { Close(); }

    bool Open(const std::wstring& path) {
        Close();
        if (path.empty()) {
            return false;
        }
        std::error_code ec;
        std::filesystem::path fsPath(NormalizePath(path));
        if (fsPath.has_parent_path()) {
            std::filesystem::create_directories(fsPath.parent_path(), ec);
        }
        _file = CreateFileW(fsPath.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr,
                            OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (_file == INVALID_HANDLE_VALUE) {
            return false;
        }
        LARGE_INTEGER size{};
        GetFileSizeEx(_file, &size);
        SetFilePointer(_file, 0, nullptr, FILE_END);
        if (size.QuadPart == 0) {
            const char bom[3] = {static_cast<char>(0xEF), static_cast<char>(0xBB),
                                 static_cast<char>(0xBF)};
            DWORD written = 0;
            WriteFile(_file, bom, 3, &written, nullptr);
        }
        return true;
    }

    void Line(const std::wstring& line) {
        std::lock_guard<std::mutex> lock(_mutex);
        if (_file == INVALID_HANDLE_VALUE) {
            return;
        }
        std::wstring full = L"[" + NowStamp() + L"] " + line + L"\r\n";
        std::string bytes = NarrowUtf8(full);
        DWORD written = 0;
        WriteFile(_file, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr);
        FlushFileBuffers(_file);
    }

    void Close() {
        std::lock_guard<std::mutex> lock(_mutex);
        if (_file != INVALID_HANDLE_VALUE) {
            CloseHandle(_file);
            _file = INVALID_HANDLE_VALUE;
        }
    }

private:
    HANDLE _file = INVALID_HANDLE_VALUE;
    std::mutex _mutex;
};

struct CaseInsensitiveLess {
    bool operator()(const std::wstring& left, const std::wstring& right) const {
        int compare = _wcsicmp(left.c_str(), right.c_str());
        if (compare != 0) {
            return compare < 0;
        }
        return left < right;
    }
};

bool ShouldIgnoreName(const std::wstring& name, const std::wstring& appDir,
                      const std::wstring& fullPath) {
    std::wstring lower = ToLower(name);
    if (lower == L"thumbs.db" || lower == L"desktop.ini" || lower == L".ds_store" ||
        lower == L"syncstickportable.exe" || lower == L"syncstickportable.ini" ||
        lower == L"syncstickportable.log" || lower == L"autorun.inf") {
        return true;
    }
    if (lower == L"$recycle.bin" || lower == L"system volume information") {
        return true;
    }
    if (EndsWithI(lower, L".syncstick-tmp")) {
        return true;
    }
    // Never let a rule recursively copy the portable app's own data folder.
    if (IsSamePath(fullPath, appDir)) {
        return true;
    }
    return false;
}

std::wstring ParentDirectory(const std::wstring& path) {
    std::filesystem::path p(path);
    if (!p.has_parent_path()) {
        return {};
    }
    return NormalizePath(p.parent_path().wstring());
}

bool EnsureParentDirectory(const std::wstring& path, std::wstring& error) {
    std::wstring parent = ParentDirectory(path);
    if (parent.empty()) {
        return true;
    }
    if (!EnsureDirectory(parent)) {
        error = TF(L"无法创建目录：%s", parent);
        return false;
    }
    return true;
}

std::wstring RelativeSubPath(const std::wstring& root, const std::wstring& full) {
    std::wstring normalizedRoot = NormalizePath(root);
    std::wstring normalizedFull = NormalizePath(full);
    if (normalizedRoot.size() >= 2 && normalizedRoot[1] == L':') {
        // NormalizePath keeps roots like C:\ intact; no change needed.
    }
    if (!IsSubPath(normalizedRoot, normalizedFull)) {
        return {};
    }
    if (IsSamePath(normalizedRoot, normalizedFull)) {
        return L".";
    }
    size_t offset = normalizedRoot.size();
    while (offset < normalizedFull.size() && (normalizedFull[offset] == L'\\' || normalizedFull[offset] == L'/')) {
        ++offset;
    }
    return normalizedFull.substr(offset);
}

bool EnumerateFolder(const std::wstring& root, const std::wstring& appDir,
                     std::map<std::wstring, FileInfo, CaseInsensitiveLess>& files) {
    files.clear();
    if (!PathExists(root)) {
        return true;
    }
    if (!IsDirectory(root)) {
        return false;
    }

    std::error_code ec;
    std::filesystem::path rootPath(root);
    std::filesystem::recursive_directory_iterator iterator(
        rootPath, std::filesystem::directory_options::skip_permission_denied, ec);
    std::filesystem::recursive_directory_iterator end;
    if (ec) {
        return false;
    }
    while (iterator != end) {
        if (ec) {
            ec.clear();
        }
        const std::filesystem::directory_entry& entry = *iterator;
        std::wstring full = NormalizePath(entry.path().wstring());
        DWORD attributes = GetFileAttributesW(full.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES) {
            iterator.increment(ec);
            continue;
        }
        if ((attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
            iterator.disable_recursion_pending();
            iterator.increment(ec);
            continue;
        }
        if ((attributes & FILE_ATTRIBUTE_DIRECTORY) == 0) {
            std::wstring name = entry.path().filename().wstring();
            if (!ShouldIgnoreName(name, appDir, full)) {
                std::wstring relative = RelativeSubPath(root, full);
                if (!relative.empty() && relative != L".") {
                    FileInfo info;
                    if (GetFileInfo(full, info)) {
                        files[relative] = info;
                    }
                }
            }
        }
        iterator.increment(ec);
    }
    return true;
}

std::wstring ModeText(const std::wstring& mode) {
    if (mode == L"pc2usb") {
        return T(L"电脑 → U 盘");
    }
    if (mode == L"usb2pc") {
        return T(L"U 盘 → 电脑");
    }
    return T(L"双向同步");
}

} // namespace

std::wstring FormatWin32Error(DWORD error) {
    wchar_t* buffer = nullptr;
    DWORD length = FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM |
                                      FORMAT_MESSAGE_IGNORE_INSERTS,
                                  nullptr, error, 0, reinterpret_cast<wchar_t*>(&buffer), 0,
                                  nullptr);
    std::wstring result;
    if (length > 0 && buffer != nullptr) {
        result.assign(buffer, length);
        while (!result.empty() && (result.back() == L'\r' || result.back() == L'\n' ||
                                   result.back() == L' ')) {
            result.pop_back();
        }
    } else {
        result = TF(L"错误 %s", static_cast<unsigned long>(error));
    }
    if (buffer != nullptr) {
        LocalFree(buffer);
    }
    return result;
}

std::wstring GetModulePath() {
    std::vector<wchar_t> buffer(512);
    for (;;) {
        DWORD length = GetModuleFileNameW(nullptr, buffer.data(),
                                          static_cast<DWORD>(buffer.size()));
        if (length == 0) {
            return {};
        }
        if (length < buffer.size() - 1) {
            return std::wstring(buffer.data(), length);
        }
        buffer.resize(buffer.size() * 2);
    }
}

std::wstring GetAppDirectory() {
    std::wstring module = GetModulePath();
    if (module.empty()) {
        return NormalizePath(L".");
    }
    std::filesystem::path path(module);
    return NormalizePath(path.parent_path().wstring());
}

std::wstring JoinPath(const std::wstring& base, const std::wstring& child) {
    if (child.empty()) {
        return NormalizePath(base);
    }
    if (IsAbsolutePath(child)) {
        return NormalizePath(child);
    }
    std::wstring result = base;
    if (!result.empty() && result.back() != L'\\' && result.back() != L'/') {
        result.push_back(L'\\');
    }
    result += child;
    return NormalizePath(result);
}

std::wstring NormalizePath(const std::wstring& path) {
    if (path.empty()) {
        return {};
    }
    DWORD needed = GetFullPathNameW(path.c_str(), 0, nullptr, nullptr);
    if (needed == 0) {
        return path;
    }
    std::vector<wchar_t> buffer(needed + 1);
    DWORD length = GetFullPathNameW(path.c_str(), static_cast<DWORD>(buffer.size()),
                                    buffer.data(), nullptr);
    if (length == 0) {
        return path;
    }
    std::wstring result(buffer.data(), length);
    std::replace(result.begin(), result.end(), L'/', L'\\');
    while (result.size() > 3 && result.back() == L'\\') {
        result.pop_back();
    }
    return result;
}

std::wstring SanitizeFileComponent(const std::wstring& value) {
    std::wstring result;
    result.reserve(value.size());
    for (wchar_t ch : value) {
        if (ch < 32 || wcschr(L"<>:\"/\\|?*", ch) != nullptr) {
            result.push_back(L'_');
        } else {
            result.push_back(ch);
        }
    }
    while (!result.empty() && (result.back() == L' ' || result.back() == L'.')) {
        result.pop_back();
    }
    if (result.empty()) {
        result = L"SyncStick";
    }
    if (result.size() > 80) {
        result.resize(80);
    }
    return result;
}

std::wstring GetFileName(const std::wstring& path) {
    size_t pos = path.find_last_of(L"\\/");
    if (pos == std::wstring::npos) {
        return path;
    }
    return path.substr(pos + 1);
}

bool PathExists(const std::wstring& path) {
    DWORD attributes = GetFileAttributesW(LongPath(path).c_str());
    return attributes != INVALID_FILE_ATTRIBUTES;
}

bool IsDirectory(const std::wstring& path) {
    DWORD attributes = GetFileAttributesW(LongPath(path).c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
}

bool IsSamePath(const std::wstring& a, const std::wstring& b) {
    if (a.empty() || b.empty()) {
        return false;
    }
    return _wcsicmp(NormalizePath(a).c_str(), NormalizePath(b).c_str()) == 0;
}

bool IsSubPath(const std::wstring& parent, const std::wstring& child) {
    if (parent.empty() || child.empty()) {
        return false;
    }
    std::wstring p = NormalizePath(parent);
    std::wstring c = NormalizePath(child);
    if (_wcsicmp(p.c_str(), c.c_str()) == 0) {
        return true;
    }
    std::wstring prefix = p;
    if (!prefix.empty() && prefix.back() != L'\\') {
        prefix.push_back(L'\\');
    }
    return _wcsnicmp(c.c_str(), prefix.c_str(), prefix.size()) == 0;
}

bool EnsureDirectory(const std::wstring& path) {
    if (path.empty()) {
        return false;
    }
    std::error_code ec;
    if (std::filesystem::is_directory(LongPath(path), ec)) {
        return true;
    }
    std::filesystem::create_directories(LongPath(path), ec);
    if (ec) {
        return false;
    }
    return std::filesystem::is_directory(LongPath(path), ec);
}

bool GetFileInfo(const std::wstring& path, FileInfo& info) {
    info = FileInfo{};
    WIN32_FILE_ATTRIBUTE_DATA data{};
    if (!GetFileAttributesExW(LongPath(path).c_str(), GetFileExInfoStandard, &data)) {
        return false;
    }
    info.exists = true;
    info.isDirectory = (data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
    info.size = (static_cast<uint64_t>(data.nFileSizeHigh) << 32) | data.nFileSizeLow;
    info.modified = data.ftLastWriteTime;
    return true;
}

std::wstring FormatBytes(uint64_t bytes) {
    wchar_t buffer[64]{};
    if (bytes < 1024) {
        swprintf_s(buffer, L"%llu B", static_cast<unsigned long long>(bytes));
    } else if (bytes < 1024ULL * 1024ULL) {
        swprintf_s(buffer, L"%.1f KB", static_cast<double>(bytes) / 1024.0);
    } else if (bytes < 1024ULL * 1024ULL * 1024ULL) {
        swprintf_s(buffer, L"%.1f MB", static_cast<double>(bytes) / (1024.0 * 1024.0));
    } else {
        swprintf_s(buffer, L"%.2f GB", static_cast<double>(bytes) / (1024.0 * 1024.0 * 1024.0));
    }
    return buffer;
}

std::wstring NowStamp() {
    SYSTEMTIME time{};
    GetLocalTime(&time);
    wchar_t buffer[32]{};
    swprintf_s(buffer, L"%04u-%02u-%02u %02u:%02u:%02u",
               time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute, time.wSecond);
    return buffer;
}

std::wstring DirectionLabel(const std::wstring& mode) {
    return ModeText(mode);
}

DeviceRules* AppConfig::FindDevice(const std::wstring& id) {
    for (auto& device : devices) {
        if (_wcsicmp(device.id.c_str(), id.c_str()) == 0) {
            return &device;
        }
    }
    return nullptr;
}

const DeviceRules* AppConfig::FindDevice(const std::wstring& id) const {
    for (const auto& device : devices) {
        if (_wcsicmp(device.id.c_str(), id.c_str()) == 0) {
            return &device;
        }
    }
    return nullptr;
}

DeviceRules& AppConfig::FindOrCreateDevice(const std::wstring& id, const std::wstring& name) {
    if (DeviceRules* device = FindDevice(id)) {
        if (!name.empty()) {
            device->name = name;
        }
        return *device;
    }
    DeviceRules device;
    device.id = id;
    device.name = name.empty() ? id : name;
    devices.push_back(std::move(device));
    return devices.back();
}

RuleSet* AppConfig::FindRuleSet(const std::wstring& id) {
    for (auto& ruleSet : ruleSets) {
        if (_wcsicmp(ruleSet.id.c_str(), id.c_str()) == 0) {
            return &ruleSet;
        }
    }
    return nullptr;
}

const RuleSet* AppConfig::FindRuleSet(const std::wstring& id) const {
    for (const auto& ruleSet : ruleSets) {
        if (_wcsicmp(ruleSet.id.c_str(), id.c_str()) == 0) {
            return &ruleSet;
        }
    }
    return nullptr;
}

RuleSet& AppConfig::FindOrCreateRuleSet(const std::wstring& id, const std::wstring& name) {
    if (RuleSet* ruleSet = FindRuleSet(id)) {
        if (!name.empty()) {
            ruleSet->name = name;
        }
        return *ruleSet;
    }
    RuleSet ruleSet;
    ruleSet.id = id;
    ruleSet.name = name;
    ruleSets.push_back(std::move(ruleSet));
    return ruleSets.back();
}

std::wstring NewRuleSetId(const AppConfig& config) {
    for (int number = 1;; ++number) {
        std::wstring candidate = L"rs" + std::to_wstring(number);
        if (config.FindRuleSet(candidate) == nullptr) {
            return candidate;
        }
    }
}

std::wstring ReadMachineGuid() {
    HKEY key = nullptr;
    const wchar_t* subkey = L"SOFTWARE\\Microsoft\\Cryptography";
    DWORD disposition = 0;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, subkey, 0,
                      KEY_READ | KEY_WOW64_64KEY, &key) != ERROR_SUCCESS) {
        // 32-bit view fallback (the portable app is x64, but keep this for
        // structurally odd images).
        if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, subkey, 0,
                          KEY_READ | KEY_WOW64_32KEY, &key) != ERROR_SUCCESS) {
            return {};
        }
    }
    (void)disposition;
    wchar_t buffer[256]{};
    DWORD size = sizeof(buffer);
    DWORD type = 0;
    LSTATUS status = RegQueryValueExW(key, L"MachineGuid", nullptr, &type,
                                      reinterpret_cast<LPBYTE>(buffer), &size);
    RegCloseKey(key);
    if (status != ERROR_SUCCESS || (type != REG_SZ && type != REG_EXPAND_SZ)) {
        return {};
    }
    size_t length = size / sizeof(wchar_t);
    while (length > 0 && buffer[length - 1] == L'\0') {
        --length;
    }
    return std::wstring(buffer, length);
}

std::wstring GetComputerNameText() {
    wchar_t buffer[MAX_COMPUTERNAME_LENGTH + 1]{};
    DWORD size = static_cast<DWORD>(std::size(buffer));
    if (!GetComputerNameW(buffer, &size)) {
        return T(L"这台电脑");
    }
    return buffer;
}

DeviceIdentity GetCurrentDevice() {
    DeviceIdentity identity;
    identity.name = GetComputerNameText();
    std::wstring guid = ReadMachineGuid();
    if (!guid.empty()) {
        std::string bytes = NarrowUtf8(ToLower(guid));
        uint64_t hash = 1469598103934665603ULL;
        for (unsigned char ch : bytes) {
            hash ^= ch;
            hash *= 1099511628211ULL;
        }
        wchar_t buffer[32]{};
        swprintf_s(buffer, L"%012llX", static_cast<unsigned long long>(hash & 0xFFFFFFFFFFFFULL));
        identity.id = buffer;
    } else {
        std::wstring seed = identity.name;
        wchar_t windows[MAX_PATH]{};
        UINT length = GetWindowsDirectoryW(windows, MAX_PATH);
        if (length > 0) {
            std::wstring root(windows, std::min<UINT>(length, 3));
            DWORD serial = 0;
            if (GetVolumeInformationW(root.c_str(), nullptr, 0, &serial, nullptr, nullptr,
                                      nullptr, 0)) {
                wchar_t serialText[32]{};
                swprintf_s(serialText, L"-%08lX", static_cast<unsigned long>(serial));
                seed += serialText;
            }
        }
        uint64_t hash = 1469598103934665603ULL;
        for (wchar_t ch : seed) {
            hash ^= static_cast<uint64_t>(ch);
            hash *= 1099511628211ULL;
        }
        wchar_t buffer[32]{};
        swprintf_s(buffer, L"%012llX", static_cast<unsigned long long>(hash & 0xFFFFFFFFFFFFULL));
        identity.id = buffer;
    }
    if (identity.id.empty()) {
        identity.id = L"UNKNOWN";
    }
    if (identity.name.empty()) {
        identity.name = identity.id;
    }
    return identity;
}

std::wstring GetConfigPath() {
    return JoinPath(GetAppDirectory(), L"SyncStickPortable.ini");
}

std::wstring ResolvePortablePath(const std::wstring& value, const std::wstring& appDir) {
    std::wstring trimmed = Trim(value);
    if (trimmed.empty()) {
        return {};
    }
    if (IsAbsolutePath(trimmed)) {
        return NormalizePath(trimmed);
    }
    return NormalizePath(JoinPath(appDir, trimmed));
}

std::wstring MakePortablePath(const std::wstring& value, const std::wstring& appDir) {
    std::wstring absolute = NormalizePath(value);
    std::wstring base = NormalizePath(appDir);
    if (absolute.empty()) {
        return {};
    }
    if (IsSamePath(base, absolute)) {
        return L".";
    }
    if (IsSubPath(base, absolute)) {
        size_t offset = base.size();
        while (offset < absolute.size() &&
               (absolute[offset] == L'\\' || absolute[offset] == L'/')) {
            ++offset;
        }
        return absolute.substr(offset);
    }
    return absolute;
}

std::wstring DefaultUsbRulePath(const std::wstring& appDir, const std::wstring& deviceName,
                                const std::wstring& ruleName, const std::wstring& kind,
                                const std::wstring& pcPath) {
    std::wstring dataRoot = JoinPath(appDir, L"SyncStickData");
    std::wstring deviceDir = JoinPath(dataRoot, SanitizeFileComponent(deviceName));
    std::wstring name = SanitizeFileComponent(ruleName);
    if (kind == L"file") {
        std::wstring fileName = GetFileName(pcPath);
        if (fileName.empty()) {
            fileName = name;
        }
        return JoinPath(deviceDir, SanitizeFileComponent(fileName));
    }
    return JoinPath(deviceDir, name);
}

bool LoadConfig(const std::wstring& path, AppConfig& config, std::wstring& error) {
    config = AppConfig{};
    error.clear();
    std::wstring text;
    if (!ReadTextFileUtf8(path, text, error)) {
        return false;
    }
    std::wstring appDir = GetAppDirectory();
    std::wstring line;
    std::wstring currentDeviceId;
    DeviceRules* currentDevice = nullptr;
    RuleSet* currentRuleSet = nullptr;
    Rule* currentRule = nullptr;

    auto flushRule = [&]() {
        if (currentRule == nullptr) {
            return;
        }
        if (currentRule->name.empty()) {
            currentRule->name = GetFileName(currentRule->pcPath);
            if (currentRule->name.empty()) {
                currentRule->name = DefaultRuleNameText();
            }
        }
        if (currentRule->kind.empty()) {
            currentRule->kind = L"folder";
        }
        if (currentRule->mode.empty()) {
            currentRule->mode = L"both";
        }
        currentRule = nullptr;
    };

    auto beginRule = [&](std::vector<Rule>& rules) {
        Rule rule;
        rule.kind = L"folder";
        rule.mode = L"both";
        rules.push_back(std::move(rule));
        return &rules.back();
    };

    size_t start = 0;
    while (start <= text.size()) {
        size_t end = text.find(L'\n', start);
        if (end == std::wstring::npos) {
            line = text.substr(start);
            start = text.size() + 1;
        } else {
            line = text.substr(start, end - start);
            start = end + 1;
        }
        if (!line.empty() && line.back() == L'\r') {
            line.pop_back();
        }
        std::wstring trimmed = Trim(line);
        if (trimmed.empty() || trimmed[0] == L'#' || trimmed[0] == L';') {
            continue;
        }
        if (trimmed.front() == L'[') {
            flushRule();
            size_t close = trimmed.find(L']');
            if (close == std::wstring::npos) {
                continue;
            }
            std::wstring section = Trim(trimmed.substr(1, close - 1));
            currentRule = nullptr;
            if (StartsWithI(section, L"device.")) {
                std::wstring deviceId = Trim(section.substr(7));
                currentRuleSet = nullptr;
                if (deviceId.empty()) {
                    currentDevice = nullptr;
                    continue;
                }
                currentDeviceId = deviceId;
                currentDevice = &config.FindOrCreateDevice(deviceId, L"");
            } else if (StartsWithI(section, L"ruleset.")) {
                std::wstring rest = Trim(section.substr(8));
                size_t lastDot = rest.find_last_of(L'.');
                currentDevice = nullptr;
                if (rest.empty()) {
                    currentRuleSet = nullptr;
                    continue;
                }
                if (lastDot == std::wstring::npos) {
                    // "[ruleset.<id>]" - the header of one named rule set.
                    currentRuleSet = &config.FindOrCreateRuleSet(rest, L"");
                } else {
                    // "[ruleset.<id>.<n>]" - one rule inside that set.
                    std::wstring setId = Trim(rest.substr(0, lastDot));
                    std::wstring indexText = Trim(rest.substr(lastDot + 1));
                    if (setId.empty() || indexText.empty()) {
                        currentRuleSet = nullptr;
                        continue;
                    }
                    currentRuleSet = &config.FindOrCreateRuleSet(setId, L"");
                    currentRule = beginRule(currentRuleSet->rules);
                }
            } else if (StartsWithI(section, L"rule.")) {
                // Old per-device sections, migrated to rule sets below.
                std::wstring rest = Trim(section.substr(5));
                currentRuleSet = nullptr;
                size_t lastDot = rest.find_last_of(L'.');
                if (lastDot == std::wstring::npos) {
                    currentDevice = nullptr;
                    continue;
                }
                std::wstring deviceId = Trim(rest.substr(0, lastDot));
                std::wstring indexText = Trim(rest.substr(lastDot + 1));
                if (deviceId.empty() || indexText.empty()) {
                    currentDevice = nullptr;
                    continue;
                }
                currentDeviceId = deviceId;
                currentDevice = &config.FindOrCreateDevice(deviceId, L"");
                currentRule = beginRule(currentDevice->rules);
            } else {
                currentRuleSet = nullptr;
                currentDevice = nullptr;
            }
            continue;
        }

        size_t equals = trimmed.find(L'=');
        if (equals == std::wstring::npos) {
            continue;
        }
        std::wstring key = ToLower(Trim(trimmed.substr(0, equals)));
        std::wstring value = Trim(trimmed.substr(equals + 1));

        // File-level settings may legally appear before or after section
        // headers.  Handle them first so a preceding [device.*] block cannot
        // swallow them.
        if (key == L"language") {
            std::wstring tag;
            if (ParseLanguageTag(value, tag)) {
                config.language = tag;
            }
            continue;
        }
        if (key == L"activeruleset") {
            config.activeRuleSetId = value;
            continue;
        }

        if (currentRule != nullptr) {
            if (key == L"name") {
                currentRule->name = value;
            } else if (key == L"pc") {
                currentRule->pcPath = NormalizePath(value);
            } else if (key == L"usb") {
                currentRule->usbPath = ResolvePortablePath(value, appDir);
            } else if (key == L"kind") {
                std::wstring kind = ToLower(value);
                currentRule->kind = (kind == L"file") ? L"file" : L"folder";
            } else if (key == L"mode") {
                std::wstring mode = ToLower(value);
                if (mode != L"pc2usb" && mode != L"usb2pc") {
                    mode = L"both";
                }
                currentRule->mode = mode;
            } else if (key == L"enabled") {
                currentRule->enabled = !(value == L"0" || _wcsicmp(value.c_str(), L"false") == 0);
            }
        } else if (currentRuleSet != nullptr) {
            if (key == L"name") {
                currentRuleSet->name = value;
            }
        } else if (currentDevice != nullptr) {
            if (key == L"name") {
                currentDevice->name = value;
            } else if (key == L"lastrule") {
                currentDevice->lastRuleSetId = value;
            }
        }
    }
    flushRule();

    // Old configs keep their rules per device.  Move each device's rules into a
    // named rule set so nothing is lost; the device remembers that set.
    for (auto& device : config.devices) {
        if (device.rules.empty()) {
            continue;
        }
        RuleSet ruleSet;
        ruleSet.id = NewRuleSetId(config);
        ruleSet.name = device.name.empty() ? DefaultRuleSetNameText() : device.name;
        ruleSet.rules = std::move(device.rules);
        device.rules.clear();
        if (device.lastRuleSetId.empty()) {
            device.lastRuleSetId = ruleSet.id;
        }
        config.ruleSets.push_back(std::move(ruleSet));
    }

    for (auto& device : config.devices) {
        if (device.name.empty()) {
            device.name = device.id;
        }
    }
    if (config.FindRuleSet(config.activeRuleSetId) == nullptr) {
        config.activeRuleSetId.clear();
    }
    if (config.activeRuleSetId.empty() && !config.ruleSets.empty()) {
        DeviceIdentity identity = GetCurrentDevice();
        const DeviceRules* device = config.FindDevice(identity.id);
        if (device != nullptr && config.FindRuleSet(device->lastRuleSetId) != nullptr) {
            config.activeRuleSetId = device->lastRuleSetId;
        } else {
            config.activeRuleSetId = config.ruleSets.front().id;
        }
    }
    for (auto& device : config.devices) {
        if (config.FindRuleSet(device.lastRuleSetId) == nullptr) {
            device.lastRuleSetId = config.activeRuleSetId;
        }
    }
    return true;
}

std::wstring ReadStoredLanguage(const std::wstring& path) {
    AppConfig config;
    std::wstring error;
    if (!LoadConfig(path, config, error)) {
        return std::wstring();
    }
    return config.language;
}

bool SaveConfig(const std::wstring& path, const AppConfig& config, std::wstring& error) {
    error.clear();
    std::wstring appDir = GetAppDirectory();
    std::wstringstream out;
    out << L"# SyncStick Portable\r\n";
    out << L"# 规则集保存在 U 盘上；U 盘路径相对当前程序目录时，换盘符也不会失效。\r\n";
    out << L"# Rule sets are stored on the USB drive; USB paths stay relative to this folder "
           L"when possible.\r\n";
    out << L"language=" << config.language << L"\r\n";
    out << L"activeRuleset=" << config.activeRuleSetId << L"\r\n\r\n";
    for (const auto& device : config.devices) {
        out << L"[device." << device.id << L"]\r\n";
        out << L"name=" << device.name << L"\r\n";
        out << L"lastRule=" << device.lastRuleSetId << L"\r\n\r\n";
    }
    for (const auto& ruleSet : config.ruleSets) {
        out << L"[ruleset." << ruleSet.id << L"]\r\n";
        out << L"name=" << ruleSet.name << L"\r\n\r\n";
        int index = 1;
        for (const auto& rule : ruleSet.rules) {
            out << L"[ruleset." << ruleSet.id << L"." << index++ << L"]\r\n";
            out << L"name=" << rule.name << L"\r\n";
            out << L"pc=" << NormalizePath(rule.pcPath) << L"\r\n";
            out << L"usb=" << MakePortablePath(rule.usbPath, appDir) << L"\r\n";
            out << L"kind=" << (rule.kind == L"file" ? L"file" : L"folder") << L"\r\n";
            out << L"mode=" << (rule.mode == L"pc2usb" || rule.mode == L"usb2pc" ? rule.mode : L"both")
                << L"\r\n";
            out << L"enabled=" << (rule.enabled ? 1 : 0) << L"\r\n\r\n";
        }
    }
    return WriteTextFileUtf8Atomic(path, out.str(), error);
}

namespace {

struct CopyOperation {
    bool pcToUsb = true;
    std::wstring source;
    std::wstring destination;
    std::wstring relative;
    uint64_t size = 0;
};

struct PlanResult {
    std::vector<CopyOperation> operations;
    int unchanged = 0;
    int unmatched = 0;
    int conflicts = 0;
    std::vector<std::wstring> notes;
};

enum class PairDecision {
    Unchanged,
    PcToUsb,
    UsbToPc,
    Conflict,
};

std::wstring DisplayRelative(const std::wstring& relative) {
    if (relative.empty() || relative == L".") {
        return T(L"（根目录）");
    }
    return relative;
}

bool SameFileContent(const FileInfo& left, const FileInfo& right) {
    if (left.size != right.size) {
        return false;
    }
    LONGLONG leftTicks = FileTimeTicks(left.modified);
    LONGLONG rightTicks = FileTimeTicks(right.modified);
    if (leftTicks < rightTicks) {
        return rightTicks - leftTicks <= kFileTimeTolerance;
    }
    return leftTicks - rightTicks <= kFileTimeTolerance;
}

PairDecision DecidePair(const FileInfo& pc, const FileInfo& usb, std::wstring& reason) {
    reason.clear();
    if (SameFileContent(pc, usb)) {
        return PairDecision::Unchanged;
    }
    LONGLONG pcTicks = FileTimeTicks(pc.modified);
    LONGLONG usbTicks = FileTimeTicks(usb.modified);
    if (pc.size == usb.size && pcTicks == usbTicks) {
        reason = T(L"两边大小相同但修改时间完全相同，内容可能不同");
        return PairDecision::Conflict;
    }
    if (pcTicks > usbTicks + kFileTimeTolerance) {
        reason = T(L"电脑上的版本更新");
        return PairDecision::PcToUsb;
    }
    if (usbTicks > pcTicks + kFileTimeTolerance) {
        reason = T(L"U 盘上的版本更新");
        return PairDecision::UsbToPc;
    }
    reason = T(L"两边修改时间相差不到 3 秒，无法安全判断哪边更新");
    return PairDecision::Conflict;
}

void AddOperation(PlanResult& plan, bool pcToUsb, const std::wstring& source,
                  const std::wstring& destination, const std::wstring& relative,
                  uint64_t size) {
    CopyOperation operation;
    operation.pcToUsb = pcToUsb;
    operation.source = source;
    operation.destination = destination;
    operation.relative = relative;
    operation.size = size;
    plan.operations.push_back(std::move(operation));
}

std::wstring ConflictText(const std::wstring& relative, const FileInfo& pc, const FileInfo& usb,
                          const std::wstring& reason) {
    std::wstring text = TF(L"冲突：%s — %s", DisplayRelative(relative), reason);
    text += TF(L"（电脑 %s，U 盘 %s）", FileTimeText(pc.modified), FileTimeText(usb.modified));
    return text;
}

bool PlanFileRule(const Rule& rule, PlanResult& plan, std::wstring& error) {
    FileInfo pc;
    FileInfo usb;
    bool pcExists = GetFileInfo(rule.pcPath, pc);
    bool usbExists = GetFileInfo(rule.usbPath, usb);
    std::wstring relative = GetFileName(rule.kind == L"file" ? rule.pcPath : rule.usbPath);
    if (relative.empty()) {
        relative = rule.name;
    } else if (rule.kind == L"file" && pcExists) {
        relative = GetFileName(rule.pcPath);
    }

    if (!pcExists && !usbExists) {
        error = T(L"两边的文件都不存在");
        return false;
    }
    if (rule.mode == L"pc2usb" && !pcExists) {
        error = T(L"电脑上的文件不存在");
        return false;
    }
    if (rule.mode == L"usb2pc" && !usbExists) {
        error = T(L"U 盘上的文件不存在");
        return false;
    }

    if (rule.mode == L"both") {
        if (pcExists && !usbExists) {
            AddOperation(plan, true, rule.pcPath, rule.usbPath, relative, pc.size);
        } else if (!pcExists && usbExists) {
            AddOperation(plan, false, rule.usbPath, rule.pcPath, relative, usb.size);
        } else {
            std::wstring reason;
            PairDecision decision = DecidePair(pc, usb, reason);
            if (decision == PairDecision::PcToUsb) {
                AddOperation(plan, true, rule.pcPath, rule.usbPath, relative, pc.size);
            } else if (decision == PairDecision::UsbToPc) {
                AddOperation(plan, false, rule.usbPath, rule.pcPath, relative, usb.size);
            } else if (decision == PairDecision::Conflict) {
                ++plan.conflicts;
                plan.notes.push_back(ConflictText(relative, pc, usb, reason));
            } else {
                ++plan.unchanged;
            }
        }
        return true;
    }

    const bool toUsb = rule.mode == L"pc2usb";
    const FileInfo& sourceInfo = toUsb ? pc : usb;
    const FileInfo& targetInfo = toUsb ? usb : pc;
    const std::wstring& sourcePath = toUsb ? rule.pcPath : rule.usbPath;
    const std::wstring& targetPath = toUsb ? rule.usbPath : rule.pcPath;
    if (!targetInfo.exists) {
        AddOperation(plan, toUsb, sourcePath, targetPath, relative, sourceInfo.size);

    } else if (SameFileContent(sourceInfo, targetInfo)) {
        ++plan.unchanged;
    } else {
        // A one-way rule is an explicit instruction: the selected direction is
        // authoritative. No deletion is ever performed.
        AddOperation(plan, toUsb, sourcePath, targetPath, relative, sourceInfo.size);
    }
    return true;
}

bool PlanFolderRule(const Rule& rule, PlanResult& plan, std::wstring& error) {
    std::map<std::wstring, FileInfo, CaseInsensitiveLess> pcFiles;
    std::map<std::wstring, FileInfo, CaseInsensitiveLess> usbFiles;
    if (!EnumerateFolder(rule.pcPath, GetAppDirectory(), pcFiles)) {
        error = TF(L"无法读取电脑文件夹：%s", FormatWin32Error(GetLastError()));
        return false;
    }
    if (!EnumerateFolder(rule.usbPath, GetAppDirectory(), usbFiles)) {
        error = TF(L"无法读取 U 盘文件夹：%s", FormatWin32Error(GetLastError()));
        return false;
    }

    std::set<std::wstring, CaseInsensitiveLess> keys;
    for (const auto& pair : pcFiles) {
        keys.insert(pair.first);
    }
    for (const auto& pair : usbFiles) {
        keys.insert(pair.first);
    }

    for (const auto& relative : keys) {
        auto pcIterator = pcFiles.find(relative);
        auto usbIterator = usbFiles.find(relative);
        bool pcExists = pcIterator != pcFiles.end();
        bool usbExists = usbIterator != usbFiles.end();
        std::wstring pcFull = JoinPath(rule.pcPath, relative);
        std::wstring usbFull = JoinPath(rule.usbPath, relative);

        if (rule.mode == L"both") {
            if (pcExists && !usbExists) {
                AddOperation(plan, true, pcFull, usbFull, relative, pcIterator->second.size);
            } else if (!pcExists && usbExists) {
                AddOperation(plan, false, usbFull, pcFull, relative, usbIterator->second.size);
            } else {
                std::wstring reason;
                PairDecision decision = DecidePair(pcIterator->second, usbIterator->second, reason);
                if (decision == PairDecision::PcToUsb) {
                    AddOperation(plan, true, pcFull, usbFull, relative, pcIterator->second.size);
                } else if (decision == PairDecision::UsbToPc) {
                    AddOperation(plan, false, usbFull, pcFull, relative, usbIterator->second.size);
                } else if (decision == PairDecision::Conflict) {
                    ++plan.conflicts;
                    plan.notes.push_back(
                        ConflictText(relative, pcIterator->second, usbIterator->second, reason));
                } else {
                    ++plan.unchanged;
                }
            }
            continue;
        }

        const bool toUsb = rule.mode == L"pc2usb";
        if (toUsb) {
            if (!pcExists) {
                ++plan.unmatched;
                continue;
            }
            if (!usbExists) {
                AddOperation(plan, true, pcFull, usbFull, relative, pcIterator->second.size);
                continue;
            }
            if (SameFileContent(pcIterator->second, usbIterator->second)) {
                ++plan.unchanged;
            } else {
                AddOperation(plan, true, pcFull, usbFull, relative, pcIterator->second.size);
            }
        } else {
            if (!usbExists) {
                ++plan.unmatched;
                continue;
            }
            if (!pcExists) {
                AddOperation(plan, false, usbFull, pcFull, relative, usbIterator->second.size);
                continue;
            }
            if (SameFileContent(usbIterator->second, pcIterator->second)) {
                ++plan.unchanged;
            } else {
                AddOperation(plan, false, usbFull, pcFull, relative, usbIterator->second.size);
            }
        }
    }
    return true;
}

bool ValidateRule(const Rule& rule, std::wstring& error) {
    error.clear();
    if (!rule.enabled) {
        return true;
    }
    if (rule.name.empty()) {
        error = T(L"规则没有名称");
        return false;
    }
    if (rule.pcPath.empty() || rule.usbPath.empty()) {
        error = T(L"电脑路径或 U 盘路径为空");
        return false;
    }
    if (IsSamePath(rule.pcPath, rule.usbPath)) {
        error = T(L"电脑路径和 U 盘路径相同");
        return false;
    }

    const bool isFile = rule.kind == L"file";
    FileInfo pcInfo;
    FileInfo usbInfo;
    bool pcExists = GetFileInfo(rule.pcPath, pcInfo);
    bool usbExists = GetFileInfo(rule.usbPath, usbInfo);
    if (pcExists && (isFile ? pcInfo.isDirectory : !pcInfo.isDirectory)) {
        error = isFile ? T(L"电脑路径不是文件") : T(L"电脑路径不是文件夹");
        return false;
    }
    if (usbExists && (isFile ? usbInfo.isDirectory : !usbInfo.isDirectory)) {
        error = isFile ? T(L"U 盘路径不是文件") : T(L"U 盘路径不是文件夹");
        return false;
    }
    if (!isFile && pcExists && usbExists && (IsSubPath(rule.pcPath, rule.usbPath) ||
                                             IsSubPath(rule.usbPath, rule.pcPath))) {
        error = T(L"电脑路径和 U 盘路径互相包含，无法安全同步");
        return false;
    }

    if (rule.mode == L"both") {
        if (!pcExists && !usbExists) {
            error = T(L"两边的路径都不存在");
            return false;
        }
    } else if (rule.mode == L"pc2usb") {
        if (!pcExists) {
            error = T(L"电脑上的源文件或文件夹不存在");
            return false;
        }
    } else if (rule.mode == L"usb2pc") {
        if (!usbExists) {
            error = T(L"U 盘上的源文件或文件夹不存在");
            return false;
        }
    } else {
        error = T(L"同步方向无效");
        return false;
    }
    return true;
}

bool CopyOneFile(const CopyOperation& operation, std::wstring& error) {
    error.clear();
    if (!PathExists(operation.source)) {
        error = TF(L"源文件已经不存在：%s", operation.source);
        return false;
    }
    FileInfo sourceInfo;
    if (!GetFileInfo(operation.source, sourceInfo) || sourceInfo.isDirectory) {
        error = TF(L"源文件无法读取：%s", operation.source);
        return false;
    }
    if (operation.size > 0) {
        std::filesystem::path destinationPath(operation.destination);
        std::wstring volumeRoot = destinationPath.root_path().wstring();
        if (volumeRoot.empty()) {
            error = TF(L"无法判断目标磁盘：%s", operation.destination);
            return false;
        }
        ULARGE_INTEGER freeBytes{};
        if (GetDiskFreeSpaceExW(volumeRoot.c_str(), nullptr, nullptr, &freeBytes)) {
            if (freeBytes.QuadPart < operation.size + kCopyHeadroom) {
                error = TF(L"目标磁盘剩余空间不足（需要 %s，可用 %s）",
                           FormatBytes(operation.size), FormatBytes(freeBytes.QuadPart));
                return false;
            }
        }
    }

    std::wstring parentError;
    if (!EnsureParentDirectory(operation.destination, parentError)) {
        error = parentError;
        return false;
    }

    std::wstring temp = operation.destination + L".syncstick-tmp";
    DeleteFileW(LongPath(temp).c_str());
    if (!CopyFileW(LongPath(operation.source).c_str(), LongPath(temp).c_str(), FALSE)) {
        error = TF(L"复制失败：%s", FormatWin32Error(GetLastError()));
        DeleteFileW(LongPath(temp).c_str());
        return false;
    }

    WIN32_FILE_ATTRIBUTE_DATA sourceData{};
    if (GetFileAttributesExW(LongPath(operation.source).c_str(), GetFileExInfoStandard,
                             &sourceData)) {
        HANDLE attributes = CreateFileW(LongPath(temp).c_str(), FILE_WRITE_ATTRIBUTES,
                                        FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                                        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (attributes != INVALID_HANDLE_VALUE) {
            SetFileTime(attributes, nullptr, nullptr, &sourceData.ftLastWriteTime);
            CloseHandle(attributes);
        }
        DWORD targetAttributes = sourceData.dwFileAttributes &
                                 ~(FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_READONLY);
        if (targetAttributes != 0) {
            SetFileAttributesW(LongPath(temp).c_str(), targetAttributes);
        }
    }

    DWORD existingAttributes = GetFileAttributesW(LongPath(operation.destination).c_str());
    if (existingAttributes != INVALID_FILE_ATTRIBUTES &&
        (existingAttributes & FILE_ATTRIBUTE_READONLY) != 0) {
        SetFileAttributesW(LongPath(operation.destination).c_str(),
                           existingAttributes & ~FILE_ATTRIBUTE_READONLY);
    }
    if (!MoveFileExW(LongPath(temp).c_str(), LongPath(operation.destination).c_str(),
                     MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        error = TF(L"替换目标文件失败：%s", FormatWin32Error(GetLastError()));
        DeleteFileW(LongPath(temp).c_str());
        return false;
    }
    return true;
}

void SetProgressPhase(SyncProgress* progress, const std::wstring& phase) {
    if (progress == nullptr) {
        return;
    }
    std::lock_guard<std::mutex> lock(progress->mutex);
    progress->phase = phase;
}

void SetProgressFile(SyncProgress* progress, const std::wstring& relative) {
    if (progress == nullptr) {
        return;
    }
    std::lock_guard<std::mutex> lock(progress->mutex);
    progress->currentFile = relative;
}

void FinishProgress(SyncProgress* progress, const std::wstring& message) {
    if (progress == nullptr) {
        return;
    }
    {
        std::lock_guard<std::mutex> lock(progress->mutex);
        progress->lastMessage = message;
        progress->phase = message;
        progress->currentFile.clear();
    }
    progress->running = false;
}

} // namespace

bool SyncDevice(const DeviceRules& device, const std::wstring& appDir,
                const std::wstring& logPath, SyncProgress* progress, SyncStats& stats) {
    (void)appDir;
    stats = SyncStats{};
    if (progress != nullptr) {
        progress->cancel = false;
        progress->running = true;
        progress->filesDone = 0;
        progress->totalFiles = 0;
        progress->bytesDone = 0;
        progress->totalBytes = 0;
    }

    LogWriter log;
    if (!logPath.empty()) {
        log.Open(logPath);
    }
    log.Line(TF(L"开始同步设备：%s（%s）", device.name, device.id));

    auto addMessage = [&](const std::wstring& message) {
        stats.messages.push_back(message);
        log.Line(message);
    };
    auto addError = [&](const std::wstring& message) {
        ++stats.errors;
        addMessage(message);
    };

    if (progress != nullptr) {
        SetProgressPhase(progress, T(L"正在检查规则"));
    }

    struct RulePlan {
        const Rule* rule = nullptr;
        PlanResult plan;
    };
    std::vector<RulePlan> plans;
    for (const auto& rule : device.rules) {
        if (!rule.enabled) {
            continue;
        }
        std::wstring error;
        if (!ValidateRule(rule, error)) {
            addError(TF(L"规则「%s」：%s", rule.name, error));
            continue;
        }
        PlanResult plan;
        if (rule.kind == L"file") {
            if (!PlanFileRule(rule, plan, error)) {
                addError(TF(L"规则「%s」：%s", rule.name, error));
                continue;
            }
        } else {
            if (!PlanFolderRule(rule, plan, error)) {
                addError(TF(L"规则「%s」：%s", rule.name, error));
                continue;
            }
        }
        stats.conflicts += plan.conflicts;
        stats.skipped += plan.unchanged + plan.unmatched;
        for (const auto& note : plan.notes) {
            addMessage(TF(L"规则「%s」%s", rule.name, note));
        }
        plans.push_back(RulePlan{&rule, std::move(plan)});
    }

    if (progress != nullptr && progress->cancel) {
        stats.cancelled = true;
        stats.summary = T(L"同步已取消");
        FinishProgress(progress, stats.summary);
        log.Line(stats.summary);
        return false;
    }

    uint64_t totalBytes = 0;
    int totalOperations = 0;
    for (const auto& rulePlan : plans) {
        totalOperations += static_cast<int>(rulePlan.plan.operations.size());
        for (const auto& operation : rulePlan.plan.operations) {
            totalBytes += operation.size;
        }
    }
    stats.total = totalOperations;
    if (progress != nullptr) {
        progress->totalFiles = totalOperations;
        progress->totalBytes = totalBytes;
        SetProgressPhase(progress,
                         totalOperations == 0 ? T(L"正在检查文件") : T(L"准备写入"));
    }

    if (totalOperations == 0) {
        stats.summary = T(L"检查完成，没有需要同步的文件");
        FinishProgress(progress, stats.summary);
        log.Line(stats.summary);
        return stats.errors == 0;
    }

    int filesDone = 0;
    for (const auto& rulePlan : plans) {
        for (const auto& operation : rulePlan.plan.operations) {
            if (progress != nullptr && progress->cancel) {
                stats.cancelled = true;
                break;
            }
            if (progress != nullptr) {
                SetProgressPhase(progress, operation.pcToUsb ? T(L"正在写入 U 盘")
                                                              : T(L"正在写回电脑"));
                SetProgressFile(progress, operation.relative);
            }
            log.Line(operation.pcToUsb ? TF(L"复制到 U 盘：%s", operation.relative)
                                       : TF(L"复制到电脑：%s", operation.relative));
            std::wstring error;
            if (CopyOneFile(operation, error)) {
                ++stats.copied;
                stats.bytesCopied += operation.size;
                if (progress != nullptr) {
                    progress->bytesDone = stats.bytesCopied;
                }
            } else {
                addError(TF(L"复制失败：%s — %s", DisplayRelative(operation.relative), error));
            }
            ++filesDone;
            if (progress != nullptr) {
                progress->filesDone = filesDone;
            }
        }
        if (stats.cancelled) {
            break;
        }
    }

    stats.summary = BuildSummary(stats);
    FinishProgress(progress, stats.summary);
    log.Line(stats.summary);
    return !stats.cancelled && stats.errors == 0;
}

std::wstring BuildSummary(const SyncStats& stats) {
    if (stats.cancelled) {
        return TF(L"同步已取消，已写入 %s 个文件", stats.copied);
    }
    if (stats.errors > 0) {
        return TF(L"同步结束，写入 %s 个文件，%s 个错误", stats.copied, stats.errors);
    }
    std::wstring summary;
    if (stats.copied == 0) {
        summary = T(L"检查完成，没有需要同步的文件");
    } else {
        summary = TF(L"同步完成，写入 %s 个文件", stats.copied);
        if (stats.skipped > 0) {
            summary += TF(L"，跳过 %s 个未变化文件", stats.skipped);
        }
    }
    if (stats.conflicts > 0) {
        summary += TF(L"，保留 %s 处冲突", stats.conflicts);
    }
    return summary;
}

} // namespace ss
