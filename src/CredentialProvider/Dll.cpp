#include "common.h"
#include "MFS100Provider.h"

HINSTANCE g_hInst = NULL;
LONG g_cRef = 0;

class CClassFactory : public IClassFactory
{
public:
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        static const QITAB qit[] =
        {
            QITABENT(CClassFactory, IClassFactory),
            { 0 }
        };
        return QISearch(this, qit, riid, ppv);
    }

    IFACEMETHODIMP_(ULONG) AddRef()
    {
        return InterlockedIncrement(&_cRef);
    }

    IFACEMETHODIMP_(ULONG) Release()
    {
        LONG cRef = InterlockedDecrement(&_cRef);
        if (cRef == 0)
        {
            delete this;
        }
        return cRef;
    }

    IFACEMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv)
    {
        if (ppv == NULL) return E_POINTER;
        *ppv = NULL;
        if (pUnkOuter != NULL) return CLASS_E_NOAGGREGATION;

        MFS100Provider* pProvider = new MFS100Provider();
        if (pProvider == NULL) return E_OUTOFMEMORY;

        HRESULT hr = pProvider->QueryInterface(riid, ppv);
        pProvider->Release();
        return hr;
    }

    IFACEMETHODIMP LockServer(BOOL fLock)
    {
        if (fLock)
            InterlockedIncrement(&g_cRef);
        else
            InterlockedDecrement(&g_cRef);
        return S_OK;
    }

    CClassFactory() : _cRef(1) {}

private:
    ~CClassFactory() {}
    LONG _cRef;
};

BOOL WINAPI DllMain(HINSTANCE hInst, DWORD dwReason, LPVOID lpReserved)
{
    if (dwReason == DLL_PROCESS_ATTACH)
    {
        g_hInst = hInst;
        DisableThreadLibraryCalls(hInst);
    }
    return TRUE;
}

STDAPI DllCanUnloadNow()
{
    return (g_cRef == 0) ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    if (ppv == NULL) return E_POINTER;
    *ppv = NULL;

    if (IsEqualCLSID(rclsid, CLSID_MFS100CredentialProvider))
    {
        CClassFactory* pFactory = new CClassFactory();
        if (pFactory == NULL) return E_OUTOFMEMORY;

        HRESULT hr = pFactory->QueryInterface(riid, ppv);
        pFactory->Release();
        return hr;
    }

    return CLASS_E_CLASSNOTAVAILABLE;
}

static const WCHAR c_szClsidKey[] = L"Software\\Classes\\CLSID\\{B16F0239-0144-4284-9D27-E4B283626155}";
static const WCHAR c_szCpKey[]    = L"Software\\Microsoft\\Windows\\CurrentVersion\\Authentication\\Credential Providers\\{B16F0239-0144-4284-9D27-E4B283626155}";

STDAPI DllRegisterServer()
{
    WCHAR szModule[MAX_PATH];
    if (GetModuleFileNameW(g_hInst, szModule, ARRAYSIZE(szModule)) == 0)
        return HRESULT_FROM_WIN32(GetLastError());

    // 1. Register COM CLSID
    HKEY hKey = NULL;
    LONG lRes = RegCreateKeyExW(HKEY_LOCAL_MACHINE, c_szClsidKey, 0, NULL,
                                REG_OPTION_NON_VOLATILE, KEY_WRITE, NULL, &hKey, NULL);
    if (lRes == ERROR_SUCCESS)
    {
        const WCHAR szDesc[] = L"Mantra MFS100 Credential Provider";
        RegSetValueExW(hKey, NULL, 0, REG_SZ, (const BYTE*)szDesc, sizeof(szDesc));

        HKEY hInproc = NULL;
        lRes = RegCreateKeyExW(hKey, L"InprocServer32", 0, NULL,
                               REG_OPTION_NON_VOLATILE, KEY_WRITE, NULL, &hInproc, NULL);
        if (lRes == ERROR_SUCCESS)
        {
            RegSetValueExW(hInproc, NULL, 0, REG_SZ, (const BYTE*)szModule, (DWORD)((wcslen(szModule) + 1) * sizeof(WCHAR)));
            const WCHAR szModel[] = L"Apartment";
            RegSetValueExW(hInproc, L"ThreadingModel", 0, REG_SZ, (const BYTE*)szModel, sizeof(szModel));
            RegCloseKey(hInproc);
        }
        RegCloseKey(hKey);
    }

    // 2. Register with Windows Credential Providers
    lRes = RegCreateKeyExW(HKEY_LOCAL_MACHINE, c_szCpKey, 0, NULL,
                           REG_OPTION_NON_VOLATILE, KEY_WRITE, NULL, &hKey, NULL);
    if (lRes == ERROR_SUCCESS)
    {
        const WCHAR szName[] = L"MFS100CredentialProvider";
        RegSetValueExW(hKey, NULL, 0, REG_SZ, (const BYTE*)szName, sizeof(szName));
        RegCloseKey(hKey);
    }

    return (lRes == ERROR_SUCCESS) ? S_OK : HRESULT_FROM_WIN32(lRes);
}

STDAPI DllUnregisterServer()
{
    RegDeleteKeyW(HKEY_LOCAL_MACHINE, c_szCpKey);
    RegDeleteKeyW(HKEY_LOCAL_MACHINE, L"Software\\Classes\\CLSID\\{B16F0239-0144-4284-9D27-E4B283626155}\\InprocServer32");
    RegDeleteKeyW(HKEY_LOCAL_MACHINE, c_szClsidKey);
    return S_OK;
}
