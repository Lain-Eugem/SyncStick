#define _WIN32_WINNT 0x0A00
#include "core.h"

#include <commctrl.h>
#include <shellapi.h>
#include <shobjidl.h>
#include <windowsx.h>

#include <algorithm>
#include <atomic>
#include <fstream>
#include <memory>
#include <mutex>
#include <sstream>
#include <thread>
#include <vector>

namespace {

constexpr const wchar_t* kMainClass = L"SyncStickPortable.Main";
constexpr const wchar_t* kRuleClass = L"SyncStickPortable.Rule";
constexpr const wchar_t* kAppTitle = L"SyncStick Portable";

constexpr UINT kTimerProgress = 1;
constexpr UINT kTimerAutoSync = 2;
constexpr UINT kMsgSyncDone = WM_APP + 1;

constexpr COLORREF kPaper = RGB(244, 240, 232);
constexpr COLORREF kCard = RGB(253, 251, 246);
constexpr COLORREF kInk = RGB(30, 36, 31);
constexpr COLORREF kInkSoft = RGB(94, 102, 94);
constexpr COLORREF kInkFaint = RGB(122, 128, 117);
constexpr COLORREF kAccent = RGB(213, 84, 46);
constexpr COLORREF kAccentDark = RGB(178, 61, 30);
constexpr COLORREF kAccentSoft = RGB(251, 232, 224);
constexpr COLORREF kBorder = RGB(229, 223, 212);
constexpr COLORREF kGreen = RGB(47, 115, 85);
constexpr COLORREF kGreenSoft = RGB(226, 239, 232);
constexpr COLORREF kDark = RGB(27, 33, 28);
constexpr COLORREF kDarkRaised = RGB(38, 47, 39);
constexpr COLORREF kOnDark = RGB(245, 242, 233);
constexpr COLORREF kOnDarkFaint = RGB(166, 178, 164);
constexpr COLORREF kRed = RGB(166, 55, 41);
constexpr COLORREF kRedSoft = RGB(250, 234, 230);

int g_dpi = 96;

int S(int value) {
    return MulDiv(value, g_dpi, 96);
}

std::wstring GetWindowTextString(HWND hwnd) {
    int length = GetWindowTextLengthW(hwnd);
    if (length <= 0) {
        return {};
    }
    std::wstring value(static_cast<size_t>(length) + 1, L'\0');
    GetWindowTextW(hwnd, value.data(), length + 1);
    value.resize(static_cast<size_t>(length));
    return value;
}

std::wstring TrimText(const std::wstring& value) {
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

std::wstring Ellipsize(HDC dc, const std::wstring& text, int maxWidth) {
    if (maxWidth <= 0) {
        return L"";
    }
    SIZE size{};
    if (GetTextExtentPoint32W(dc, text.c_str(), static_cast<int>(text.size()), &size) &&
        size.cx <= maxWidth) {
        return text;
    }
    const std::wstring dots = L"…";
    SIZE dotSize{};
    GetTextExtentPoint32W(dc, dots.c_str(), 1, &dotSize);
    std::wstring result;
    for (wchar_t ch : text) {
        std::wstring candidate = result + ch;
        GetTextExtentPoint32W(dc, candidate.c_str(), static_cast<int>(candidate.size()), &size);
        if (size.cx + dotSize.cx > maxWidth) {
            break;
        }
        result.push_back(ch);
    }
    return result + dots;
}

int MeasureTextWidth(HDC dc, const std::wstring& text, HFONT font) {
    HGDIOBJ oldFont = SelectObject(dc, font);
    SIZE size{};
    GetTextExtentPoint32W(dc, text.c_str(), static_cast<int>(text.size()), &size);
    SelectObject(dc, oldFont);
    return size.cx;
}

void DrawTextLine(HDC dc, const std::wstring& text, RECT rect, HFONT font, COLORREF color,
                  UINT format = DT_LEFT | DT_SINGLELINE | DT_VCENTER) {
    SetBkMode(dc, TRANSPARENT);
    if (font != nullptr) {
        HGDIOBJ oldFont = SelectObject(dc, font);
        SetTextColor(dc, color);
        DrawTextW(dc, text.c_str(), static_cast<int>(text.size()), &rect, format);
        SelectObject(dc, oldFont);
    } else {
        SetTextColor(dc, color);
        DrawTextW(dc, text.c_str(), static_cast<int>(text.size()), &rect, format);
    }
}

void FillRectColor(HDC dc, const RECT& rect, COLORREF color) {
    HBRUSH brush = CreateSolidBrush(color);
    FillRect(dc, &rect, brush);
    DeleteObject(brush);
}

void RoundFill(HDC dc, const RECT& rect, int radius, COLORREF fill) {
    HBRUSH brush = CreateSolidBrush(fill);
    HPEN pen = CreatePen(PS_SOLID, 1, fill);
    HGDIOBJ oldBrush = SelectObject(dc, brush);
    HGDIOBJ oldPen = SelectObject(dc, pen);
    RoundRect(dc, rect.left, rect.top, rect.right, rect.bottom, radius, radius);
    SelectObject(dc, oldPen);
    SelectObject(dc, oldBrush);
    DeleteObject(pen);
    DeleteObject(brush);
}

void RoundCard(HDC dc, const RECT& rect, int radius, COLORREF fill, COLORREF border,
               int borderWidth = 1) {
    HBRUSH brush = CreateSolidBrush(fill);
    HPEN pen = CreatePen(PS_SOLID, borderWidth, border);
    HGDIOBJ oldBrush = SelectObject(dc, brush);
    HGDIOBJ oldPen = SelectObject(dc, pen);
    RoundRect(dc, rect.left, rect.top, rect.right, rect.bottom, radius, radius);
    SelectObject(dc, oldPen);
    SelectObject(dc, oldBrush);
    DeleteObject(pen);
    DeleteObject(brush);
}

void DrawCircle(HDC dc, int centerX, int centerY, int radius, COLORREF fill, COLORREF border,
                int borderWidth = 0) {
    HBRUSH brush = CreateSolidBrush(fill);
    HPEN pen = borderWidth > 0 ? CreatePen(PS_SOLID, borderWidth, border)
                               : (HPEN)GetStockObject(NULL_PEN);
    HGDIOBJ oldBrush = SelectObject(dc, brush);
    HGDIOBJ oldPen = SelectObject(dc, pen);
    Ellipse(dc, centerX - radius, centerY - radius, centerX + radius, centerY + radius);
    SelectObject(dc, oldPen);
    SelectObject(dc, oldBrush);
    DeleteObject(pen);
    DeleteObject(brush);
}

void DrawLine(HDC dc, int x1, int y1, int x2, int y2, COLORREF color, int width = 1) {
    HPEN pen = CreatePen(PS_SOLID, width, color);
    HGDIOBJ oldPen = SelectObject(dc, pen);
    MoveToEx(dc, x1, y1, nullptr);
    LineTo(dc, x2, y2);
    SelectObject(dc, oldPen);
    DeleteObject(pen);
}

void DrawPill(HDC dc, const RECT& rect, const std::wstring& text, HFONT font, COLORREF fill,
              COLORREF color) {
    RoundFill(dc, rect, (rect.bottom - rect.top), fill);
    RECT textRect = rect;
    textRect.left += S(8);
    textRect.right -= S(8);
    DrawTextLine(dc, text, textRect, font, color, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
}

struct Fonts {
    HFONT h1 = nullptr;
    HFONT h2 = nullptr;
    HFONT bodyBold = nullptr;
    HFONT body = nullptr;
    HFONT small = nullptr;
    HFONT tiny = nullptr;
    HFONT mono = nullptr;
    HFONT monoSmall = nullptr;
    HFONT button = nullptr;

    void Create(int dpi) {
        Destroy();
        h1 = Make(L"Microsoft YaHei UI", 26, FW_BOLD, dpi);
        h2 = Make(L"Microsoft YaHei UI", 17, FW_SEMIBOLD, dpi);
        bodyBold = Make(L"Microsoft YaHei UI", 10, FW_SEMIBOLD, dpi);
        body = Make(L"Microsoft YaHei UI", 10, FW_NORMAL, dpi);
        small = Make(L"Microsoft YaHei UI", 9, FW_NORMAL, dpi);
        tiny = Make(L"Microsoft YaHei UI", 8, FW_NORMAL, dpi);
        mono = Make(L"Consolas", 9, FW_NORMAL, dpi);
        monoSmall = Make(L"Consolas", 8, FW_NORMAL, dpi);
        button = Make(L"Microsoft YaHei UI", 10, FW_SEMIBOLD, dpi);
    }

    void Destroy() {
        for (HFONT* font : {&h1, &h2, &bodyBold, &body, &small, &tiny, &mono, &monoSmall,
                            &button}) {
            if (*font != nullptr) {
                DeleteObject(*font);
                *font = nullptr;
            }
        }
    }

private:
    static HFONT Make(const wchar_t* face, int points, int weight, int dpi) {
        return CreateFontW(-MulDiv(points, dpi, 72), 0, 0, 0, weight, FALSE, FALSE, FALSE,
                           DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                           CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE, face);
    }
};

struct HitTarget {
    RECT rect{};
    int kind = 0;
    int index = -1;
};

enum HitKind {
    HitNone = 0,
    HitSync,
    HitAdd,
    HitLog,
    HitCancel,
    HitRuleOpen,
    HitRuleToggle,
    HitRuleEdit,
    HitRuleDelete,
    HitEmptyAdd,
};

bool PointIn(const RECT& rect, POINT point) {
    return point.x >= rect.left && point.x < rect.right && point.y >= rect.top &&
           point.y < rect.bottom;
}

constexpr int kIdName = 2001;
constexpr int kIdPc = 2002;
constexpr int kIdKind = 2003;
constexpr int kIdMode = 2004;
constexpr int kIdUsb = 2005;
constexpr int kIdEnabled = 2006;
constexpr int kIdBrowse = 2007;
constexpr int kIdDefaultUsb = 2008;
constexpr int kIdBrowseUsb = 2009;
constexpr wchar_t kDesktopMutex[] = L"Local\\SyncStick.SingleInstance";

struct RuleDialogState {
    HWND window = nullptr;
    HWND name = nullptr;
    HWND pc = nullptr;
    HWND kind = nullptr;
    HWND mode = nullptr;
    HWND usb = nullptr;
    HWND enabled = nullptr;
    HWND titleLabel = nullptr;
    HWND parent = nullptr;
    Fonts fonts;
    HBRUSH paperBrush = nullptr;
    HBRUSH cardBrush = nullptr;
    std::wstring appDir;
    std::wstring deviceName;
    std::wstring title;
    ss::Rule rule;
    bool accepted = false;

    ~RuleDialogState() {
        fonts.Destroy();
        if (paperBrush != nullptr) {
            DeleteObject(paperBrush);
        }
        if (cardBrush != nullptr) {
            DeleteObject(cardBrush);
        }
    }
};

std::wstring DisplayPathForDialog(const std::wstring& usbPath, const std::wstring& appDir) {
    return ss::MakePortablePath(usbPath, appDir);
}

bool IsDesktopSyncStickRunning() {
    HANDLE handle = OpenMutexW(SYNCHRONIZE, FALSE, kDesktopMutex);
    if (handle == nullptr) {
        return false;
    }
    CloseHandle(handle);
    return true;
}

bool BrowseForPath(HWND parent, bool folder, std::wstring& result,
                   const wchar_t* title = nullptr) {
    result.clear();
    IFileOpenDialog* dialog = nullptr;
    if (FAILED(CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER,
                                IID_PPV_ARGS(&dialog)))) {
        return false;
    }
    DWORD options = 0;
    dialog->GetOptions(&options);
    options |= FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST;
    if (folder) {
        options |= FOS_PICKFOLDERS | FOS_FILEMUSTEXIST;
    } else {
        options |= FOS_FILEMUSTEXIST;
    }
    dialog->SetOptions(options);
    if (title != nullptr) {
        dialog->SetTitle(title);
    } else if (folder) {
        dialog->SetTitle(L"选择电脑上的文件夹");
    } else {
        dialog->SetTitle(L"选择电脑上的文件");
    }
    HRESULT shown = dialog->Show(parent);
    if (SUCCEEDED(shown)) {
        IShellItem* item = nullptr;
        if (SUCCEEDED(dialog->GetResult(&item)) && item != nullptr) {
            PWSTR path = nullptr;
            if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path != nullptr) {
                result = path;
                CoTaskMemFree(path);
            }
            item->Release();
        }
    }
    dialog->Release();
    return !result.empty();
}

void SetDialogControlFont(HWND control, HFONT font) {
    if (control != nullptr) {
        SendMessageW(control, WM_SETFONT, reinterpret_cast<WPARAM>(font), TRUE);
    }
}

int ScaleForDpi(int value, int dpi) {
    return MulDiv(value, dpi, 96);
}

void CreateRuleControls(RuleDialogState* state) {
    HWND dialog = state->window;
    int dpi = g_dpi;
    auto X = [&](int value) { return ScaleForDpi(value, dpi); };
    auto Y = [&](int value) { return ScaleForDpi(value, dpi); };
    auto W = [&](int value) { return ScaleForDpi(value, dpi); };
    auto H = [&](int value) { return ScaleForDpi(value, dpi); };

    auto label = [&](const wchar_t* text, int x, int y, int w, int h) {
        HWND control = CreateWindowExW(0, L"STATIC", text, WS_CHILD | WS_VISIBLE | SS_LEFT,
                                       X(x), Y(y), W(w), H(h), dialog, nullptr,
                                       GetModuleHandleW(nullptr), nullptr);
        SetDialogControlFont(control, state->fonts.small);
        return control;
    };
    auto edit = [&](int id, const std::wstring& text, int x, int y, int w) {
        HWND control = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", text.c_str(),
                                       WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL,
                                       X(x), Y(y), W(w), H(32), dialog,
                                       reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
                                       GetModuleHandleW(nullptr), nullptr);
        SetDialogControlFont(control, state->fonts.body);
        return control;
    };
    auto button = [&](int id, const wchar_t* text, int x, int y, int w, bool primary) {
        HWND control = CreateWindowExW(0, L"BUTTON", text,
                                       WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_PUSHBUTTON |
                                           (primary ? BS_DEFPUSHBUTTON : 0),
                                       X(x), Y(y), W(w), H(34), dialog,
                                       reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
                                       GetModuleHandleW(nullptr), nullptr);
        SetDialogControlFont(control, state->fonts.button);
        return control;
    };

    state->titleLabel = CreateWindowExW(0, L"STATIC", state->title.c_str(),
                                        WS_CHILD | WS_VISIBLE | SS_LEFT, X(28), Y(20), W(520),
                                        H(30), dialog, nullptr, GetModuleHandleW(nullptr),
                                        nullptr);
    SetDialogControlFont(state->titleLabel, state->fonts.h2);

    label(L"名称", 28, 68, 300, 20);
    state->name = edit(kIdName, state->rule.name, 28, 88, 500);

    label(L"电脑上的文件或文件夹", 28, 138, 380, 20);
    state->pc = edit(kIdPc, state->rule.pcPath, 28, 158, 430);
    button(kIdBrowse, L"浏览…", 468, 158, 84, false);

    label(L"类型", 28, 208, 100, 20);
    state->kind = CreateWindowExW(0, L"COMBOBOX", nullptr,
                                  WS_CHILD | WS_VISIBLE | WS_TABSTOP | CBS_DROPDOWNLIST |
                                      WS_VSCROLL,
                                  X(28), Y(228), W(150), H(220), dialog,
                                  reinterpret_cast<HMENU>(static_cast<INT_PTR>(kIdKind)),
                                  GetModuleHandleW(nullptr), nullptr);
    SetDialogControlFont(state->kind, state->fonts.body);
    SendMessageW(state->kind, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"文件夹"));
    SendMessageW(state->kind, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"单个文件"));
    SendMessageW(state->kind, CB_SETCURSEL,
                 state->rule.kind == L"file" ? 1 : 0, 0);

    label(L"同步方向", 198, 208, 140, 20);
    state->mode = CreateWindowExW(0, L"COMBOBOX", nullptr,
                                  WS_CHILD | WS_VISIBLE | WS_TABSTOP | CBS_DROPDOWNLIST |
                                      WS_VSCROLL,
                                  X(198), Y(228), W(200), H(220), dialog,
                                  reinterpret_cast<HMENU>(static_cast<INT_PTR>(kIdMode)),
                                  GetModuleHandleW(nullptr), nullptr);
    SetDialogControlFont(state->mode, state->fonts.body);
    SendMessageW(state->mode, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"双向同步"));
    SendMessageW(state->mode, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"电脑 → U 盘"));
    SendMessageW(state->mode, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"U 盘 → 电脑"));
    int modeIndex = state->rule.mode == L"pc2usb" ? 1 : (state->rule.mode == L"usb2pc" ? 2 : 0);
    SendMessageW(state->mode, CB_SETCURSEL, modeIndex, 0);

    label(L"U 盘内位置（可填子文件夹）", 28, 276, 340, 20);
    state->usb = edit(kIdUsb, DisplayPathForDialog(state->rule.usbPath, state->appDir), 28, 296,
                      320);
    button(kIdBrowseUsb, L"浏览…", 356, 296, 84, false);
    button(kIdDefaultUsb, L"默认位置", 448, 296, 108, false);

    HWND hint = CreateWindowExW(
        0, L"STATIC",
        L"填相对路径会自动跟随 U 盘盘符，例如：我的项目\\资料。留空时放进 SyncStickData\\这台电脑\\规则名。",
        WS_CHILD | WS_VISIBLE | SS_LEFT, X(28), Y(330), W(540), H(35), dialog, nullptr,
        GetModuleHandleW(nullptr), nullptr);
    SetDialogControlFont(hint, state->fonts.tiny);

    state->enabled = CreateWindowExW(
        0, L"BUTTON", L"启用这一项",
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_AUTOCHECKBOX, X(28), Y(372), W(180), H(26),
        dialog, reinterpret_cast<HMENU>(static_cast<INT_PTR>(kIdEnabled)),
        GetModuleHandleW(nullptr), nullptr);
    SetDialogControlFont(state->enabled, state->fonts.body);
    SendMessageW(state->enabled, BM_SETCHECK, state->rule.enabled ? BST_CHECKED : BST_UNCHECKED,
                 0);

    button(IDCANCEL, L"取消", 344, 424, 90, false);
    button(IDOK, L"保存", 444, 424, 120, true);
}

void GenerateDefaultUsbPath(RuleDialogState* state) {
    std::wstring name = TrimText(GetWindowTextString(state->name));
    if (name.empty()) {
        name = L"新同步项";
    }
    int kindIndex = static_cast<int>(SendMessageW(state->kind, CB_GETCURSEL, 0, 0));
    std::wstring kind = kindIndex == 1 ? L"file" : L"folder";
    std::wstring pcPath = TrimText(GetWindowTextString(state->pc));
    std::wstring target =
        ss::DefaultUsbRulePath(state->appDir, state->deviceName, name, kind, pcPath);
    SetWindowTextW(state->usb, DisplayPathForDialog(target, state->appDir).c_str());
}

bool SaveRuleFromDialog(RuleDialogState* state) {
    std::wstring name = TrimText(GetWindowTextString(state->name));
    std::wstring pcPath = TrimText(GetWindowTextString(state->pc));
    if (pcPath.empty()) {
        MessageBoxW(state->window, L"请选择电脑上的文件或文件夹。", L"还差一步",
                    MB_OK | MB_ICONINFORMATION);
        SetFocus(state->pc);
        return false;
    }
    pcPath = ss::NormalizePath(pcPath);
    if (!ss::PathExists(pcPath)) {
        MessageBoxW(state->window, L"这个电脑路径不存在，请重新选择。", L"路径无效",
                    MB_OK | MB_ICONWARNING);
        SetFocus(state->pc);
        return false;
    }
    int kindIndex = static_cast<int>(SendMessageW(state->kind, CB_GETCURSEL, 0, 0));
    bool isFile = kindIndex == 1;
    bool isDir = ss::IsDirectory(pcPath);
    if (isFile && isDir) {
        MessageBoxW(state->window, L"这是文件夹，请把类型改成“文件夹”。", L"类型不匹配",
                    MB_OK | MB_ICONWARNING);
        return false;
    }
    if (!isFile && !isDir) {
        MessageBoxW(state->window, L"这是文件，请把类型改成“单个文件”。", L"类型不匹配",
                    MB_OK | MB_ICONWARNING);
        return false;
    }
    if (name.empty()) {
        name = ss::GetFileName(pcPath);
        if (name.empty()) {
            name = L"同步项";
        }
    }

    int modeIndex = static_cast<int>(SendMessageW(state->mode, CB_GETCURSEL, 0, 0));
    std::wstring mode = modeIndex == 1 ? L"pc2usb" : (modeIndex == 2 ? L"usb2pc" : L"both");

    std::wstring usbText = TrimText(GetWindowTextString(state->usb));
    std::wstring usbPath;
    if (usbText.empty()) {
        usbPath = ss::DefaultUsbRulePath(state->appDir, state->deviceName, name,
                                         isFile ? L"file" : L"folder", pcPath);
    } else {
        usbPath = ss::ResolvePortablePath(usbText, state->appDir);
    }
    if (usbPath.empty()) {
        MessageBoxW(state->window, L"U 盘内位置不能为空。", L"还差一步",
                    MB_OK | MB_ICONINFORMATION);
        SetFocus(state->usb);
        return false;
    }
    if (ss::IsSamePath(pcPath, usbPath)) {
        MessageBoxW(state->window, L"电脑路径和 U 盘路径不能相同。", L"路径重复",
                    MB_OK | MB_ICONWARNING);
        return false;
    }
    if (!isFile && (ss::IsSubPath(pcPath, usbPath) || ss::IsSubPath(usbPath, pcPath))) {
        MessageBoxW(state->window, L"电脑文件夹和 U 盘文件夹不能互相包含。", L"路径重叠",
                    MB_OK | MB_ICONWARNING);
        return false;
    }
    if (isFile) {
        if (ss::PathExists(usbPath) && ss::IsDirectory(usbPath)) {
            MessageBoxW(state->window, L"文件类型的 U 盘位置必须是一个文件路径，不是文件夹。",
                        L"路径不匹配", MB_OK | MB_ICONWARNING);
            return false;
        }
    }

    state->rule.name = name;
    state->rule.pcPath = pcPath;
    state->rule.usbPath = ss::NormalizePath(usbPath);
    state->rule.kind = isFile ? L"file" : L"folder";
    state->rule.mode = mode;
    state->rule.enabled =
        SendMessageW(state->enabled, BM_GETCHECK, 0, 0) == BST_CHECKED;
    state->accepted = true;
    DestroyWindow(state->window);
    return true;
}

LRESULT CALLBACK RuleWindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
    RuleDialogState* state =
        reinterpret_cast<RuleDialogState*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));

    switch (message) {
    case WM_CREATE: {
        auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
        state = reinterpret_cast<RuleDialogState*>(create->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(state));
        state->window = hwnd;
        g_dpi = GetDpiForWindow(hwnd);
        state->fonts.Create(g_dpi);
        state->paperBrush = CreateSolidBrush(kPaper);
        state->cardBrush = CreateSolidBrush(kCard);
        CreateRuleControls(state);
        return 0;
    }
    case WM_CTLCOLORSTATIC:
        if (state != nullptr) {
            SetBkColor(reinterpret_cast<HDC>(wParam), kPaper);
            SetTextColor(reinterpret_cast<HDC>(wParam),
                         reinterpret_cast<HWND>(lParam) == state->titleLabel ? kInk : kInkSoft);
            return reinterpret_cast<LRESULT>(state->paperBrush);
        }
        break;
    case WM_ERASEBKGND:
        if (state != nullptr) {
            RECT client{};
            GetClientRect(hwnd, &client);
            FillRectColor(reinterpret_cast<HDC>(wParam), client, kPaper);
            return 1;
        }
        break;
    case WM_CTLCOLOREDIT: {
        if (state != nullptr) {
            SetBkColor(reinterpret_cast<HDC>(wParam), RGB(255, 255, 255));
            SetTextColor(reinterpret_cast<HDC>(wParam), kInk);
            static HBRUSH white = CreateSolidBrush(RGB(255, 255, 255));
            return reinterpret_cast<LRESULT>(white);
        }
        break;
    }
    case WM_CTLCOLORBTN:
        if (state != nullptr) {
            SetBkColor(reinterpret_cast<HDC>(wParam), kPaper);
            return reinterpret_cast<LRESULT>(state->paperBrush);
        }
        break;
    case WM_COMMAND: {
        if (state == nullptr) {
            break;
        }
        int id = LOWORD(wParam);
        if (id == kIdBrowse) {
            bool folder =
                static_cast<int>(SendMessageW(state->kind, CB_GETCURSEL, 0, 0)) != 1;
            std::wstring result;
            if (BrowseForPath(hwnd, folder, result)) {
                SetWindowTextW(state->pc, result.c_str());
                if (!folder) {
                    SendMessageW(state->kind, CB_SETCURSEL, 1, 0);
                }
                std::wstring name = TrimText(GetWindowTextString(state->name));
                if (name.empty() || name == L"新同步项") {
                    std::wstring derived = ss::GetFileName(result);
                    if (!derived.empty()) {
                        SetWindowTextW(state->name, derived.c_str());
                    }
                }
                GenerateDefaultUsbPath(state);
            }
            return 0;
        }
        if (id == kIdBrowseUsb) {
            std::wstring absolute;
            if (BrowseForPath(hwnd, true, absolute, L"选择 U 盘上的文件夹")) {
                std::wstring portable = ss::MakePortablePath(absolute, state->appDir);
                SetWindowTextW(state->usb,
                               DisplayPathForDialog(portable, state->appDir).c_str());
            }
            return 0;
        }
        if (id == kIdDefaultUsb) {
            GenerateDefaultUsbPath(state);
            return 0;
        }
        if (id == IDOK) {
            SaveRuleFromDialog(state);
            return 0;
        }
        if (id == IDCANCEL) {
            DestroyWindow(hwnd);
            return 0;
        }
        break;
    }
    case WM_KEYDOWN:
        if (state != nullptr) {
            if (wParam == VK_ESCAPE) {
                DestroyWindow(hwnd);
                return 0;
            }
            if (wParam == VK_RETURN) {
                SaveRuleFromDialog(state);
                return 0;
            }
        }
        break;
    case WM_CLOSE:
        DestroyWindow(hwnd);
        return 0;
    case WM_DESTROY:
        if (state != nullptr) {
            state->window = nullptr;
        }
        return 0;
    default:
        break;
    }
    return DefWindowProcW(hwnd, message, wParam, lParam);
}

bool ShowRuleDialog(HWND parent, ss::Rule& rule, bool isNew, const std::wstring& appDir,
                    const std::wstring& deviceName) {
    RuleDialogState state;
    state.parent = parent;
    state.appDir = appDir;
    state.deviceName = deviceName;
    state.rule = rule;
    state.title = isNew ? L"添加同步项" : L"编辑同步项";

    RECT parentRect{};
    GetWindowRect(parent, &parentRect);
    int width = S(600);
    int height = S(490);
    int x = parentRect.left + ((parentRect.right - parentRect.left) - width) / 2;
    int y = parentRect.top + ((parentRect.bottom - parentRect.top) - height) / 2;

    HWND dialog = CreateWindowExW(
        WS_EX_DLGMODALFRAME | WS_EX_CONTROLPARENT, kRuleClass, state.title.c_str(),
        WS_POPUP | WS_CAPTION | WS_SYSMENU | WS_VISIBLE, x, y, width, height, parent, nullptr,
        GetModuleHandleW(nullptr), &state);
    if (dialog == nullptr) {
        return false;
    }
    EnableWindow(parent, FALSE);
    ShowWindow(dialog, SW_SHOW);
    UpdateWindow(dialog);
    SetForegroundWindow(dialog);

    MSG message{};
    while (IsWindow(dialog) && GetMessageW(&message, nullptr, 0, 0) > 0) {
        if (!IsDialogMessageW(dialog, &message)) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }
    EnableWindow(parent, TRUE);
    SetActiveWindow(parent);
    SetForegroundWindow(parent);
    if (state.accepted) {
        rule = state.rule;
    }
    return state.accepted;
}

bool RegisterRuleWindowClass(HINSTANCE instance) {
    WNDCLASSEXW windowClass{};
    windowClass.cbSize = sizeof(windowClass);
    windowClass.style = CS_HREDRAW | CS_VREDRAW;
    windowClass.lpfnWndProc = RuleWindowProc;
    windowClass.hInstance = instance;
    windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    windowClass.hbrBackground = CreateSolidBrush(kPaper);
    windowClass.lpszClassName = kRuleClass;
    return RegisterClassExW(&windowClass) != 0 || GetLastError() == ERROR_CLASS_ALREADY_EXISTS;
}

// ---------------------------------------------------------------------------
// Main window
// ---------------------------------------------------------------------------

constexpr UINT kMsgSyncFinished = WM_APP + 1;

std::wstring CountText(int count, const wchar_t* suffix = L"个") {
    return std::to_wstring(count) + suffix;
}

class MainWindow {
public:
    explicit MainWindow(HINSTANCE instance) : instance_(instance) {}
    ~MainWindow() {
        if (syncActive_.load()) {
            progress_.cancel.store(true);
        }
        if (worker_.joinable()) {
            worker_.join();
        }
        fonts_.Destroy();
        if (appIcon_ != nullptr) {
            DestroyIcon(appIcon_);
            appIcon_ = nullptr;
        }
    }

    bool Create();
    int Run();

public:
    static LRESULT CALLBACK WndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam);

private:
    LRESULT HandleMessage(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam);

    void OnCreate();
    void OnPaint();
    void OnDpiChanged();
    void Paint(HDC dc, int width, int height);
    void RebuildHits(int width, int height);
    const HitTarget* FindHit(POINT point) const;

    ss::DeviceRules* CurrentDevice();
    const ss::DeviceRules* CurrentDevice() const;
    int RuleCount() const;
    int EnabledRuleCount() const;
    bool SaveConfiguration(bool showError);
    void AddRule();
    void EditRule(int index);
    void DeleteRule(int index);
    void ToggleRule(int index);
    void OpenRulePath(int index);
    void OpenLog();
    void StartSync(bool interactive);
    void ShowDesktopConflictNotice();
    void OnSyncFinished();
    void UpdateStatusTexts();

    void DrawHeader(HDC dc, int width, int contentX, int contentW);
    void DrawToolbar(HDC dc, int width, int contentX, int contentW);
    void DrawStatus(HDC dc, int width, int contentX, int contentW);
    void DrawRules(HDC dc, int width, int contentX, int contentW, int sectionY, int rulesTop);
    void DrawRuleCard(HDC dc, const ss::Rule& rule, int index, int x, int y, int width, int height);
    void DrawEmptyState(HDC dc, int x, int y, int width);
    void DrawFooter(HDC dc, int width, int contentX, int contentW);
    void DrawButton(HDC dc, const RECT& rect, const std::wstring& label, bool primary, bool ghost,
                    bool enabled = true);
    void DrawSwitch(HDC dc, const RECT& rect, bool on, bool enabled = true);
    void DrawRuleGlyph(HDC dc, const RECT& rect, const std::wstring& kind);
    void DrawAppMark(HDC dc, int x, int y, int size);

    int ContentWidth(int width) const;
    int ContentX(int width) const;
    bool IsHit(const HitTarget* hit, int kind) const;

    HINSTANCE instance_ = nullptr;
    HWND hwnd_ = nullptr;
    std::wstring appDir_;
    std::wstring configPath_;
    std::wstring logPath_;
    std::wstring configError_;
    std::wstring lastRun_;
    std::wstring lastSummary_;
    std::wstring phaseText_;
    std::wstring currentFileText_;
    ss::AppConfig config_;
    ss::DeviceIdentity identity_;
    Fonts fonts_;
    HICON appIcon_ = nullptr;
    HBRUSH backgroundBrush_ = nullptr;
    std::vector<HitTarget> hits_;
    int scroll_ = 0;
    int maxScroll_ = 0;

    ss::SyncProgress progress_;
    std::thread worker_;
    std::atomic<bool> syncActive_{false};
    std::mutex resultMutex_;
    std::unique_ptr<ss::SyncStats> pendingStats_;
    bool pendingOk_ = false;
    bool closeAfterSync_ = false;
    bool interactiveSync_ = false;
    bool desktopRunning_ = false;
    bool desktopWarningShown_ = false;
    bool hasLastSync_ = false;
    bool lastSyncOk_ = true;
    ss::SyncStats lastStats_;
};

bool RegisterMainWindowClass(HINSTANCE instance, HICON icon) {
    WNDCLASSEXW windowClass{};
    windowClass.cbSize = sizeof(windowClass);
    windowClass.style = CS_HREDRAW | CS_VREDRAW | CS_DBLCLKS;
    windowClass.lpfnWndProc = MainWindow::WndProc;
    windowClass.hInstance = instance;
    windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    windowClass.hbrBackground = CreateSolidBrush(kPaper);
    windowClass.lpszClassName = kMainClass;
    windowClass.hIcon = icon;
    windowClass.hIconSm = icon;
    return RegisterClassExW(&windowClass) != 0 || GetLastError() == ERROR_CLASS_ALREADY_EXISTS;
}

bool MainWindow::Create() {
    appDir_ = ss::GetAppDirectory();
    configPath_ = ss::GetConfigPath();
    logPath_ = ss::JoinPath(appDir_, L"SyncStickPortable.log");

    std::wstring error;
    bool loaded = ss::LoadConfig(configPath_, config_, error);
    if (!loaded && ss::PathExists(configPath_)) {
        configError_ = error.empty() ? L"配置文件读取失败" : error;
    }

    identity_ = ss::GetCurrentDevice();
    bool newDevice = config_.FindDevice(identity_.id) == nullptr;
    ss::DeviceRules& device = config_.FindOrCreateDevice(identity_.id, identity_.name);
    if (device.name.empty()) {
        device.name = identity_.name;
    }

    if (!loaded && configError_.empty()) {
        std::wstring saveError;
        ss::SaveConfig(configPath_, config_, saveError);
    } else if (newDevice) {
        std::wstring saveError;
        ss::SaveConfig(configPath_, config_, saveError);
    }

    appIcon_ = LoadIconW(instance_, MAKEINTRESOURCEW(101));
    if (appIcon_ == nullptr) {
        appIcon_ = LoadIconW(nullptr, IDI_APPLICATION);
    }
    if (!RegisterRuleWindowClass(instance_) || !RegisterMainWindowClass(instance_, appIcon_)) {
        return false;
    }

    int desiredW = S(980);
    int desiredH = S(760);
    RECT workArea{0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN)};
    HMONITOR primaryMonitor = MonitorFromPoint(POINT{0, 0}, MONITOR_DEFAULTTOPRIMARY);
    MONITORINFO monitorInfo{};
    monitorInfo.cbSize = sizeof(monitorInfo);
    if (GetMonitorInfoW(primaryMonitor, &monitorInfo)) {
        workArea = monitorInfo.rcWork;
    }
    int maxW = std::max(S(760), static_cast<int>(workArea.right - workArea.left) - S(40));
    int maxH = std::max(S(560), static_cast<int>(workArea.bottom - workArea.top) - S(40));
    desiredW = std::min(desiredW, maxW);
    desiredH = std::min(desiredH, maxH);

    RECT windowRect{0, 0, desiredW, desiredH};
    AdjustWindowRectEx(&windowRect, WS_OVERLAPPEDWINDOW, FALSE, 0);
    int windowW = windowRect.right - windowRect.left;
    int windowH = windowRect.bottom - windowRect.top;
    int x = workArea.left + ((workArea.right - workArea.left) - windowW) / 2;
    int y = workArea.top + ((workArea.bottom - workArea.top) - windowH) / 2;
    hwnd_ = CreateWindowExW(0, kMainClass, L"SyncStick Portable · U 盘双向同步",
                            WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN, x, y, windowW, windowH,
                            nullptr, nullptr, instance_, this);
    if (hwnd_ == nullptr) {
        return false;
    }
    ShowWindow(hwnd_, SW_SHOW);
    UpdateWindow(hwnd_);

    desktopRunning_ = IsDesktopSyncStickRunning();
    if (desktopRunning_) {
        if (EnabledRuleCount() > 0) {
            ShowDesktopConflictNotice();
        }
    } else {
        SetTimer(hwnd_, kTimerAutoSync, 900, nullptr);
    }

    if (!configError_.empty()) {
        std::wstring message = L"无法读取 U 盘上的规则文件：\n" + configError_ +
                               L"\n\n原文件不会被自动覆盖。";
        MessageBoxW(hwnd_, message.c_str(), L"规则文件有问题", MB_OK | MB_ICONWARNING);
    }
    return true;
}

int MainWindow::Run() {
    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    return static_cast<int>(message.wParam);
}

LRESULT CALLBACK MainWindow::WndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_NCCREATE) {
        auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
        auto* self = reinterpret_cast<MainWindow*>(create->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        self->hwnd_ = hwnd;
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }
    auto* self = reinterpret_cast<MainWindow*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (self == nullptr) {
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }
    return self->HandleMessage(hwnd, message, wParam, lParam);
}

LRESULT MainWindow::HandleMessage(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
    case WM_CREATE:
        OnCreate();
        return 0;
    case WM_SIZE:
        InvalidateRect(hwnd_, nullptr, FALSE);
        return 0;
    case WM_GETMINMAXINFO: {
        auto* info = reinterpret_cast<MINMAXINFO*>(lParam);
        info->ptMinTrackSize.x = S(820);
        info->ptMinTrackSize.y = S(600);
        return 0;
    }
    case WM_DPICHANGED:
        OnDpiChanged();
        return 0;
    case WM_ERASEBKGND:
        return 1;
    case WM_PAINT:
        OnPaint();
        return 0;
    case WM_TIMER:
        if (wParam == kTimerAutoSync) {
            KillTimer(hwnd_, kTimerAutoSync);
            if (!syncActive_.load() && EnabledRuleCount() > 0) {
                StartSync(false);
            }
            return 0;
        }
        if (wParam == kTimerProgress) {
            InvalidateRect(hwnd_, nullptr, FALSE);
            return 0;
        }
        break;
    case WM_MOUSEWHEEL:
        if (maxScroll_ > 0) {
            int delta = GET_WHEEL_DELTA_WPARAM(wParam);
            int amount = (delta / WHEEL_DELTA) * S(58);
            scroll_ -= amount;
            scroll_ = std::clamp(scroll_, 0, maxScroll_);
            InvalidateRect(hwnd_, nullptr, FALSE);
        }
        return 0;
    case WM_LBUTTONDOWN: {
        SetFocus(hwnd_);
        RECT client{};
        GetClientRect(hwnd_, &client);
        RebuildHits(client.right, client.bottom);
        POINT point{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
        const HitTarget* hit = FindHit(point);
        if (hit == nullptr) {
            return 0;
        }
        switch (hit->kind) {
        case HitSync:
            if (syncActive_.load()) {
                progress_.cancel.store(true);
                InvalidateRect(hwnd_, nullptr, FALSE);
            } else {
                StartSync(true);
            }
            break;
        case HitAdd:
        case HitEmptyAdd:
            AddRule();
            break;
        case HitLog:
            OpenLog();
            break;
        case HitRuleOpen:
            OpenRulePath(hit->index);
            break;
        case HitRuleToggle:
            ToggleRule(hit->index);
            break;
        case HitRuleEdit:
            EditRule(hit->index);
            break;
        case HitRuleDelete:
            DeleteRule(hit->index);
            break;
        default:
            break;
        }
        return 0;
    }
    case WM_SETCURSOR:
        if (LOWORD(lParam) == HTCLIENT) {
            POINT point{};
            GetCursorPos(&point);
            ScreenToClient(hwnd_, &point);
            RECT client{};
            GetClientRect(hwnd_, &client);
            RebuildHits(client.right, client.bottom);
            if (FindHit(point) != nullptr) {
                SetCursor(LoadCursorW(nullptr, IDC_HAND));
                return TRUE;
            }
        }
        break;
    case WM_KEYDOWN:
        if (wParam == VK_F5) {
            StartSync(true);
            return 0;
        }
        if ((GetKeyState(VK_CONTROL) & 0x8000) != 0) {
            if (wParam == 'N') {
                AddRule();
                return 0;
            }
            if (wParam == 'L') {
                OpenLog();
                return 0;
            }
        }
        break;
    case kMsgSyncFinished:
        OnSyncFinished();
        return 0;
    case WM_CLOSE:
        if (syncActive_.load()) {
            int answer = MessageBoxW(hwnd_, L"正在同步，要停止并退出吗？",
                                     L"SyncStick Portable", MB_YESNO | MB_ICONQUESTION);
            if (answer != IDYES) {
                return 0;
            }
            progress_.cancel.store(true);
            closeAfterSync_ = true;
            InvalidateRect(hwnd_, nullptr, FALSE);
            return 0;
        }
        DestroyWindow(hwnd_);
        return 0;
    case WM_DESTROY:
        KillTimer(hwnd_, kTimerAutoSync);
        KillTimer(hwnd_, kTimerProgress);
        if (syncActive_.load()) {
            progress_.cancel.store(true);
        }
        if (worker_.joinable()) {
            worker_.join();
        }
        PostQuitMessage(0);
        return 0;
    default:
        break;
    }
    return DefWindowProcW(hwnd, message, wParam, lParam);
}

void MainWindow::OnCreate() {
    g_dpi = GetDpiForWindow(hwnd_);
    fonts_.Create(g_dpi);
    backgroundBrush_ = CreateSolidBrush(kPaper);
    if (appIcon_ != nullptr) {
        SendMessageW(hwnd_, WM_SETICON, ICON_BIG, reinterpret_cast<LPARAM>(appIcon_));
        SendMessageW(hwnd_, WM_SETICON, ICON_SMALL, reinterpret_cast<LPARAM>(appIcon_));
    }
}

void MainWindow::OnDpiChanged() {
    UINT dpi = GetDpiForWindow(hwnd_);
    if (dpi != 0) {
        g_dpi = static_cast<int>(dpi);
    }
    fonts_.Create(g_dpi);
    InvalidateRect(hwnd_, nullptr, FALSE);
}

void MainWindow::OnPaint() {
    PAINTSTRUCT paint{};
    HDC dc = BeginPaint(hwnd_, &paint);
    RECT client{};
    GetClientRect(hwnd_, &client);
    int width = client.right - client.left;
    int height = client.bottom - client.top;
    if (width > 0 && height > 0) {
        HDC memory = CreateCompatibleDC(dc);
        HBITMAP bitmap = CreateCompatibleBitmap(dc, width, height);
        if (memory != nullptr && bitmap != nullptr) {
            HGDIOBJ oldBitmap = SelectObject(memory, bitmap);
            Paint(memory, width, height);
            BitBlt(dc, 0, 0, width, height, memory, 0, 0, SRCCOPY);
            SelectObject(memory, oldBitmap);
        }
        if (bitmap != nullptr) {
            DeleteObject(bitmap);
        }
        if (memory != nullptr) {
            DeleteDC(memory);
        }
    }
    EndPaint(hwnd_, &paint);
}

int MainWindow::ContentWidth(int width) const {
    return std::min(S(1000), std::max(S(620), width - S(64)));
}

int MainWindow::ContentX(int width) const {
    return (width - ContentWidth(width)) / 2;
}

void MainWindow::Paint(HDC dc, int width, int height) {
    RebuildHits(width, height);
    FillRectColor(dc, RECT{0, 0, width, height}, kPaper);
    int contentW = ContentWidth(width);
    int contentX = ContentX(width);
    DrawHeader(dc, width, contentX, contentW);
    DrawToolbar(dc, width, contentX, contentW);
    bool showStatus = RuleCount() > 0 || hasLastSync_;
    if (showStatus) {
        DrawStatus(dc, width, contentX, contentW);
    }
    int sectionY = S(138) + S(18) + S(44);
    if (showStatus) {
        sectionY += S(58) + S(76);
    }
    sectionY += S(34);
    int rulesTop = sectionY + S(30);
    DrawRules(dc, width, contentX, contentW, sectionY, rulesTop);
    DrawFooter(dc, width, contentX, contentW);
}

void MainWindow::RebuildHits(int width, int height) {
    hits_.clear();
    maxScroll_ = 0;
    if (width <= 0 || height <= 0) {
        return;
    }
    int contentW = ContentWidth(width);
    int contentX = ContentX(width);
    int toolbarY = S(138) + S(18);
    auto add = [&](int left, int top, int right, int bottom, int kind, int index = -1) {
        HitTarget hit;
        hit.rect = RECT{left, top, right, bottom};
        hit.kind = kind;
        hit.index = index;
        hits_.push_back(hit);
    };
    add(contentX, toolbarY, contentX + S(150), toolbarY + S(44), HitSync);
    add(contentX + S(162), toolbarY, contentX + S(304), toolbarY + S(44), HitAdd);
    add(contentX + S(316), toolbarY, contentX + S(438), toolbarY + S(44), HitLog);

    const int cardHeight = S(98);
    const int cardGap = S(10);
    bool showStatus = RuleCount() > 0 || hasLastSync_;
    int rulesTop = S(138) + S(18) + S(44) + S(34) + S(30);
    if (showStatus) {
        rulesTop += S(58) + S(76);
    }
    const ss::DeviceRules* device = CurrentDevice();
    int count = device == nullptr ? 0 : static_cast<int>(device->rules.size());
    int contentBottom = rulesTop + count * (cardHeight + cardGap);
    if (count == 0) {
        contentBottom = rulesTop + S(176);
    }
    int footerTop = height - S(38);
    maxScroll_ = std::max(0, contentBottom + S(14) - footerTop);
    scroll_ = std::clamp(scroll_, 0, maxScroll_);

    if (count == 0) {
        add(contentX, rulesTop - scroll_, contentX + contentW, rulesTop + S(164) - scroll_,
            HitEmptyAdd);
        return;
    }
    for (int index = 0; index < count; ++index) {
        int top = rulesTop + index * (cardHeight + cardGap) - scroll_;
        int bottom = top + cardHeight;
        if (bottom <= rulesTop || top >= footerTop) {
            continue;
        }
        add(contentX, top, contentX + contentW, bottom, HitRuleOpen, index);
        int toggleW = S(42);
        int toggleX = contentX + contentW - S(74);
        add(toggleX, top + S(16), toggleX + toggleW, top + S(38), HitRuleToggle, index);
        int editX = contentX + contentW - S(156);
        add(editX, top + S(13), editX + S(32), top + S(41), HitRuleEdit, index);
        int deleteX = contentX + contentW - S(118);
        add(deleteX, top + S(13), deleteX + S(32), top + S(41), HitRuleDelete, index);
    }
}

const HitTarget* MainWindow::FindHit(POINT point) const {
    for (auto it = hits_.rbegin(); it != hits_.rend(); ++it) {
        if (PointIn(it->rect, point)) {
            return &(*it);
        }
    }
    return nullptr;
}

bool MainWindow::IsHit(const HitTarget* hit, int kind) const {
    return hit != nullptr && hit->kind == kind;
}

ss::DeviceRules* MainWindow::CurrentDevice() {
    return config_.FindDevice(identity_.id);
}

const ss::DeviceRules* MainWindow::CurrentDevice() const {
    return config_.FindDevice(identity_.id);
}

int MainWindow::RuleCount() const {
    const ss::DeviceRules* device = CurrentDevice();
    return device == nullptr ? 0 : static_cast<int>(device->rules.size());
}

int MainWindow::EnabledRuleCount() const {
    const ss::DeviceRules* device = CurrentDevice();
    if (device == nullptr) {
        return 0;
    }
    int count = 0;
    for (const auto& rule : device->rules) {
        if (rule.enabled) {
            ++count;
        }
    }
    return count;
}

bool MainWindow::SaveConfiguration(bool showError) {
    std::wstring error;
    if (ss::SaveConfig(configPath_, config_, error)) {
        return true;
    }
    if (showError) {
        std::wstring message = L"规则没能写入 U 盘：\n" + error;
        MessageBoxW(hwnd_, message.c_str(), L"保存失败", MB_OK | MB_ICONWARNING);
    }
    return false;
}

void MainWindow::AddRule() {
    ss::DeviceRules* device = CurrentDevice();
    if (device == nullptr) {
        return;
    }
    ss::Rule rule;
    rule.kind = L"folder";
    rule.mode = L"both";
    rule.enabled = true;
    if (!ShowRuleDialog(hwnd_, rule, true, appDir_, identity_.name)) {
        return;
    }
    for (const auto& existing : device->rules) {
        if (ss::IsSamePath(existing.pcPath, rule.pcPath) &&
            ss::IsSamePath(existing.usbPath, rule.usbPath)) {
            MessageBoxW(hwnd_, L"这个电脑路径已经有一条相同的同步项了。", L"重复的同步项",
                        MB_OK | MB_ICONINFORMATION);
            return;
        }
    }
    device->rules.push_back(rule);
    SaveConfiguration(true);
    InvalidateRect(hwnd_, nullptr, FALSE);
}

void MainWindow::EditRule(int index) {
    ss::DeviceRules* device = CurrentDevice();
    if (device == nullptr || index < 0 || index >= static_cast<int>(device->rules.size())) {
        return;
    }
    ss::Rule rule = device->rules[static_cast<size_t>(index)];
    if (!ShowRuleDialog(hwnd_, rule, false, appDir_, identity_.name)) {
        return;
    }
    device->rules[static_cast<size_t>(index)] = rule;
    SaveConfiguration(true);
    InvalidateRect(hwnd_, nullptr, FALSE);
}

void MainWindow::DeleteRule(int index) {
    ss::DeviceRules* device = CurrentDevice();
    if (device == nullptr || index < 0 || index >= static_cast<int>(device->rules.size())) {
        return;
    }
    const ss::Rule& rule = device->rules[static_cast<size_t>(index)];
    std::wstring message = L"删除“" + rule.name + L"”这条规则吗？\nU 盘里的文件不会被删除。";
    if (MessageBoxW(hwnd_, message.c_str(), L"删除同步项",
                    MB_YESNO | MB_ICONQUESTION) != IDYES) {
        return;
    }
    device->rules.erase(device->rules.begin() + index);
    SaveConfiguration(true);
    InvalidateRect(hwnd_, nullptr, FALSE);
}

void MainWindow::ToggleRule(int index) {
    ss::DeviceRules* device = CurrentDevice();
    if (device == nullptr || index < 0 || index >= static_cast<int>(device->rules.size())) {
        return;
    }
    ss::Rule& rule = device->rules[static_cast<size_t>(index)];
    rule.enabled = !rule.enabled;
    SaveConfiguration(true);
    InvalidateRect(hwnd_, nullptr, FALSE);
}

void MainWindow::OpenRulePath(int index) {
    const ss::DeviceRules* device = CurrentDevice();
    if (device == nullptr || index < 0 || index >= static_cast<int>(device->rules.size())) {
        return;
    }
    const ss::Rule& rule = device->rules[static_cast<size_t>(index)];
    if (!ss::PathExists(rule.pcPath)) {
        MessageBoxW(hwnd_, L"这个电脑路径已经不存在了，可以编辑或删除这条规则。", L"路径不存在",
                    MB_OK | MB_ICONINFORMATION);
        return;
    }
    if (rule.kind == L"file") {
        std::wstring arguments = L"/select,\"" + rule.pcPath + L"\"";
        ShellExecuteW(hwnd_, L"open", L"explorer.exe", arguments.c_str(), nullptr, SW_SHOWNORMAL);
    } else {
        ShellExecuteW(hwnd_, L"open", rule.pcPath.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
    }
}

void MainWindow::OpenLog() {
    if (!ss::PathExists(logPath_)) {
        MessageBoxW(hwnd_, L"还没有生成日志文件。先同步一次吧。", L"暂无日志",
                    MB_OK | MB_ICONINFORMATION);
        return;
    }
    ShellExecuteW(hwnd_, L"open", logPath_.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
}

void MainWindow::ShowDesktopConflictNotice() {
    desktopWarningShown_ = true;
    MessageBoxW(
        hwnd_,
        L"检测到桌面版 SyncStick 正在运行。\n\n"
        L"两个程序同时写同一支 U 盘可能互相覆盖。便携版这次不会自动同步；"
        L"请先退出桌面版，再回到这里点“立即同步”。",
        L"检测到桌面版 SyncStick", MB_OK | MB_ICONINFORMATION);
}

void MainWindow::StartSync(bool interactive) {
    if (syncActive_.load()) {
        return;
    }
    if (IsDesktopSyncStickRunning()) {
        desktopRunning_ = true;
        if (interactive || !desktopWarningShown_) {
            ShowDesktopConflictNotice();
        }
        InvalidateRect(hwnd_, nullptr, FALSE);
        return;
    }
    desktopRunning_ = false;
    desktopWarningShown_ = false;
    ss::DeviceRules* device = CurrentDevice();
    if (device == nullptr || device->rules.empty()) {
        if (interactive) {
            int answer = MessageBoxW(hwnd_, L"还没有同步项，先添加一个文件夹或文件吧。",
                                     L"还没有同步项", MB_YESNO | MB_ICONINFORMATION);
            if (answer == IDYES) {
                AddRule();
            }
        }
        return;
    }
    bool anyEnabled = false;
    for (const auto& rule : device->rules) {
        if (rule.enabled) {
            anyEnabled = true;
            break;
        }
    }
    if (!anyEnabled) {
        if (interactive) {
            MessageBoxW(hwnd_, L"所有同步项都暂停了。先打开其中一项再同步。", L"没有启用的同步项",
                        MB_OK | MB_ICONINFORMATION);
        }
        return;
    }
    KillTimer(hwnd_, kTimerAutoSync);
    SaveConfiguration(false);
    ss::DeviceRules snapshot = *device;
    {
        std::lock_guard<std::mutex> lock(progress_.mutex);
        progress_.phase = L"正在准备";
        progress_.currentFile.clear();
        progress_.lastMessage.clear();
    }
    progress_.cancel.store(false);
    progress_.filesDone.store(0);
    progress_.totalFiles.store(0);
    progress_.bytesDone.store(0);
    progress_.totalBytes.store(0);
    progress_.running.store(true);
    syncActive_.store(true);
    interactiveSync_ = interactive;
    closeAfterSync_ = false;
    SetTimer(hwnd_, kTimerProgress, 120, nullptr);
    InvalidateRect(hwnd_, nullptr, FALSE);

    worker_ = std::thread([this, snapshot]() {
        ss::SyncStats stats;
        bool ok = ss::SyncDevice(snapshot, appDir_, logPath_, &progress_, stats);
        {
            std::lock_guard<std::mutex> lock(resultMutex_);
            pendingStats_ = std::make_unique<ss::SyncStats>(std::move(stats));
            pendingOk_ = ok;
        }
        if (IsWindow(hwnd_)) {
            PostMessageW(hwnd_, kMsgSyncFinished, 0, 0);
        }
    });
}

void MainWindow::OnSyncFinished() {
    if (worker_.joinable()) {
        worker_.join();
    }
    std::unique_ptr<ss::SyncStats> stats;
    {
        std::lock_guard<std::mutex> lock(resultMutex_);
        stats = std::move(pendingStats_);
    }
    syncActive_.store(false);
    progress_.running.store(false);
    KillTimer(hwnd_, kTimerProgress);
    bool interactive = interactiveSync_;
    interactiveSync_ = false;

    if (stats != nullptr) {
        lastStats_ = *stats;
        hasLastSync_ = true;
        lastSyncOk_ = pendingOk_;
        lastSummary_ = stats->summary;
        lastRun_ = ss::NowStamp();
        if (lastSummary_.empty()) {
            lastSummary_ = lastSyncOk_ ? L"已按规则完成一次同步" : L"同步没有完成";
        }
    }

    InvalidateRect(hwnd_, nullptr, FALSE);

    if (closeAfterSync_) {
        closeAfterSync_ = false;
        DestroyWindow(hwnd_);
        return;
    }

    if (interactive && stats != nullptr && !stats->cancelled &&
        (!pendingOk_ || stats->errors > 0)) {
        std::wstring body = stats->summary;
        if (!stats->messages.empty()) {
            body += L"\n\n";
            int shown = 0;
            for (const auto& message : stats->messages) {
                if (shown >= 6) {
                    body += L"…\n";
                    break;
                }
                body += L"· " + message + L"\n";
                ++shown;
            }
        }
        MessageBoxW(hwnd_, body.c_str(), L"同步遇到问题", MB_OK | MB_ICONWARNING);
    }
}

void MainWindow::UpdateStatusTexts() {
    std::lock_guard<std::mutex> lock(progress_.mutex);
    phaseText_ = progress_.phase;
    currentFileText_ = progress_.currentFile;
}

void MainWindow::DrawHeader(HDC dc, int width, int contentX, int contentW) {
    RECT header{0, 0, width, S(138)};
    FillRectColor(dc, header, kDark);

    for (int x = contentX + contentW - S(360); x < contentX + contentW; x += S(24)) {
        for (int y = S(16); y < S(130); y += S(24)) {
            RECT dot{x, y, x + 1, y + 1};
            FillRectColor(dc, dot, RGB(47, 56, 47));
        }
    }

    int markX = contentX + S(4);
    int markY = S(28);
    DrawAppMark(dc, markX, markY, S(48));

    int brandX = markX + S(66);
    int brandWidth = MeasureTextWidth(dc, L"SyncStick", fonts_.h1);
    DrawTextLine(dc, L"SyncStick", RECT{brandX, S(28), brandX + brandWidth + S(4), S(60)},
                 fonts_.h1, kOnDark, DT_LEFT | DT_SINGLELINE | DT_VCENTER);
    int pillX = brandX + brandWidth + S(16);
    DrawPill(dc, RECT{pillX, S(36), pillX + S(88), S(58)}, L"PORTABLE",
             fonts_.tiny, kAccent, kOnDark);
    DrawTextLine(dc, L"U 盘随身同步", RECT{brandX, S(62), brandX + S(220), S(82)},
                 fonts_.small, kOnDarkFaint);
    std::wstring machine = L"这台电脑 · " + identity_.name;
    DrawTextLine(dc, machine, RECT{brandX, S(92), brandX + S(360), S(112)}, fonts_.tiny,
                 kOnDarkFaint);
    DrawTextLine(dc, L"规则保存在这支 U 盘里", RECT{brandX, S(110), brandX + S(420), S(128)},
                 fonts_.tiny, RGB(130, 145, 130));

    int ringCenterX = contentX + contentW - S(96);
    int ringCenterY = S(61);
    int ringRadius = S(39);
    COLORREF ringColor = RGB(92, 104, 94);
    if (syncActive_.load()) {
        ringColor = kAccent;
    } else if (hasLastSync_ && (!lastSyncOk_ || lastStats_.errors > 0)) {
        ringColor = kRed;
    } else if (hasLastSync_) {
        ringColor = kGreen;
    }
    DrawCircle(dc, ringCenterX, ringCenterY, ringRadius, kDark, ringColor, S(3));
    DrawTextLine(dc, L"USB", RECT{ringCenterX - S(38), ringCenterY - S(26),
                                  ringCenterX + S(38), ringCenterY + S(6)},
                 fonts_.h2, kOnDark, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
    DrawTextLine(dc, syncActive_.load() ? L"同步中" : L"已插入",
                 RECT{ringCenterX - S(38), ringCenterY + S(4), ringCenterX + S(38),
                      ringCenterY + S(26)},
                 fonts_.tiny, kOnDarkFaint, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
    DrawTextLine(dc, L"U 盘已就绪", RECT{ringCenterX - S(70), S(110), ringCenterX + S(70),
                                          S(128)},
                 fonts_.tiny, kOnDarkFaint, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
}

void MainWindow::DrawToolbar(HDC dc, int width, int contentX, int contentW) {
    int y = S(138) + S(18);
    int height = S(44);
    RECT syncRect{contentX, y, contentX + S(150), y + height};
    DrawButton(dc, syncRect, syncActive_.load() ? L"停止同步" : L"立即同步", true, false,
               syncActive_.load() || RuleCount() > 0);
    RECT addRect{contentX + S(162), y, contentX + S(304), y + height};
    DrawButton(dc, addRect, L"添加同步项", false, false);
    RECT logRect{contentX + S(316), y, contentX + S(438), y + height};
    DrawButton(dc, logRect, L"打开日志", false, true);

    std::wstring countText = CountText(RuleCount()) + L" 个同步项";
    if (EnabledRuleCount() != RuleCount()) {
        countText += L" · " + CountText(EnabledRuleCount()) + L" 个启用";
    }
    DrawTextLine(dc, countText,
                 RECT{contentX + contentW - S(360), y, contentX + contentW, y + height},
                 fonts_.small, kInkFaint, DT_RIGHT | DT_SINGLELINE | DT_VCENTER);
}

void MainWindow::DrawStatus(HDC dc, int width, int contentX, int contentW) {
    int y = S(138) + S(18) + S(44) + S(58);
    int height = S(76);
    RECT card{contentX, y, contentX + contentW, y + height};
    RoundCard(dc, card, S(14), kCard, kBorder);

    bool active = syncActive_.load();
    bool hasProblem = hasLastSync_ && (!lastSyncOk_ || lastStats_.errors > 0);
    COLORREF dotColor = active ? kAccent : (hasProblem ? kRed : (hasLastSync_ ? kGreen : kInkFaint));
    DrawCircle(dc, contentX + S(22), y + S(26), S(5), dotColor, dotColor);

    std::wstring title;
    std::wstring subtitle;
    if (active) {
        UpdateStatusTexts();
        title = L"正在同步";
        subtitle = phaseText_.empty() ? L"正在比较两边的文件" : phaseText_;
        if (!currentFileText_.empty()) {
            subtitle = currentFileText_;
        }
    } else if (hasLastSync_) {
        if (lastStats_.cancelled) {
            title = L"已停止";
        } else if (!lastSyncOk_ || lastStats_.errors > 0) {
            title = L"同步遇到问题";
        } else {
            title = L"同步完成";
        }
        subtitle = lastSummary_;
    } else if (RuleCount() == 0) {
        title = L"还没有同步项";
        subtitle = L"先添加这台电脑上需要随身带走的文件夹或文件";
    } else {
        title = L"准备好了";
        subtitle = L"启动时会自动比较一次两边的内容";
    }
    DrawTextLine(dc, title, RECT{contentX + S(38), y + S(10), contentX + contentW - S(180),
                                 y + S(32)},
                 fonts_.bodyBold, kInk);
    DrawTextLine(dc, Ellipsize(dc, subtitle, contentW - S(230)),
                 RECT{contentX + S(38), y + S(34), contentX + contentW - S(170), y + S(56)},
                 fonts_.small, kInkSoft);

    if (active) {
        int filesDone = progress_.filesDone.load();
        int totalFiles = progress_.totalFiles.load();
        int percent = 0;
        if (totalFiles > 0) {
            percent = std::clamp(filesDone * 100 / totalFiles, 0, 100);
        } else {
            uint64_t bytesDone = progress_.bytesDone.load();
            uint64_t totalBytes = progress_.totalBytes.load();
            if (totalBytes > 0) {
                percent = static_cast<int>(std::clamp<uint64_t>(bytesDone * 100 / totalBytes, 0, 100));
            }
        }
        RECT track{contentX + S(38), y + S(58), contentX + contentW - S(90), y + S(62)};
        RoundFill(dc, track, S(2), kBorder);
        if (percent > 0) {
            RECT fill = track;
            fill.right = track.left + (track.right - track.left) * percent / 100;
            RoundFill(dc, fill, S(2), kAccent);
        }
        std::wstring percentText = std::to_wstring(percent) + L"%";
        DrawTextLine(dc, percentText, RECT{contentX + contentW - S(80), y + S(46),
                                           contentX + contentW - S(18), y + S(66)},
                     fonts_.mono, kInk, DT_RIGHT | DT_SINGLELINE | DT_VCENTER);
    } else {
        std::wstring rightText = lastRun_.empty() ? L"" : L"上次同步 " + lastRun_;
        DrawTextLine(dc, rightText,
                     RECT{contentX + contentW - S(260), y + S(14), contentX + contentW - S(18),
                          y + S(34)},
                     fonts_.tiny, kInkFaint, DT_RIGHT | DT_SINGLELINE | DT_VCENTER);
    }
}

void MainWindow::DrawRules(HDC dc, int width, int contentX, int contentW, int sectionY,
                           int rulesTop) {
    DrawTextLine(dc, L"同步项", RECT{contentX, sectionY, contentX + S(120), sectionY + S(24)},
                 fonts_.bodyBold, kInk);
    RECT badge{contentX + S(64), sectionY + S(2), contentX + S(92), sectionY + S(22)};
    DrawPill(dc, badge, std::to_wstring(RuleCount()), fonts_.tiny, kInkSoft, kCard);
    DrawTextLine(dc, L"这台电脑的规则 · 双击一次启动",
                 RECT{contentX + contentW - S(360), sectionY, contentX + contentW,
                      sectionY + S(24)},
                 fonts_.tiny, kInkFaint, DT_RIGHT | DT_SINGLELINE | DT_VCENTER);

    const ss::DeviceRules* device = CurrentDevice();
    int count = device == nullptr ? 0 : static_cast<int>(device->rules.size());
    if (count == 0) {
        DrawEmptyState(dc, contentX, rulesTop - scroll_, contentW);
        DrawTextLine(dc,
                     L"提示：插上 U 盘后双击一次，程序会自动识别这台电脑并同步这里的规则。",
                     RECT{contentX, rulesTop + S(184), contentX + contentW,
                          rulesTop + S(208)},
                     fonts_.small, kInkFaint, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
        return;
    }
    for (int index = 0; index < count; ++index) {
        int cardHeight = S(98);
        int cardGap = S(10);
        int top = rulesTop + index * (cardHeight + cardGap) - scroll_;
        if (top + cardHeight <= rulesTop || top >= width * 0 + (S(100000))) {
            continue;
        }
        DrawRuleCard(dc, device->rules[static_cast<size_t>(index)], index, contentX, top,
                     contentW, cardHeight);
    }

    if (maxScroll_ > 0) {
        int footerTop = 0;
        RECT client{};
        GetClientRect(hwnd_, &client);
        footerTop = client.bottom - S(38);
        int trackTop = rulesTop;
        int trackHeight = std::max(S(40), footerTop - trackTop - S(8));
        int thumbHeight =
            std::max(S(32), trackHeight * trackHeight / std::max(1, trackHeight + maxScroll_));
        int thumbTop = trackTop + (trackHeight - thumbHeight) * scroll_ / std::max(1, maxScroll_);
        RECT track{contentX + contentW - S(4), trackTop, contentX + contentW, trackTop + trackHeight};
        RECT thumb{track.left, thumbTop, track.right, thumbTop + thumbHeight};
        RoundFill(dc, track, S(2), kBorder);
        RoundFill(dc, thumb, S(2), RGB(197, 189, 176));
    }
}

void MainWindow::DrawRuleCard(HDC dc, const ss::Rule& rule, int index, int x, int y, int width,
                              int height) {
    bool enabled = rule.enabled;
    COLORREF cardBorder = enabled ? kBorder : RGB(226, 218, 207);
    RECT shadow{x + S(2), y + S(3), x + width + S(2), y + height + S(3)};
    RoundFill(dc, shadow, S(15), RGB(235, 229, 219));
    RECT card{x, y, x + width, y + height};
    RoundCard(dc, card, S(15), enabled ? kCard : RGB(249, 246, 240), cardBorder);

    RECT iconTile{x + S(16), y + S(16), x + S(56), y + S(56)};
    RoundFill(dc, iconTile, S(11), enabled ? kAccentSoft : RGB(241, 236, 228));
    DrawRuleGlyph(dc, iconTile, rule.kind);

    int textLeft = x + S(72);
    int textRight = x + width - S(174);
    DrawTextLine(dc, Ellipsize(dc, rule.name, textRight - textLeft),
                 RECT{textLeft, y + S(10), textRight, y + S(30)}, fonts_.bodyBold,
                 enabled ? kInk : kInkFaint);
    DrawTextLine(dc, Ellipsize(dc, rule.pcPath, textRight - textLeft),
                 RECT{textLeft, y + S(31), textRight, y + S(47)}, fonts_.small,
                 enabled ? kInkSoft : kInkFaint);
    std::wstring usbText = L"U 盘 · " + ss::MakePortablePath(rule.usbPath, appDir_);
    DrawTextLine(dc, Ellipsize(dc, usbText, textRight - textLeft),
                 RECT{textLeft, y + S(49), textRight, y + S(65)}, fonts_.tiny,
                 enabled ? kInkFaint : RGB(151, 151, 141));

    RECT modePill{x + width - S(168), y + S(68), x + width - S(74), y + S(90)};
    DrawPill(dc, modePill, ss::DirectionLabel(rule.mode), fonts_.tiny, kCard, kInkSoft);

    DrawTextLine(dc, L"改", RECT{x + width - S(156), y + S(13), x + width - S(124),
                                 y + S(41)},
                 fonts_.small, kInkFaint, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
    DrawTextLine(dc, L"删", RECT{x + width - S(118), y + S(13), x + width - S(86), y + S(41)},
                 fonts_.small, kInkFaint, DT_CENTER | DT_SINGLELINE | DT_VCENTER);

    RECT toggle{x + width - S(74), y + S(16), x + width - S(32), y + S(38)};
    DrawSwitch(dc, toggle, enabled, true);

    if (!enabled) {
        DrawTextLine(dc, L"已暂停", RECT{x + width - S(168), y + S(42), x + width - S(76),
                                         y + S(62)},
                     fonts_.tiny, kInkFaint, DT_RIGHT | DT_SINGLELINE | DT_VCENTER);
    }
}

void MainWindow::DrawEmptyState(HDC dc, int x, int y, int width) {
    RECT card{x, y, x + width, y + S(168)};
    RoundFill(dc, RECT{x + S(2), y + S(3), x + width + S(2), y + S(171)}, S(15),
              RGB(235, 229, 219));
    RoundCard(dc, card, S(15), kCard, kBorder);
    RECT iconTile{x + width / 2 - S(24), y + S(22), x + width / 2 + S(24), y + S(70)};
    RoundFill(dc, iconTile, S(12), kAccentSoft);
    DrawRuleGlyph(dc, iconTile, L"folder");
    DrawTextLine(dc, L"还没有同步项",
                 RECT{x, y + S(78), x + width, y + S(104)}, fonts_.h2, kInk,
                 DT_CENTER | DT_SINGLELINE | DT_VCENTER);
    DrawTextLine(dc, L"选一个文件夹或文件，规则会按这台电脑记住",
                 RECT{x + S(40), y + S(108), x + width - S(40), y + S(128)}, fonts_.small,
                 kInkSoft, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
    RECT button{x + width / 2 - S(80), y + S(134), x + width / 2 + S(80), y + S(166)};
    DrawButton(dc, button, L"添加同步项", true, false);
}

void MainWindow::DrawFooter(HDC dc, int width, int contentX, int contentW) {
    int y = 0;
    RECT client{};
    GetClientRect(hwnd_, &client);
    y = client.bottom - S(38);
    DrawLine(dc, contentX, y, contentX + contentW, y, kBorder);
    const wchar_t* footer = desktopRunning_
                                ? L"桌面版正在运行 · 便携版已暂停自动同步"
                                : L"本机运行 · 规则保存在 U 盘 · 双击一次启动";
    DrawTextLine(dc, footer,
                 RECT{contentX, y + S(8), contentX + contentW - S(180), y + S(30)},
                 fonts_.tiny, desktopRunning_ ? kAccent : kInkFaint);
    DrawTextLine(dc, L"SyncStick Portable 1.0.1",
                 RECT{contentX + contentW - S(200), y + S(8), contentX + contentW, y + S(30)},
                 fonts_.monoSmall, kInkFaint, DT_RIGHT | DT_SINGLELINE | DT_VCENTER);
}

void MainWindow::DrawButton(HDC dc, const RECT& rect, const std::wstring& label, bool primary,
                            bool ghost, bool enabled) {
    if (primary) {
        RoundFill(dc, rect, S(11), enabled ? kAccent : kBorder);
    } else if (!ghost) {
        RoundCard(dc, rect, S(11), kCard, kBorder);
    }
    COLORREF textColor = primary ? kOnDark : kInk;
    if (!enabled) {
        textColor = kInkFaint;
    }
    DrawTextLine(dc, label, rect, fonts_.button, textColor,
                 DT_CENTER | DT_SINGLELINE | DT_VCENTER);
}

void MainWindow::DrawSwitch(HDC dc, const RECT& rect, bool on, bool enabled) {
    int radius = (rect.bottom - rect.top) / 2;
    RoundFill(dc, rect, radius, on ? (enabled ? kAccent : kBorder) : kBorder);
    int knobRadius = radius - S(3);
    int knobX = on ? rect.right - radius : rect.left + radius;
    int knobY = (rect.top + rect.bottom) / 2;
    DrawCircle(dc, knobX, knobY, knobRadius, RGB(253, 251, 246), RGB(253, 251, 246));
}

void MainWindow::DrawRuleGlyph(HDC dc, const RECT& rect, const std::wstring& kind) {
    int centerX = (rect.left + rect.right) / 2;
    int centerY = (rect.top + rect.bottom) / 2;
    HPEN pen = CreatePen(PS_SOLID, S(2), kAccent);
    HGDIOBJ oldPen = SelectObject(dc, pen);
    HGDIOBJ oldBrush = SelectObject(dc, GetStockObject(NULL_BRUSH));
    if (kind == L"file") {
        int left = centerX - S(9);
        int top = centerY - S(12);
        int right = centerX + S(9);
        int bottom = centerY + S(12);
        Rectangle(dc, left, top, right, bottom);
        MoveToEx(dc, right - S(7), top, nullptr);
        LineTo(dc, right, top + S(7));
        MoveToEx(dc, right - S(7), top, nullptr);
        LineTo(dc, right - S(7), top + S(7));
        LineTo(dc, right, top + S(7));
        MoveToEx(dc, left + S(4), centerY, nullptr);
        LineTo(dc, right - S(4), centerY);
        MoveToEx(dc, left + S(4), centerY + S(5), nullptr);
        LineTo(dc, right - S(4), centerY + S(5));
    } else {
        int left = centerX - S(12);
        int top = centerY - S(9);
        int right = centerX + S(12);
        int bottom = centerY + S(10);
        RoundRect(dc, left, top + S(3), right, bottom, S(3), S(3));
        MoveToEx(dc, left, top + S(3), nullptr);
        LineTo(dc, left + S(3), top);
        LineTo(dc, left + S(10), top);
        LineTo(dc, left + S(12), top + S(3));
    }
    SelectObject(dc, oldBrush);
    SelectObject(dc, oldPen);
    DeleteObject(pen);
}

void MainWindow::DrawAppMark(HDC dc, int x, int y, int size) {
    if (appIcon_ != nullptr) {
        DrawIconEx(dc, x, y, appIcon_, size, size, 0, nullptr, DI_NORMAL);
        return;
    }
    RECT box{x, y, x + size, y + size};
    RoundFill(dc, box, S(11), kDarkRaised);
    DrawCircle(dc, x + size / 2, y + size / 2, size / 3, kAccent, kAccent);
    DrawTextLine(dc, L"S", box, fonts_.h2, kOnDark, DT_CENTER | DT_SINGLELINE | DT_VCENTER);
}

} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    SetProcessDPIAware();
    g_dpi = static_cast<int>(GetDpiForSystem());

    HRESULT comResult = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
    INITCOMMONCONTROLSEX commonControls{};
    commonControls.dwSize = sizeof(commonControls);
    commonControls.dwICC = ICC_STANDARD_CLASSES | ICC_WIN95_CLASSES;
    InitCommonControlsEx(&commonControls);

    HANDLE instanceMutex = CreateMutexW(nullptr, TRUE, L"Local\\SyncStickPortable.Instance");
    if (instanceMutex != nullptr && GetLastError() == ERROR_ALREADY_EXISTS) {
        HWND existing = FindWindowW(kMainClass, nullptr);
        if (existing != nullptr) {
            if (IsIconic(existing)) {
                ShowWindow(existing, SW_RESTORE);
            } else {
                ShowWindow(existing, SW_SHOW);
            }
            SetForegroundWindow(existing);
        }
        CloseHandle(instanceMutex);
        if (SUCCEEDED(comResult)) {
            CoUninitialize();
        }
        return 0;
    }

    MainWindow window(instance);
    int result = 0;
    if (window.Create()) {
        result = window.Run();
    }
    if (instanceMutex != nullptr) {
        ReleaseMutex(instanceMutex);
        CloseHandle(instanceMutex);
    }
    if (SUCCEEDED(comResult)) {
        CoUninitialize();
    }
    return result;
}
