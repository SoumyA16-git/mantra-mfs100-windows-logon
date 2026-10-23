#include "MFS100Provider.h"
#include <sddl.h>

static const CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR s_rgFieldDescriptors[] =
{
    { FID_LOGO,       CPFT_TILE_IMAGE,    (PWSTR)L"MFS100 Logo" },
    { FID_LARGE_TEXT, CPFT_LARGE_TEXT,    (PWSTR)L"MFS100 Biometric Login" },
    { FID_SMALL_TEXT, CPFT_SMALL_TEXT,    (PWSTR)L"Biometric Status" },
    { FID_USERNAME,   CPFT_EDIT_TEXT,     (PWSTR)L"Username" },
    { FID_PASSWORD,   CPFT_PASSWORD_TEXT, (PWSTR)L"Password" },
    { FID_SUBMIT,     CPFT_SUBMIT_BUTTON, (PWSTR)L"Logon" }
};

MFS100Provider::MFS100Provider() :
    _cRef(1),
    _cpus(CPUS_INVALID),
    _pEvents(NULL),
    _upAdviseContext(0),
    _pCredential(NULL)
{
    LogDebug(L"[MFS100Provider] Constructor called.");
}

MFS100Provider::~MFS100Provider()
{
    LogDebug(L"[MFS100Provider] Destructor called.");
    if (_pCredential != NULL)
    {
        _pCredential->Release();
        _pCredential = NULL;
    }
    if (_pEvents != NULL)
    {
        _pEvents->Release();
        _pEvents = NULL;
    }
}

HRESULT MFS100Provider::QueryInterface(REFIID riid, void** ppv)
{
    if (ppv == NULL) return E_POINTER;
    static const QITAB qit[] =
    {
        QITABENT(MFS100Provider, ICredentialProvider),
        QITABENT(MFS100Provider, ICredentialProviderSetUserArray),
        { 0 }
    };
    return QISearch(this, qit, riid, ppv);
}

ULONG MFS100Provider::AddRef()
{
    return InterlockedIncrement(&_cRef);
}

ULONG MFS100Provider::Release()
{
    LONG cRef = InterlockedDecrement(&_cRef);
    if (cRef == 0)
    {
        delete this;
    }
    return cRef;
}

HRESULT MFS100Provider::SetUsageScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, DWORD dwFlags)
{
    LogDebug(L"[MFS100Provider] SetUsageScenario: %d, Flags: 0x%X", cpus, dwFlags);
    switch (cpus)
    {
    case CPUS_LOGON:
    case CPUS_UNLOCK_WORKSTATION:
    case CPUS_CREDUI:
        _cpus = cpus;
        return S_OK;
    default:
        return E_NOTIMPL;
    }
}

HRESULT MFS100Provider::SetSerialization(const CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* pcpcs)
{
    LogDebug(L"[MFS100Provider] SetSerialization called.");
    return S_OK;
}

HRESULT MFS100Provider::Advise(ICredentialProviderEvents* pcpe, UINT_PTR upAdviseContext)
{
    LogDebug(L"[MFS100Provider] Advise called.");
    if (_pEvents != NULL)
    {
        _pEvents->Release();
    }
    _pEvents = pcpe;
    if (_pEvents != NULL)
    {
        _pEvents->AddRef();
    }
    _upAdviseContext = upAdviseContext;
    return S_OK;
}

HRESULT MFS100Provider::UnAdvise()
{
    LogDebug(L"[MFS100Provider] UnAdvise called.");
    if (_pEvents != NULL)
    {
        _pEvents->Release();
        _pEvents = NULL;
    }
    _upAdviseContext = 0;
    return S_OK;
}

HRESULT MFS100Provider::GetFieldDescriptorCount(DWORD* pdwCount)
{
    if (pdwCount == NULL) return E_POINTER;
    *pdwCount = ARRAYSIZE(s_rgFieldDescriptors);
    return S_OK;
}

HRESULT MFS100Provider::GetFieldDescriptorAt(DWORD dwIndex, CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR** ppcpfd)
{
    if (ppcpfd == NULL) return E_POINTER;
    if (dwIndex >= ARRAYSIZE(s_rgFieldDescriptors)) return E_INVALIDARG;

    CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR* pcpfd = (CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR*)CoTaskMemAlloc(sizeof(CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR));
    if (pcpfd == NULL) return E_OUTOFMEMORY;

    pcpfd->dwFieldID = s_rgFieldDescriptors[dwIndex].dwFieldID;
    pcpfd->cpft = s_rgFieldDescriptors[dwIndex].cpft;

    DWORD cch = (DWORD)wcslen(s_rgFieldDescriptors[dwIndex].pszLabel) + 1;
    pcpfd->pszLabel = (PWSTR)CoTaskMemAlloc(cch * sizeof(WCHAR));
    if (pcpfd->pszLabel == NULL)
    {
        CoTaskMemFree(pcpfd);
        return E_OUTOFMEMORY;
    }
    StringCchCopyW(pcpfd->pszLabel, cch, s_rgFieldDescriptors[dwIndex].pszLabel);

    *ppcpfd = pcpfd;
    return S_OK;
}

HRESULT MFS100Provider::GetCredentialCount(DWORD* pdwCount, DWORD* pdwDefault, BOOL* pbAutoLogonWithDefault)
{
    if (pdwCount == NULL || pdwDefault == NULL || pbAutoLogonWithDefault == NULL)
        return E_POINTER;

    if (_pCredential == NULL)
    {
        _pCredential = new MFS100Credential();
        if (_pCredential != NULL)
        {
            _pCredential->Initialize(_cpus, L"", L"");
        }
    }

    *pdwCount = (_pCredential != NULL) ? 1 : 0;
    *pdwDefault = 0;
    *pbAutoLogonWithDefault = FALSE;

    LogDebug(L"[MFS100Provider] GetCredentialCount: %d", *pdwCount);
    return S_OK;
}

HRESULT MFS100Provider::GetCredentialAt(DWORD dwIndex, ICredentialProviderCredential** ppcpc)
{
    if (ppcpc == NULL) return E_POINTER;
    if (dwIndex != 0 || _pCredential == NULL) return E_INVALIDARG;

    *ppcpc = _pCredential;
    (*ppcpc)->AddRef();
    return S_OK;
}

HRESULT MFS100Provider::SetUserArray(ICredentialProviderUserArray* users)
{
    LogDebug(L"[MFS100Provider] SetUserArray called.");
    if (users != NULL)
    {
        DWORD dwCount = 0;
        users->GetCount(&dwCount);
        LogDebug(L"[MFS100Provider] SetUserArray user count: %d", dwCount);

        for (DWORD i = 0; i < dwCount; i++)
        {
            ICredentialProviderUser* pUser = NULL;
            if (SUCCEEDED(users->GetAt(i, &pUser)) && pUser != NULL)
            {
                PWSTR pszSid = NULL;
                if (SUCCEEDED(pUser->GetSid(&pszSid)) && pszSid != NULL)
                {
                    PSID pSid = NULL;
                    if (ConvertStringSidToSidW(pszSid, &pSid) && pSid != NULL)
                    {
                        WCHAR szName[256] = L"";
                        WCHAR szDomain[256] = L"";
                        DWORD cchName = ARRAYSIZE(szName);
                        DWORD cchDomain = ARRAYSIZE(szDomain);
                        SID_NAME_USE snu;
                        if (LookupAccountSidW(NULL, pSid, szName, &cchName, szDomain, &cchDomain, &snu))
                        {
                            LogDebug(L"[MFS100Provider] User[%d]: Domain='%s', Name='%s', SID='%s'",
                                     i, szDomain, szName, pszSid);

                            if (_pCredential != NULL)
                            {
                                WCHAR szFull[512];
                                StringCchPrintfW(szFull, ARRAYSIZE(szFull), L"%s\\%s", szDomain, szName);
                                _pCredential->Initialize(_cpus, szFull, pszSid);
                            }
                        }
                        LocalFree(pSid);
                    }
                    CoTaskMemFree(pszSid);
                }
                pUser->Release();
            }
        }
    }
    return S_OK;
}
