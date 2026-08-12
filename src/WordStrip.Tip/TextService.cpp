#include "TextService.h"

#include "LoadLog.h"

namespace wordstrip {

LONG g_objectCount = 0;

TextService::TextService()
    : _refCount(1), _threadMgr(nullptr), _clientId(TF_CLIENTID_NULL) {
    InterlockedIncrement(&g_objectCount);
}

TextService::~TextService() {
    InterlockedDecrement(&g_objectCount);
}

STDMETHODIMP TextService::QueryInterface(REFIID riid, void** ppv) {
    if (ppv == nullptr) return E_INVALIDARG;
    *ppv = nullptr;

    if (IsEqualIID(riid, IID_IUnknown) ||
        IsEqualIID(riid, IID_ITfTextInputProcessor)) {
        *ppv = static_cast<ITfTextInputProcessor*>(this);
    } else if (IsEqualIID(riid, IID_ITfTextInputProcessorEx)) {
        *ppv = static_cast<ITfTextInputProcessorEx*>(this);
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

STDMETHODIMP TextService::Activate(ITfThreadMgr* threadMgr, TfClientId clientId) {
    // Windows calls whichever of the two it prefers; routing one into the other means there is a single
    // activation path to reason about rather than two that must be kept in step.
    return ActivateEx(threadMgr, clientId, 0);
}

STDMETHODIMP TextService::ActivateEx(ITfThreadMgr* threadMgr, TfClientId clientId, DWORD flags) {
    _threadMgr = threadMgr;
    if (_threadMgr) _threadMgr->AddRef();
    _clientId = clientId;

    // The moment that answers Stage 1's question. If this line appears in the log with host=chrome.exe, a
    // WordStrip text service is running inside Chrome.
    LogEvent(flags & TF_TMAE_SECUREMODE ? L"ACTIVATE(secure)" : L"ACTIVATE");

    return S_OK;
}

STDMETHODIMP TextService::Deactivate() {
    LogEvent(L"DEACTIVATE");

    if (_threadMgr) {
        _threadMgr->Release();
        _threadMgr = nullptr;
    }
    _clientId = TF_CLIENTID_NULL;

    return S_OK;
}

}  // namespace wordstrip
