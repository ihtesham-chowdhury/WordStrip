#include "PipeClient.h"

#include <strsafe.h>

#include <cstdint>
#include <cstring>

#include "LoadLog.h"

namespace wordstrip {
namespace {

constexpr uint32_t kProtocolVersion = 1;

constexpr uint32_t kFlagEditable = 1u << 0;
constexpr uint32_t kFlagPassword = 1u << 1;
constexpr uint32_t kFlagSelection = 1u << 2;
constexpr uint32_t kFlagHasCaret = 1u << 3;

// Header: version, flags, four caret longs, text length. Little-endian, which is the only thing this will
// ever run on and is what the managed reader assumes.
constexpr int kHeaderBytes = 4 + 4 + (4 * 4) + 4;

/// One pipe per logon session, matching TsfContextChannel.PipeNameForCurrentSession(). Session rather than
/// user because it is three lines on both sides and still keeps two simultaneously signed-in users apart.
bool PipeName(wchar_t* buffer, size_t chars) {
    DWORD session = 0;
    if (!ProcessIdToSessionId(GetCurrentProcessId(), &session)) return false;

    return SUCCEEDED(StringCchPrintfW(buffer, chars, L"\\\\.\\pipe\\WordStrip.TextContext.%lu", session));
}

void WriteU32(BYTE* p, uint32_t v) { memcpy(p, &v, 4); }
void WriteI32(BYTE* p, LONG v) { memcpy(p, &v, 4); }

}  // namespace

PipeClient::~PipeClient() {
    Close();
}

void PipeClient::Close() {
    if (_pipe != INVALID_HANDLE_VALUE) {
        CloseHandle(_pipe);
        _pipe = INVALID_HANDLE_VALUE;
    }

    if (_writeEvent != nullptr) {
        CloseHandle(_writeEvent);
        _writeEvent = nullptr;
    }
}

bool PipeClient::EnsureConnected() {
    if (_pipe != INVALID_HANDLE_VALUE) return true;

    // Rate-limit. Typing in Chrome with WordStrip closed would otherwise mean a failed CreateFile for every
    // character, inside somebody else's UI thread.
    const ULONGLONG now = GetTickCount64();
    if (_lastAttemptTick != 0 && (now - _lastAttemptTick) < kRetryIntervalMs) return false;
    _lastAttemptTick = now;

    wchar_t name[128];
    if (!PipeName(name, ARRAYSIZE(name))) return false;

    // No WaitNamedPipe and no retry loop: if no instance is free right now, the answer is to drop this
    // update and try on the next keystroke. Blocking here would freeze the host application's UI.
    //
    // FILE_FLAG_OVERLAPPED so the write below can be abandoned rather than waited on indefinitely.
    const HANDLE pipe = CreateFileW(
        name, GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) return false;

    // Message mode, matching the server. Without this the server's per-read framing breaks and it sees a
    // byte stream it will parse as one malformed message after another.
    DWORD mode = PIPE_READMODE_MESSAGE;
    if (!SetNamedPipeHandleState(pipe, &mode, nullptr, nullptr)) {
        CloseHandle(pipe);
        return false;
    }

    const HANDLE event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (event == nullptr) {
        CloseHandle(pipe);
        return false;
    }

    _pipe = pipe;
    _writeEvent = event;
    return true;
}

bool PipeClient::Send(const ContextSnapshot& snapshot) {
    const bool delivered = SendCore(snapshot);

    if (delivered && !_loggedSuccess) {
        _loggedSuccess = true;
        LogEvent(L"PIPE-OK");
    } else if (!delivered && !_loggedFailure) {
        _loggedFailure = true;
        LogEvent(L"PIPE-FAIL");
    }

    return delivered;
}

bool PipeClient::SendCore(const ContextSnapshot& snapshot) {
    if (!EnsureConnected()) return false;

    int chars = snapshot.textChars;
    if (chars < 0) chars = 0;
    if (chars > ContextSnapshot::kMaxTextChars) chars = ContextSnapshot::kMaxTextChars;

    BYTE buffer[kHeaderBytes + (ContextSnapshot::kMaxTextChars * 2)];

    uint32_t flags = 0;
    if (snapshot.editable) flags |= kFlagEditable;
    if (snapshot.password) flags |= kFlagPassword;
    if (snapshot.hasSelection) flags |= kFlagSelection;
    if (snapshot.hasCaret) flags |= kFlagHasCaret;

    WriteU32(buffer + 0, kProtocolVersion);
    WriteU32(buffer + 4, flags);
    WriteI32(buffer + 8, snapshot.caretLeft);
    WriteI32(buffer + 12, snapshot.caretTop);
    WriteI32(buffer + 16, snapshot.caretRight);
    WriteI32(buffer + 20, snapshot.caretBottom);
    WriteU32(buffer + 24, static_cast<uint32_t>(chars));

    if (chars > 0) memcpy(buffer + kHeaderBytes, snapshot.text, static_cast<size_t>(chars) * 2);

    const DWORD total = static_cast<DWORD>(kHeaderBytes + (chars * 2));

    OVERLAPPED overlapped = {};
    overlapped.hEvent = _writeEvent;
    ResetEvent(_writeEvent);

    DWORD written = 0;
    if (WriteFile(_pipe, buffer, total, &written, &overlapped)) return true;  // completed inline

    if (GetLastError() != ERROR_IO_PENDING) {
        // The tray application closed, or the pipe broke. Drop the handle and let the next send reconnect;
        // there is nothing to report and nobody to report it to.
        Close();
        return false;
    }

    if (WaitForSingleObject(_writeEvent, kWriteTimeoutMs) != WAIT_OBJECT_0) {
        // WordStrip is not draining the pipe. Abandon this update rather than hold the host's UI thread any
        // longer; the next keystroke will produce another. CancelIoEx before returning, or the overlapped
        // structure on this stack outlives the operation writing into it.
        CancelIoEx(_pipe, &overlapped);
        WaitForSingleObject(_writeEvent, INFINITE);
        Close();
        return false;
    }

    return GetOverlappedResult(_pipe, &overlapped, &written, FALSE) && written == total;
}

}  // namespace wordstrip
