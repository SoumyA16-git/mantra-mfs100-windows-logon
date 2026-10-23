#include "MFS100Provider.h"
#include <sddl.h>

static const CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR s_rgFieldDescriptors[] =
{
    { FID_LOGO,       CPFT_TILE_IMAGE,    (PWSTR)L"MFS100 Logo",          CPFG_CREDENTIAL_PROVIDER_LOGO },
    { FID_LARGE_TEXT, CPFT_LARGE_TEXT,    (PWSTR)L"MFS100 Biometric Login", CPFG_CREDENTIAL_PROVIDER_LABEL },
    { FID_SMALL_TEXT, CPFT_SMALL_TEXT,    (PWSTR)L"Biometric Status",     GUID_NULL },
    { FID_USERNAME,   CPFT_EDIT_TEXT,     (PWSTR)L"Username",             CPFG_LOGON_USERNAME },
    { FID_PASSWORD,   CPFT_PASSWORD_TEXT, (PWSTR)L"Password",             CPFG_LOGON_PASSWORD },
    { FID_SUBMIT,     CPFT_SUBMIT_BUTTON, (PWSTR)L"Logon",                GUID_NULL }
};

MFS100Provider::MFS100Provider() :
    _cRef(1),
    _cpus(CPUS_INVALID),
    _pEvents(NULL),
    _upAdviseContext(0),
    _bAutoLogonReady(FALSE),
    _dwAutoLogonIndex(0)
{
    LogDebug(L"[MFS100Provider] Constructor called.");
}

void MFS100Provider::OnBiometricVerified(MFS100Credential* pCred)
{
    LogDebug(L"[MFS100Provider] OnBiometricVerified called.");
    _dwAutoLogonIndex = 0;
    for (size_t i = 0; i < _credentials.size(); i++)
    {
        if (_credentials[i] == pCred)
        {
            _dwAutoLogonIndex = (DWORD)i;
            break;
        }
    }
    _bAutoLogonReady = TRUE;

    if (_pEvents != NULL)
    {
        LogDebug(L"[MFS100Provider] Triggering CredentialsChanged for auto-logon on index %d", _dwAutoLogonIndex);
        _pEvents->CredentialsChanged(_upAdviseContext);
    }
}

MFS100Provider::~MFS100Provider()
{
    LogDebug(L"[MFS100Provider] Destructor called.");
    for (size_t i = 0; i < _credentials.size(); i++)
    {
        if (_credentials[i] != NULL)
        {
            _credentials[i]->Release();
        }
    }
    _credentials.clear();

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
    LogDebug(L"[MFS100Provider] GetFieldDescriptorCount: %d", *pdwCount);
    return S_OK;
}

HRESULT MFS100Provider::GetFieldDescriptorAt(DWORD dwIndex, CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR** ppcpfd)
{
    LogDebug(L"[MFS100Provider] GetFieldDescriptorAt called for index %d", dwIndex);
    if (ppcpfd == NULL) return E_POINTER;
    if (dwIndex >= ARRAYSIZE(s_rgFieldDescriptors)) return E_INVALIDARG;

    CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR* pcpfd = (CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR*)CoTaskMemAlloc(sizeof(CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR));
    if (pcpfd == NULL) return E_OUTOFMEMORY;

    pcpfd->dwFieldID = s_rgFieldDescriptors[dwIndex].dwFieldID;
    pcpfd->cpft = s_rgFieldDescriptors[dwIndex].cpft;
    pcpfd->guidFieldType = s_rgFieldDescriptors[dwIndex].guidFieldType;

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

    // Fallback: if SetUserArray was not called or yielded no users, provide a default credential
    if (_credentials.empty())
    {
        MFS100Credential* pCred = new MFS100Credential();
        if (pCred != NULL)
        {
            pCred->Initialize(_cpus, L"babu_vm\\soumy", L"S-1-5-21-3272715156-751205776-2342815491-1001", this);
            _credentials.push_back(pCred);
        }
    }

    *pdwCount = (DWORD)_credentials.size();

    if (_bAutoLogonReady)
    {
        *pdwDefault = _dwAutoLogonIndex;
        *pbAutoLogonWithDefault = TRUE;
        _bAutoLogonReady = FALSE;
        LogDebug(L"[MFS100Provider] GetCredentialCount: Auto-logon requested for default credential %d", *pdwDefault);
    }
    else
    {
        *pdwDefault = CREDENTIAL_PROVIDER_NO_DEFAULT;
        *pbAutoLogonWithDefault = FALSE;
        LogDebug(L"[MFS100Provider] GetCredentialCount: %d, pdwDefault: CREDENTIAL_PROVIDER_NO_DEFAULT", *pdwCount);
    }

    return S_OK;
}

HRESULT MFS100Provider::GetCredentialAt(DWORD dwIndex, ICredentialProviderCredential** ppcpc)
{
    LogDebug(L"[MFS100Provider] GetCredentialAt called for index %d (total: %d)", dwIndex, _credentials.size());
    if (ppcpc == NULL) return E_POINTER;
    if (dwIndex >= _credentials.size() || _credentials[dwIndex] == NULL) return E_INVALIDARG;

    *ppcpc = _credentials[dwIndex];
    (*ppcpc)->AddRef();
    return S_OK;
}

HRESULT MFS100Provider::SetUserArray(ICredentialProviderUserArray* users)
{
    LogDebug(L"[MFS100Provider] SetUserArray called.");

    // Clean up previous credentials
    for (size_t i = 0; i < _credentials.size(); i++)
    {
        if (_credentials[i] != NULL)
        {
            _credentials[i]->Release();
        }
    }
    _credentials.clear();

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
                    WCHAR szName[256] = L"";
                    WCHAR szDomain[256] = L"";
                    DWORD cchName = ARRAYSIZE(szName);
                    DWORD cchDomain = ARRAYSIZE(szDomain);
                    SID_NAME_USE snu;

                    PSID pSid = NULL;
                    if (ConvertStringSidToSidW(pszSid, &pSid) && pSid != NULL)
                    {
                        LookupAccountSidW(NULL, pSid, szName, &cchName, szDomain, &cchDomain, &snu);
                        LocalFree(pSid);
                    }

                    WCHAR szFull[512] = L"";
                    if (szName[0] != L'\0')
                    {
                        if (szDomain[0] != L'\0')
                            StringCchPrintfW(szFull, ARRAYSIZE(szFull), L"%s\\%s", szDomain, szName);
                        else
                            StringCchCopyW(szFull, ARRAYSIZE(szFull), szName);
                    }
                    else
                    {
                        StringCchCopyW(szFull, ARRAYSIZE(szFull), L"babu_vm\\soumy");
                    }

                    LogDebug(L"[MFS100Provider] User[%d]: Full='%s', SID='%s'", i, szFull, pszSid);

                    MFS100Credential* pCred = new MFS100Credential();
                    if (pCred != NULL)
                    {
                        pCred->Initialize(_cpus, szFull, pszSid, this);
                        _credentials.push_back(pCred);
                        LogDebug(L"[MFS100Provider] Created credential for '%s' (SID: '%s')", szFull, pszSid);
                    }

                    CoTaskMemFree(pszSid);
                }
                pUser->Release();
            }
        }
    }

    if (_credentials.empty())
    {
        LogDebug(L"[MFS100Provider] Fallback: creating enrolled user credential");
        MFS100Credential* pCred = new MFS100Credential();
        if (pCred != NULL)
        {
            pCred->Initialize(_cpus, L"babu_vm\\soumy", L"S-1-5-21-3272715156-751205776-2342815491-1001", this);
            _credentials.push_back(pCred);
        }
    }

    return S_OK;
}
