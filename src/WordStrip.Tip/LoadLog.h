// Proof of life for a DLL that runs inside other people's processes.
//
// A text service has no window, no console and no obvious way to say "I loaded". It is also loaded into
// every application that accepts text, which makes a debugger an awkward first instrument. This writes one
// line per event to a file, and that file is both how Stage 1 is verified and the raw material for the
// compatibility matrix the phase brief requires: after using several applications, the log says which of
// them actually loaded the service.
//
// Deliberately NOT called from DllMain. File I/O under the loader lock is a documented way to deadlock a
// host process, and deadlocking Word to find out whether a DLL loaded is a poor trade. The earliest safe
// marker is DllGetClassObject, which runs after the loader lock is released.

#pragma once

#include <windows.h>

namespace wordstrip {

// Appends "<timestamp>  <event>  host=<exe> pid=<n>" to %LOCALAPPDATA%\WordStrip\tip-load.log.
// Never throws, never blocks on failure, and silently does nothing if the file cannot be opened - a
// diagnostic that can take down its host is worse than no diagnostic.
void LogEvent(const wchar_t* event);

}  // namespace wordstrip
