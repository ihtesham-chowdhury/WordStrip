// WordStrip's Text Services Framework text service.
//
// Reads the text immediately before the caret out of whatever application has focus and sends it to the
// WordStrip tray process, which is where the dictionary, the language model and the suggestion bar live.
// It does not display anything, does not handle keys, and does not modify the document. Committing a
// suggestion through TSF is Stage 3 and is deliberately absent: this service can be wrong about what it
// reads and the worst outcome is a poor suggestion, whereas a service that writes can corrupt somebody's
// document.
//
// Threading: TSF calls in on the host application's UI thread, and every registered TIP on this machine is
// Apartment-model. Nothing here may block. The one call that could - sending down the pipe - is bounded to
// 20 ms and abandons rather than waits, because a blocking send is a frozen Chrome.

#pragma once

#include <windows.h>
#include <msctf.h>

#include "PipeClient.h"

namespace wordstrip {

class TextService final
    : public ITfTextInputProcessorEx,
      public ITfThreadMgrEventSink,
      public ITfTextEditSink {
public:
    TextService();

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    // ITfTextInputProcessor / Ex
    STDMETHODIMP Activate(ITfThreadMgr* threadMgr, TfClientId clientId) override;
    STDMETHODIMP Deactivate() override;
    STDMETHODIMP ActivateEx(ITfThreadMgr* threadMgr, TfClientId clientId, DWORD flags) override;

    // ITfThreadMgrEventSink - which document has focus
    STDMETHODIMP OnInitDocumentMgr(ITfDocumentMgr* documentMgr) override;
    STDMETHODIMP OnUninitDocumentMgr(ITfDocumentMgr* documentMgr) override;
    STDMETHODIMP OnSetFocus(ITfDocumentMgr* focus, ITfDocumentMgr* previous) override;
    STDMETHODIMP OnPushContext(ITfContext* context) override;
    STDMETHODIMP OnPopContext(ITfContext* context) override;

    // ITfTextEditSink - the document changed
    STDMETHODIMP OnEndEdit(ITfContext* context, TfEditCookie readCookie, ITfEditRecord* editRecord) override;

private:
    ~TextService();

    void StartListeningTo(ITfDocumentMgr* documentMgr);
    void StopListening();

    /// Reads the text before the caret under an existing read lock and sends it. Called only from OnEndEdit,
    /// which hands us a read cookie - requesting our own edit session would be a second lock for no reason.
    void CaptureAndSend(ITfContext* context, TfEditCookie readCookie);

    /// Tells WordStrip there is nothing to suggest against, so the bar comes down rather than hanging over
    /// whatever the user moved to.
    void SendNotEditable();

    /// Whether this context accepts input at all. Read-only documents and, importantly, surfaces the host
    /// has marked keyboard-disabled both answer false - see the implementation for what that covers and,
    /// more to the point, what it does not.
    static bool IsInputAllowed(ITfContext* context);

    LONG _refCount;
    ITfThreadMgr* _threadMgr;
    TfClientId _clientId;

    DWORD _threadMgrCookie;

    /// The context currently being listened to, and the advise cookie for it. Exactly one at a time: TSF
    /// focus is singular, and holding sinks on documents the user has left would mean reporting text from a
    /// window that is no longer in front of them.
    ITfContext* _context;
    DWORD _editCookie;

    PipeClient _pipe;
};

extern LONG g_objectCount;

}  // namespace wordstrip
