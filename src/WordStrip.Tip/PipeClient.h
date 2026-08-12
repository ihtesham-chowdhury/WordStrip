// Sends context from inside a host application to the WordStrip tray process.
//
// Everything here is written from the standpoint that this code runs inside somebody else's application:
// Chrome, Word, Explorer. It must never block their UI thread, never throw, and never care whether WordStrip
// is running. A tray application that is not there is the ordinary case, not an error - the user may not
// have started it, may have quit it, or may never have installed the part that listens.

#pragma once

#include <windows.h>

namespace wordstrip {

/// One context snapshot, matching TsfContextMessage on the managed side. Any change here is a wire-format
/// change and must bump the version on both sides.
struct ContextSnapshot {
    bool editable = false;
    bool password = false;
    bool hasSelection = false;
    bool hasCaret = false;
    LONG caretLeft = 0, caretTop = 0, caretRight = 0, caretBottom = 0;

    /// Text immediately before the caret, oldest first. Never more than kMaxTextChars - the cap is a privacy
    /// requirement from the phase brief, not a buffer convenience.
    static constexpr int kMaxTextChars = 128;
    wchar_t text[kMaxTextChars] = {};
    int textChars = 0;
};

/// Connects lazily and reconnects on failure, with a floor on how often it will try.
class PipeClient {
public:
    ~PipeClient();

    /// Sends a snapshot, connecting first if needed. Returns false if it could not be delivered, which the
    /// caller is expected to ignore - there is nothing useful a text service can do about WordStrip being
    /// closed, and reporting it anywhere would mean writing to disk on the typing path.
    bool Send(const ContextSnapshot& snapshot);

    void Close();

private:
    bool EnsureConnected();
    bool SendCore(const ContextSnapshot& snapshot);

    HANDLE _pipe = INVALID_HANDLE_VALUE;

    /// Reused across sends. Creating an event per keystroke inside a host's UI thread is avoidable waste.
    HANDLE _writeEvent = nullptr;

    /// Whether the first success and the first failure have been logged.
    ///
    /// Logged once each, never per send. Stage 1's lesson was that "you will see nothing, and that is
    /// success" is a hard thing to verify against, so the question "did context actually leave this host?"
    /// needs an answer someone can look up. Logging every keystroke would answer it by making the file
    /// unreadable and putting disk I/O on the typing path.
    bool _loggedSuccess = false;
    bool _loggedFailure = false;

    /// Last attempt, so a host does not try to connect on every keystroke while WordStrip is not running.
    /// Without this, typing in Chrome with the tray app closed would mean a failed CreateFile per character.
    ULONGLONG _lastAttemptTick = 0;

    static constexpr ULONGLONG kRetryIntervalMs = 2000;

    /// How long a send may hold the host's UI thread before it is abandoned.
    ///
    /// This is the number that matters most in this file. Writes happen from a TSF callback on the host
    /// application's UI thread, so a blocking write is a frozen Chrome. The pipe is opened overlapped and
    /// the wait is bounded: if WordStrip is wedged or not draining, the update is dropped and the next
    /// keystroke tries again. A missed suggestion is invisible; a stalled browser is not.
    static constexpr DWORD kWriteTimeoutMs = 20;
};

}  // namespace wordstrip
