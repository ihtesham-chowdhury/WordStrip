#include "TextService.h"

#include <ctffunc.h>

#include "LoadLog.h"

namespace wordstrip {
namespace {

/// Releases and nulls a COM pointer. Written out because this file acquires a lot of them on paths that can
/// fail at any step, and a missed Release here leaks inside somebody else's application.
template <typename T>
void SafeRelease(T*& p) {
    if (p != nullptr) {
        p->Release();
        p = nullptr;
    }
}

}  // namespace

LONG g_objectCount = 0;

TextService::TextService()
    : _refCount(1),
      _threadMgr(nullptr),
      _clientId(TF_CLIENTID_NULL),
      _threadMgrCookie(TF_INVALID_COOKIE),
      _context(nullptr),
      _editCookie(TF_INVALID_COOKIE) {
    InterlockedIncrement(&g_objectCount);
}

TextService::~TextService() {
    InterlockedDecrement(&g_objectCount);
}

// ---------------------------------------------------------------------------------------------------
// IUnknown
// ---------------------------------------------------------------------------------------------------

STDMETHODIMP TextService::QueryInterface(REFIID riid, void** ppv) {
    if (ppv == nullptr) return E_INVALIDARG;
    *ppv = nullptr;

    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfTextInputProcessor)) {
        *ppv = static_cast<ITfTextInputProcessor*>(this);
    } else if (IsEqualIID(riid, IID_ITfTextInputProcessorEx)) {
        *ppv = static_cast<ITfTextInputProcessorEx*>(this);
    } else if (IsEqualIID(riid, IID_ITfThreadMgrEventSink)) {
        *ppv = static_cast<ITfThreadMgrEventSink*>(this);
    } else if (IsEqualIID(riid, IID_ITfTextEditSink)) {
        *ppv = static_cast<ITfTextEditSink*>(this);
    }

    if (*ppv == nullptr) return E_NOINTERFACE;

    AddRef();
    return S_OK;
}

STDMETHODIMP_(ULONG) TextService::AddRef() {
    return InterlockedIncrement(&_refCount);
}

STDMETHODIMP_(ULONG) TextService::Release() {
    const LONG remaining = InterlockedDecrement(&_refCount);
    if (remaining == 0) delete this;
    return remaining;
}

// ---------------------------------------------------------------------------------------------------
// Activation
// ---------------------------------------------------------------------------------------------------

STDMETHODIMP TextService::Activate(ITfThreadMgr* threadMgr, TfClientId clientId) {
    return ActivateEx(threadMgr, clientId, 0);
}

STDMETHODIMP TextService::ActivateEx(ITfThreadMgr* threadMgr, TfClientId clientId, DWORD flags) {
    _threadMgr = threadMgr;
    if (_threadMgr) _threadMgr->AddRef();
    _clientId = clientId;

    LogEvent(flags & TF_TMAE_SECUREMODE ? L"ACTIVATE(secure)" : L"ACTIVATE");

    if (_threadMgr == nullptr) return S_OK;

    ITfSource* source = nullptr;
    if (SUCCEEDED(_threadMgr->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(&source)))) {
        source->AdviseSink(IID_ITfThreadMgrEventSink,
                           static_cast<ITfThreadMgrEventSink*>(this), &_threadMgrCookie);
        source->Release();
    }

    // Focus is usually already somewhere by the time a service activates - the user was typing, which is
    // what caused this. Without picking it up here the service stays silent until they click into a
    // different field.
    ITfDocumentMgr* focused = nullptr;
    if (SUCCEEDED(_threadMgr->GetFocus(&focused)) && focused != nullptr) {
        StartListeningTo(focused);
        focused->Release();
    }

    return S_OK;
}

STDMETHODIMP TextService::Deactivate() {
    LogEvent(L"DEACTIVATE");

    StopListening();
    SendNotEditable();
    _pipe.Close();

    if (_threadMgr != nullptr && _threadMgrCookie != TF_INVALID_COOKIE) {
        ITfSource* source = nullptr;
        if (SUCCEEDED(_threadMgr->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(&source)))) {
            source->UnadviseSink(_threadMgrCookie);
            source->Release();
        }
        _threadMgrCookie = TF_INVALID_COOKIE;
    }

    SafeRelease(_threadMgr);
    _clientId = TF_CLIENTID_NULL;

    return S_OK;
}

// ---------------------------------------------------------------------------------------------------
// Which document has focus
// ---------------------------------------------------------------------------------------------------

STDMETHODIMP TextService::OnInitDocumentMgr(ITfDocumentMgr*) { return S_OK; }
STDMETHODIMP TextService::OnUninitDocumentMgr(ITfDocumentMgr*) { return S_OK; }
STDMETHODIMP TextService::OnPushContext(ITfContext*) { return S_OK; }
STDMETHODIMP TextService::OnPopContext(ITfContext*) { return S_OK; }

STDMETHODIMP TextService::OnSetFocus(ITfDocumentMgr* focus, ITfDocumentMgr*) {
    StopListening();

    if (focus == nullptr) {
        // Focus left every text surface in this application - the user clicked a toolbar, or moved to
        // another window entirely. Say so, or the bar hangs over whatever they went to.
        SendNotEditable();
        return S_OK;
    }

    StartListeningTo(focus);
    return S_OK;
}

void TextService::StartListeningTo(ITfDocumentMgr* documentMgr) {
    if (documentMgr == nullptr) return;

    ITfContext* context = nullptr;
    if (FAILED(documentMgr->GetTop(&context)) || context == nullptr) {
        SendNotEditable();
        return;
    }

    ITfSource* source = nullptr;
    if (SUCCEEDED(context->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(&source)))) {
        if (SUCCEEDED(source->AdviseSink(IID_ITfTextEditSink,
                                         static_cast<ITfTextEditSink*>(this), &_editCookie))) {
            _context = context;
            _context->AddRef();
        }
        source->Release();
    }

    context->Release();

    if (_context == nullptr) SendNotEditable();
}

void TextService::StopListening() {
    if (_context == nullptr) return;

    if (_editCookie != TF_INVALID_COOKIE) {
        ITfSource* source = nullptr;
        if (SUCCEEDED(_context->QueryInterface(IID_ITfSource, reinterpret_cast<void**>(&source)))) {
            source->UnadviseSink(_editCookie);
            source->Release();
        }
        _editCookie = TF_INVALID_COOKIE;
    }

    SafeRelease(_context);
}

// ---------------------------------------------------------------------------------------------------
// Reading the document
// ---------------------------------------------------------------------------------------------------

STDMETHODIMP TextService::OnEndEdit(ITfContext* context, TfEditCookie readCookie, ITfEditRecord*) {
    // Fires for text changes and for selection changes alike, which between them cover everything that can
    // move the caret. The edit record could narrow that down, but reading the same short span either way is
    // cheaper than the branch is worth.
    CaptureAndSend(context, readCookie);
    return S_OK;
}

bool TextService::IsInputAllowed(ITfContext* context) {
    if (context == nullptr) return false;

    TF_STATUS status = {};
    if (SUCCEEDED(context->GetStatus(&status))) {
        if (status.dwStaticFlags & TS_SS_DISJOINTSEL) {
            // Nothing wrong with it, but a disjoint selection means "the caret is in several places", which
            // is not something a single word in progress describes.
            return false;
        }
        if (status.dwDynamicFlags & TS_SD_READONLY) return false;
    }

    // The compartment applications set when input methods must keep out. This is the mechanism by which a
    // host says "not here", and it is the closest thing TSF offers to a password-field signal.
    //
    // It is NOT a guarantee of one. TSF has no explicit "this is a password box" flag, and whether a given
    // browser sets this compartment on password inputs has to be established by testing rather than assumed.
    // Until it has been, treat suggestions appearing over a password field as an open risk - recorded in
    // CLAUDE_PROJECT_CONTEXT.md section 12. Nothing is learned or written on this path, so the exposure is
    // a visible suggestion rather than a stored secret, but that is a reason to check rather than to relax.
    ITfCompartmentMgr* compartments = nullptr;
    if (SUCCEEDED(context->QueryInterface(IID_ITfCompartmentMgr,
                                          reinterpret_cast<void**>(&compartments)))) {
        ITfCompartment* disabled = nullptr;
        if (SUCCEEDED(compartments->GetCompartment(GUID_COMPARTMENT_KEYBOARD_DISABLED, &disabled))) {
            VARIANT value;
            VariantInit(&value);
            if (SUCCEEDED(disabled->GetValue(&value)) && value.vt == VT_I4 && value.lVal != 0) {
                VariantClear(&value);
                disabled->Release();
                compartments->Release();
                return false;
            }
            VariantClear(&value);
            disabled->Release();
        }
        compartments->Release();
    }

    return true;
}

void TextService::CaptureAndSend(ITfContext* context, TfEditCookie readCookie) {
    if (context == nullptr) return;

    if (!IsInputAllowed(context)) {
        SendNotEditable();
        return;
    }

    TF_SELECTION selection = {};
    ULONG fetched = 0;
    if (FAILED(context->GetSelection(readCookie, TF_DEFAULT_SELECTION, 1, &selection, &fetched)) ||
        fetched == 0 || selection.range == nullptr) {
        SendNotEditable();
        return;
    }

    ContextSnapshot snapshot;
    snapshot.editable = true;

    // A non-empty selection means the user has text highlighted. Reported rather than acted on: replacing a
    // selection is a commit, and commits are Stage 3.
    ITfRange* caretRange = nullptr;
    if (SUCCEEDED(selection.range->Clone(&caretRange)) && caretRange != nullptr) {
        BOOL empty = FALSE;
        if (SUCCEEDED(caretRange->IsEmpty(readCookie, &empty))) snapshot.hasSelection = !empty;

        // Collapse to the start so that what follows describes the text before the caret, whether or not
        // anything was selected.
        caretRange->Collapse(readCookie, TF_ANCHOR_START);

        // Caret rectangle, for placing the bar. Best-effort: plenty of hosts decline, and the bar has a
        // sensible fallback position when it has nothing to follow. The range is already collapsed to the
        // caret, so an empty range is exactly the right thing to ask about.
        ITfContextView* view = nullptr;
        if (SUCCEEDED(context->GetActiveView(&view)) && view != nullptr) {
            RECT rect = {};
            BOOL clipped = FALSE;
            if (SUCCEEDED(view->GetTextExt(readCookie, caretRange, &rect, &clipped))) {
                snapshot.hasCaret = true;
                snapshot.caretLeft = rect.left;
                snapshot.caretTop = rect.top;
                snapshot.caretRight = rect.right;
                snapshot.caretBottom = rect.bottom;
            }
            view->Release();
        }

        // Walk the start anchor back over at most the cap, leaving the range spanning
        // [caret - n, caret]. ShiftStart reports how far it actually managed, which is less at the top of
        // a document - that is the answer, not a failure.
        LONG shifted = 0;
        if (SUCCEEDED(caretRange->ShiftStart(readCookie, -ContextSnapshot::kMaxTextChars, &shifted, nullptr))) {
            ULONG got = 0;
            if (SUCCEEDED(caretRange->GetText(readCookie, 0, snapshot.text,
                                              ContextSnapshot::kMaxTextChars, &got))) {
                snapshot.textChars = static_cast<int>(got);
            }
        }

        caretRange->Release();
    }

    selection.range->Release();

    _pipe.Send(snapshot);
}

void TextService::SendNotEditable() {
    ContextSnapshot snapshot;  // editable defaults to false, text is empty
    _pipe.Send(snapshot);
}

}  // namespace wordstrip
