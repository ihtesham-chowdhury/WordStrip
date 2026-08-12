// COM plumbing and TSF registration for the WordStrip text service.
//
// Registration writes to HKEY_CLASSES_ROOT and therefore needs administrator rights. That is not a choice:
// the Stage 1 spike found 21 text services registered on this machine and every one of them under HKLM,
// none under HKCU. The consequence was decided deliberately - WordStrip's ordinary installer stays per-user
// with no UAC, and registering the service is a separate opt-in step the user takes from Settings.
//
// See CLAUDE_PROJECT_CONTEXT.md section 14 for that decision and what constrains it. In particular: the
// elevation belongs to the registration step alone. The tray application must never run elevated, because a
// keyboard hook installed by an elevated process cannot see input going to non-elevated windows, and
// WordStrip would silently stop working everywhere ordinary.

#include <initguid.h>  // must precede Guids.h, in exactly one translation unit, to define rather than declare

#include <windows.h>
#include <msctf.h>
#include <olectl.h>
#include <strsafe.h>

#include <new>  // std::nothrow - allocation failure inside somebody else's process returns E_OUTOFMEMORY,
                // it does not throw through a COM boundary

#include "Guids.h"
#include "LoadLog.h"
#include "TextService.h"

namespace {

HINSTANCE g_instance = nullptr;
LONG g_lockCount = 0;

bool ModulePath(wchar_t* buffer, DWORD chars) {
    const DWORD written = GetModuleFileNameW(g_instance, buffer, chars);
    return written > 0 && written < chars;
}

// ---------------------------------------------------------------------------------------------------
// Class factory
// ---------------------------------------------------------------------------------------------------

class ClassFactory final : public IClassFactory {
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (ppv == nullptr) return E_INVALIDARG;
        *ppv = nullptr;

        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory)) {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    // The factory is a singleton living in static storage, so its lifetime is the DLL's. Reference counting
    // it would imply it could be destroyed, which it cannot.
    STDMETHODIMP_(ULONG) AddRef() override { return 1; }
    STDMETHODIMP_(ULONG) Release() override { return 1; }

    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override {
        if (ppv == nullptr) return E_INVALIDARG;
        *ppv = nullptr;

        if (outer != nullptr) return CLASS_E_NOAGGREGATION;

        auto* service = new (std::nothrow) wordstrip::TextService();
        if (service == nullptr) return E_OUTOFMEMORY;

        const HRESULT hr = service->QueryInterface(riid, ppv);
        service->Release();
        return hr;
    }

    STDMETHODIMP LockServer(BOOL lock) override {
        if (lock) {
            InterlockedIncrement(&g_lockCount);
        } else {
            InterlockedDecrement(&g_lockCount);
        }
        return S_OK;
    }
};

ClassFactory g_classFactory;

// ---------------------------------------------------------------------------------------------------
// Registry helpers
// ---------------------------------------------------------------------------------------------------

bool GuidToString(REFGUID guid, wchar_t* buffer, int chars) {
    return StringFromGUID2(guid, buffer, chars) > 0;
}

bool RegisterComServer() {
    wchar_t clsid[64];
    if (!GuidToString(CLSID_WordStripTextService, clsid, ARRAYSIZE(clsid))) return false;

    wchar_t modulePath[MAX_PATH];
    if (!ModulePath(modulePath, ARRAYSIZE(modulePath))) return false;

    wchar_t keyPath[128];
    if (FAILED(StringCchPrintfW(keyPath, ARRAYSIZE(keyPath), L"CLSID\\%s", clsid))) return false;

    HKEY key = nullptr;
    if (RegCreateKeyExW(HKEY_CLASSES_ROOT, keyPath, 0, nullptr, REG_OPTION_NON_VOLATILE,
                        KEY_WRITE, nullptr, &key, nullptr) != ERROR_SUCCESS) {
        return false;
    }

    const wchar_t* description = WORDSTRIP_TIP_DESCRIPTION;
    RegSetValueExW(key, nullptr, 0, REG_SZ, reinterpret_cast<const BYTE*>(description),
                   static_cast<DWORD>((wcslen(description) + 1) * sizeof(wchar_t)));

    HKEY serverKey = nullptr;
    const LSTATUS status = RegCreateKeyExW(key, L"InprocServer32", 0, nullptr, REG_OPTION_NON_VOLATILE,
                                           KEY_WRITE, nullptr, &serverKey, nullptr);
    RegCloseKey(key);
    if (status != ERROR_SUCCESS) return false;

    RegSetValueExW(serverKey, nullptr, 0, REG_SZ, reinterpret_cast<const BYTE*>(modulePath),
                   static_cast<DWORD>((wcslen(modulePath) + 1) * sizeof(wchar_t)));

    // Apartment, matching every other TIP on the system. TSF calls in on the host's UI thread and the
    // service must not assume otherwise.
    const wchar_t* threading = L"Apartment";
    RegSetValueExW(serverKey, L"ThreadingModel", 0, REG_SZ, reinterpret_cast<const BYTE*>(threading),
                   static_cast<DWORD>((wcslen(threading) + 1) * sizeof(wchar_t)));

    RegCloseKey(serverKey);
    return true;
}

void UnregisterComServer() {
    wchar_t clsid[64];
    if (!GuidToString(CLSID_WordStripTextService, clsid, ARRAYSIZE(clsid))) return;

    wchar_t keyPath[128];
    if (FAILED(StringCchPrintfW(keyPath, ARRAYSIZE(keyPath), L"CLSID\\%s", clsid))) return;

    RegDeleteTreeW(HKEY_CLASSES_ROOT, keyPath);
}

// Tells TSF that this CLSID is a text service, gives it a name the input-method picker can show, and
// declares it a keyboard TIP.
HRESULT RegisterProfile() {
    ITfInputProcessorProfiles* profiles = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
                                  IID_ITfInputProcessorProfiles, reinterpret_cast<void**>(&profiles));
    if (FAILED(hr)) return hr;

    hr = profiles->Register(CLSID_WordStripTextService);
    if (SUCCEEDED(hr)) {
        wchar_t modulePath[MAX_PATH];
        const bool havePath = ModulePath(modulePath, ARRAYSIZE(modulePath));

        hr = profiles->AddLanguageProfile(
            CLSID_WordStripTextService,
            WORDSTRIP_TIP_LANGID,
            GUID_WordStripProfile,
            WORDSTRIP_TIP_DESCRIPTION,
            static_cast<ULONG>(wcslen(WORDSTRIP_TIP_DESCRIPTION)),
            havePath ? modulePath : nullptr,
            havePath ? static_cast<ULONG>(wcslen(modulePath)) : 0,
            0);
    }

    profiles->Release();
    return hr;
}

void UnregisterProfile() {
    ITfInputProcessorProfiles* profiles = nullptr;
    if (FAILED(CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
                                IID_ITfInputProcessorProfiles, reinterpret_cast<void**>(&profiles)))) {
        return;
    }

    profiles->Unregister(CLSID_WordStripTextService);
    profiles->Release();
}

HRESULT RegisterCategories() {
    ITfCategoryMgr* categories = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER,
                                  IID_ITfCategoryMgr, reinterpret_cast<void**>(&categories));
    if (FAILED(hr)) return hr;

    // Keyboard TIP, and nothing else. GUID_TFCAT_TIPCAP_SECUREMODE in particular is left off on purpose:
    // it would load this into the secure desktop, and a service that has not yet been shown to behave in
    // Notepad has no business running on the credential screen.
    hr = categories->RegisterCategory(CLSID_WordStripTextService, GUID_TFCAT_TIP_KEYBOARD,
                                      CLSID_WordStripTextService);

    categories->Release();
    return hr;
}

void UnregisterCategories() {
    ITfCategoryMgr* categories = nullptr;
    if (FAILED(CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER,
                                IID_ITfCategoryMgr, reinterpret_cast<void**>(&categories)))) {
        return;
    }

    categories->UnregisterCategory(CLSID_WordStripTextService, GUID_TFCAT_TIP_KEYBOARD,
                                   CLSID_WordStripTextService);
    categories->Release();
}

}  // namespace

// ---------------------------------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------------------------------

BOOL APIENTRY DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    switch (reason) {
        case DLL_PROCESS_ATTACH:
            g_instance = instance;
            // Nothing else here on purpose. This runs under the loader lock, inside somebody else's
            // application, and the list of things that deadlock there is longer than the list that do not.
            // The load log is written from DllGetClassObject instead.
            DisableThreadLibraryCalls(instance);
            break;
        default:
            break;
    }

    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv) {
    if (ppv == nullptr) return E_INVALIDARG;
    *ppv = nullptr;

    if (!IsEqualCLSID(rclsid, CLSID_WordStripTextService)) return CLASS_E_CLASSNOTAVAILABLE;

    // The earliest point outside the loader lock at which we know a host has decided to instantiate us.
    // Logged separately from ACTIVATE because the gap between the two is diagnostic: reaching here and never
    // activating means TSF created the service and then rejected it.
    wordstrip::LogEvent(L"CREATE");

    return g_classFactory.QueryInterface(riid, ppv);
}

STDAPI DllCanUnloadNow() {
    return (wordstrip::g_objectCount == 0 && g_lockCount == 0) ? S_OK : S_FALSE;
}

STDAPI DllRegisterServer() {
    if (!RegisterComServer()) {
        UnregisterComServer();
        return SELFREG_E_CLASS;
    }

    if (FAILED(RegisterProfile())) {
        UnregisterProfile();
        UnregisterComServer();
        return SELFREG_E_CLASS;
    }

    if (FAILED(RegisterCategories())) {
        UnregisterCategories();
        UnregisterProfile();
        UnregisterComServer();
        return SELFREG_E_CLASS;
    }

    return S_OK;
}

STDAPI DllUnregisterServer() {
    // Unconditional and in reverse order. Each step tolerates the thing it removes being absent already,
    // because the state this has to clean up includes "a previous registration that failed halfway".
    UnregisterCategories();
    UnregisterProfile();
    UnregisterComServer();
    return S_OK;
}
