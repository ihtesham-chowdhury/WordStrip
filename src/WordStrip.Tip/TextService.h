// WordStrip's Text Services Framework text service.
//
// Stage 1 of the phase brief, and deliberately inert: it registers, it loads, it records that it loaded, and
// it does nothing else. No text store, no key event sink, no candidate UI. The point of this stage is to
// find out whether a TIP written here actually gets loaded by Chrome, Word and WinUI applications, and that
// question is answered far more cleanly by something that cannot itself be the reason a host misbehaves.
//
// Threading: every registered TIP on this machine is Apartment-model, and TSF calls in on the host's UI
// thread. Nothing here may block - the brief is explicit that model loading, disk I/O and inference must
// stay off these callbacks. The one thing that touches disk is the load log, which fires twice in the
// lifetime of a host process rather than per keystroke.

#pragma once

#include <windows.h>
#include <msctf.h>

namespace wordstrip {

class TextService final : public ITfTextInputProcessorEx {
public:
    TextService();

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    // ITfTextInputProcessor
    STDMETHODIMP Activate(ITfThreadMgr* threadMgr, TfClientId clientId) override;
    STDMETHODIMP Deactivate() override;

    // ITfTextInputProcessorEx
    STDMETHODIMP ActivateEx(ITfThreadMgr* threadMgr, TfClientId clientId, DWORD flags) override;

private:
    ~TextService();

    LONG _refCount;
    ITfThreadMgr* _threadMgr;
    TfClientId _clientId;
};

// Live object count. DllCanUnloadNow consults this: returning S_OK while an instance is alive lets the host
// unload the DLL out from under a live COM pointer, which crashes the host rather than this.
extern LONG g_objectCount;

}  // namespace wordstrip
