#include "LoadLog.h"

#include <shlobj.h>
#include <strsafe.h>

namespace wordstrip {
namespace {

// Same folder as everything else WordStrip keeps about its user, so "delete the WordStrip folder" stays a
// complete answer to "remove everything this has put on my machine".
bool LogPath(wchar_t* buffer, size_t chars) {
    PWSTR localAppData = nullptr;
    if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &localAppData))) return false;

    const HRESULT hr = StringCchPrintfW(buffer, chars, L"%s\\WordStrip", localAppData);
    CoTaskMemFree(localAppData);
    if (FAILED(hr)) return false;

    // The tray application normally creates this; the service may well load first, on a machine where the
    // user has never opened Settings.
    CreateDirectoryW(buffer, nullptr);

    return SUCCEEDED(StringCchCatW(buffer, chars, L"\\tip-load.log"));
}

void HostExecutableName(wchar_t* buffer, DWORD chars) {
    if (GetModuleFileNameW(nullptr, buffer, chars) == 0) {
        StringCchCopyW(buffer, chars, L"<unknown>");
        return;
    }

    // Just the file name. The full path of every host is noise, and on a shared log it is more of the user's
    // machine written down than the question needs.
    wchar_t* lastSlash = wcsrchr(buffer, L'\\');
    if (lastSlash && *(lastSlash + 1)) {
        StringCchCopyW(buffer, chars, lastSlash + 1);
    }
}

}  // namespace

void LogEvent(const wchar_t* event) {
    wchar_t path[MAX_PATH];
    if (!LogPath(path, ARRAYSIZE(path))) return;

    wchar_t host[MAX_PATH];
    HostExecutableName(host, ARRAYSIZE(host));

    SYSTEMTIME now;
    GetLocalTime(&now);

    wchar_t line[1024];
    if (FAILED(StringCchPrintfW(
            line, ARRAYSIZE(line),
            L"%04d-%02d-%02d %02d:%02d:%02d.%03d  %-16s  host=%s pid=%lu\r\n",
            now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond, now.wMilliseconds,
            event, host, GetCurrentProcessId()))) {
        return;
    }

    // FILE_SHARE_READ|WRITE because this is written from many processes at once - every application the user
    // types in has its own copy of this DLL. Opening exclusively would mean the second host silently logs
    // nothing, which is precisely the case the log exists to catch.
    const HANDLE file = CreateFileW(
        path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
        OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;

    // UTF-8 so the file opens sanely in anything, and so a host with an exotic executable name does not
    // produce mojibake in the one artifact used to decide whether that host works.
    char utf8[2048];
    const int bytes = WideCharToMultiByte(CP_UTF8, 0, line, -1, utf8, sizeof(utf8), nullptr, nullptr);
    if (bytes > 1) {
        DWORD written = 0;
        WriteFile(file, utf8, static_cast<DWORD>(bytes - 1), &written, nullptr);  // -1: drop the NUL
    }

    CloseHandle(file);
}

}  // namespace wordstrip
