#include "MFS100Credential.h"

MFS100Credential::MFS100Credential() :
    _cRef(1),
    _cpus(CPUS_INVALID),
    _pEvents(NULL),
    _bBiometricVerified(FALSE),
    _bIsSelected(FALSE),
    _hBiometricThread(NULL),
    _hStopEvent(NULL)
{
    ZeroMemory(_szUsername, sizeof(_szUsername));
    ZeroMemory(_szDomain, sizeof(_szDomain));
    ZeroMemory(_szPassword, sizeof(_szPassword));
    ZeroMemory(_szUserSid, sizeof(_szUserSid));
    ZeroMemory(_szStatusText, sizeof(_szStatusText));

    StringCchCopyW(_szStatusText, ARRAYSIZE(_szStatusText), L"Touch Mantra MFS100 sensor with enrolled finger...");
}

MFS100Credential::~MFS100Credential()
{
    LogDebug(L"[MFS100Credential] Destructor called.");
    UnAdvise();

    if (_hStopEvent != NULL)
    {
        CloseHandle(_hStopEvent);
        _hStopEvent = NULL;
    }

    SecureZeroMemory(_szPassword, sizeof(_szPassword));
}

HRESULT MFS100Credential::Initialize(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, PCWSTR pszUser, PCWSTR pszSid)
{
    _cpus = cpus;

    if (pszUser != NULL && *pszUser != L'\0')
    {
        StringCchCopyW(_szUsername, ARRAYSIZE(_szUsername), pszUser);
    }
    else
    {
        // Try to get logged in / default username
        DWORD cch = ARRAYSIZE(_szUsername);
        GetUserNameW(_szUsername, &cch);
    }

    if (pszSid != NULL && *pszSid != L'\0')
    {
        StringCchCopyW(_szUserSid, ARRAYSIZE(_szUserSid), pszSid);
    }

    _hStopEvent = CreateEventW(NULL, TRUE, FALSE, NULL);

    LogDebug(L"[MFS100Credential] Initialized for User: '%s', SID: '%s'", _szUsername, _szUserSid);
    return S_OK;
}

HRESULT MFS100Credential::QueryInterface(REFIID riid, void** ppv)
{
    if (ppv == NULL) return E_POINTER;
    static const QITAB qit[] =
    {
        QITABENT(MFS100Credential, ICredentialProviderCredential2),
        QITABENT(MFS100Credential, ICredentialProviderCredential),
        { 0 }
    };
    return QISearch(this, qit, riid, ppv);
}

ULONG MFS100Credential::AddRef()
{
    return InterlockedIncrement(&_cRef);
}

ULONG MFS100Credential::Release()
{
    LONG cRef = InterlockedDecrement(&_cRef);
    if (cRef == 0)
    {
        delete this;
    }
    return cRef;
}

HRESULT MFS100Credential::Advise(ICredentialProviderCredentialEvents* pcpce)
{
    LogDebug(L"[MFS100Credential] Advise called.");
    if (_pEvents != NULL)
    {
        _pEvents->Release();
    }
    _pEvents = pcpce;
    if (_pEvents != NULL)
    {
        _pEvents->AddRef();
    }

    if (_hStopEvent != NULL)
    {
        ResetEvent(_hStopEvent);
    }

    if (_hBiometricThread == NULL)
    {
        _hBiometricThread = CreateThread(NULL, 0, BiometricThreadProc, this, 0, NULL);
    }

    return S_OK;
}

HRESULT MFS100Credential::UnAdvise()
{
    LogDebug(L"[MFS100Credential] UnAdvise called.");

    if (_hStopEvent != NULL)
    {
        SetEvent(_hStopEvent);
    }

    if (_hBiometricThread != NULL)
    {
        WaitForSingleObject(_hBiometricThread, 1500);
        CloseHandle(_hBiometricThread);
        _hBiometricThread = NULL;
    }

    if (_pEvents != NULL)
    {
        _pEvents->Release();
        _pEvents = NULL;
    }

    return S_OK;
}

HRESULT MFS100Credential::SetSelected(BOOL* pbAutoLogon)
{
    LogDebug(L"[MFS100Credential] SetSelected called.");
    _bIsSelected = TRUE;
    if (pbAutoLogon != NULL)
    {
        *pbAutoLogon = FALSE;
    }
    return S_OK;
}

HRESULT MFS100Credential::SetDeselected()
{
    LogDebug(L"[MFS100Credential] SetDeselected called.");
    _bIsSelected = FALSE;
    return S_OK;
}

HRESULT MFS100Credential::GetFieldState(DWORD dwIndex, CREDENTIAL_PROVIDER_FIELD_STATE* pcpfs, CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE* pcpfis)
{
    if (pcpfs == NULL || pcpfis == NULL) return E_POINTER;

    switch (dwIndex)
    {
    case FID_LOGO:
    case FID_LARGE_TEXT:
        *pcpfs = CPFS_DISPLAY_IN_BOTH;
        *pcpfis = CPFIS_NONE;
        return S_OK;

    case FID_SMALL_TEXT:
    case FID_USERNAME:
    case FID_SUBMIT:
        *pcpfs = CPFS_DISPLAY_IN_SELECTED_TILE;
        *pcpfis = CPFIS_NONE;
        return S_OK;

    case FID_PASSWORD:
        *pcpfs = CPFS_DISPLAY_IN_SELECTED_TILE;
        *pcpfis = CPFIS_FOCUSED;
        return S_OK;

    default:
        *pcpfs = CPFS_HIDDEN;
        *pcpfis = CPFIS_NONE;
        return S_OK;
    }
}

HRESULT MFS100Credential::GetStringValue(DWORD dwIndex, PWSTR* ppsz)
{
    if (ppsz == NULL) return E_POINTER;
    *ppsz = NULL;

    PCWSTR pszSource = L"";

    switch (dwIndex)
    {
    case FID_LARGE_TEXT:
        pszSource = L"Mantra MFS100 Biometric Login";
        break;
    case FID_SMALL_TEXT:
        pszSource = _szStatusText;
        break;
    case FID_USERNAME:
        pszSource = _szUsername;
        break;
    default:
        return E_INVALIDARG;
    }

    DWORD cch = (DWORD)wcslen(pszSource) + 1;
    PWSTR pszAlloc = (PWSTR)CoTaskMemAlloc(cch * sizeof(WCHAR));
    if (pszAlloc == NULL) return E_OUTOFMEMORY;

    StringCchCopyW(pszAlloc, cch, pszSource);
    *ppsz = pszAlloc;
    return S_OK;
}

HRESULT MFS100Credential::SetStringValue(DWORD dwIndex, PCWSTR psz)
{
    if (psz == NULL) return E_POINTER;

    switch (dwIndex)
    {
    case FID_USERNAME:
        StringCchCopyW(_szUsername, ARRAYSIZE(_szUsername), psz);
        LogDebug(L"[MFS100Credential] Username set to: '%s'", _szUsername);
        return S_OK;

    case FID_PASSWORD:
        StringCchCopyW(_szPassword, ARRAYSIZE(_szPassword), psz);
        return S_OK;

    default:
        return E_INVALIDARG;
    }
}

HRESULT MFS100Credential::GetBitmapValue(DWORD dwIndex, HBITMAP* phbmp)
{
    if (phbmp == NULL) return E_POINTER;
    *phbmp = NULL;
    return E_NOTIMPL;
}

HRESULT MFS100Credential::GetCheckboxValue(DWORD dwIndex, BOOL* pbChecked, PWSTR* ppszLabel)
{
    return E_NOTIMPL;
}

HRESULT MFS100Credential::SetCheckboxValue(DWORD dwIndex, BOOL bChecked)
{
    return E_NOTIMPL;
}

HRESULT MFS100Credential::GetSubmitButtonValue(DWORD dwIndex, DWORD* pdwAdjacentTo)
{
    if (pdwAdjacentTo == NULL) return E_POINTER;
    if (dwIndex == FID_SUBMIT)
    {
        *pdwAdjacentTo = FID_PASSWORD;
        return S_OK;
    }
    return E_INVALIDARG;
}

HRESULT MFS100Credential::GetComboBoxValueCount(DWORD dwIndex, DWORD* pcItems, DWORD* pdwSelectedItem)
{
    return E_NOTIMPL;
}

HRESULT MFS100Credential::GetComboBoxValueAt(DWORD dwIndex, DWORD dwItemIndex, PWSTR* ppszItem)
{
    return E_NOTIMPL;
}

HRESULT MFS100Credential::SetComboBoxSelectedValue(DWORD dwIndex, DWORD dwSelectedItem)
{
    return E_NOTIMPL;
}

HRESULT MFS100Credential::CommandLinkClicked(DWORD dwIndex)
{
    return E_NOTIMPL;
}

HRESULT MFS100Credential::GetUserSid(PWSTR* ppszSid)
{
    if (ppszSid == NULL) return E_POINTER;
    *ppszSid = NULL;

    if (_szUserSid[0] != L'\0')
    {
        DWORD cch = (DWORD)wcslen(_szUserSid) + 1;
        PWSTR psz = (PWSTR)CoTaskMemAlloc(cch * sizeof(WCHAR));
        if (psz != NULL)
        {
            StringCchCopyW(psz, cch, _szUserSid);
            *ppszSid = psz;
            return S_OK;
        }
    }
    return E_FAIL;
}

HRESULT MFS100Credential::ReportResult(NTSTATUS ntsStatus, NTSTATUS ntsSubstatus, PWSTR* ppszOptionalStatusText, CREDENTIAL_PROVIDER_STATUS_ICON* pcpsi)
{
    LogDebug(L"[MFS100Credential] ReportResult: Status=0x%08X, Substatus=0x%08X", ntsStatus, ntsSubstatus);
    _bBiometricVerified = FALSE;
    SecureZeroMemory(_szPassword, sizeof(_szPassword));
    return S_OK;
}

HRESULT MFS100Credential::GetSerialization(CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE* pcpgsr,
                                           CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* pcpcs,
                                           PWSTR* ppszOptionalStatusText,
                                           CREDENTIAL_PROVIDER_STATUS_ICON* pcpsi)
{
    LogDebug(L"[MFS100Credential] GetSerialization called. Verified=%d", _bBiometricVerified);

    if (pcpgsr == NULL || pcpcs == NULL) return E_POINTER;

    // Split username if domain\user or user@domain
    WCHAR szDomainPart[256] = L".";
    WCHAR szUserPart[256] = L"";

    PCWSTR pszBackslash = wcschr(_szUsername, L'\\');
    if (pszBackslash != NULL)
    {
        StringCchCopyNW(szDomainPart, ARRAYSIZE(szDomainPart), _szUsername, pszBackslash - _szUsername);
        StringCchCopyW(szUserPart, ARRAYSIZE(szUserPart), pszBackslash + 1);
    }
    else
    {
        StringCchCopyW(szUserPart, ARRAYSIZE(szUserPart), _szUsername);
    }

    // Connect to LSA to look up MSV1_0 authentication package
    HANDLE hLsa = NULL;
    NTSTATUS status = LsaConnectUntrusted(&hLsa);
    if (status != 0)
    {
        LogDebug(L"[MFS100Credential] LsaConnectUntrusted failed: 0x%08X", status);
        return HRESULT_FROM_WIN32(LsaNtStatusToWinError(status));
    }

    LSA_STRING pkgName;
    pkgName.Buffer = (PCHAR)"MSV1_0";
    pkgName.Length = (USHORT)strlen(pkgName.Buffer);
    pkgName.MaximumLength = pkgName.Length + 1;

    ULONG ulAuthPackage = 0;
    status = LsaLookupAuthenticationPackage(hLsa, &pkgName, &ulAuthPackage);
    LsaDeregisterLogonProcess(hLsa);

    if (status != 0)
    {
        LogDebug(L"[MFS100Credential] LsaLookupAuthenticationPackage failed: 0x%08X", status);
        return HRESULT_FROM_WIN32(LsaNtStatusToWinError(status));
    }

    DWORD cbDomain = (DWORD)(wcslen(szDomainPart) * sizeof(WCHAR));
    DWORD cbUser = (DWORD)(wcslen(szUserPart) * sizeof(WCHAR));
    DWORD cbPass = (DWORD)(wcslen(_szPassword) * sizeof(WCHAR));

    DWORD cbTotal = sizeof(MSV1_0_INTERACTIVE_LOGON) + cbDomain + cbUser + cbPass;
    BYTE* pBuffer = (BYTE*)CoTaskMemAlloc(cbTotal);
    if (pBuffer == NULL) return E_OUTOFMEMORY;

    ZeroMemory(pBuffer, cbTotal);

    PMSV1_0_INTERACTIVE_LOGON pLogon = (PMSV1_0_INTERACTIVE_LOGON)pBuffer;
    pLogon->MessageType = MsV1_0InteractiveLogon;

    BYTE* pData = pBuffer + sizeof(MSV1_0_INTERACTIVE_LOGON);

    pLogon->LogonDomainName.Length = (USHORT)cbDomain;
    pLogon->LogonDomainName.MaximumLength = (USHORT)cbDomain;
    pLogon->LogonDomainName.Buffer = (PWSTR)pData;
    memcpy(pData, szDomainPart, cbDomain);
    pData += cbDomain;

    pLogon->UserName.Length = (USHORT)cbUser;
    pLogon->UserName.MaximumLength = (USHORT)cbUser;
    pLogon->UserName.Buffer = (PWSTR)pData;
    memcpy(pData, szUserPart, cbUser);
    pData += cbUser;

    pLogon->Password.Length = (USHORT)cbPass;
    pLogon->Password.MaximumLength = (USHORT)cbPass;
    pLogon->Password.Buffer = (PWSTR)pData;
    memcpy(pData, _szPassword, cbPass);

    pcpcs->clsidCredentialProvider = CLSID_MFS100CredentialProvider;
    pcpcs->ulAuthenticationPackage = ulAuthPackage;
    pcpcs->cbSerialization = cbTotal;
    pcpcs->rgbSerialization = pBuffer;

    *pcpgsr = CPGSR_RETURN_CREDENTIAL_FINISHED;

    // Securely wipe transient password buffer in memory
    SecureZeroMemory(_szPassword, sizeof(_szPassword));

    LogDebug(L"[MFS100Credential] Serialization packaged successfully for '%s\\%s'.", szDomainPart, szUserPart);
    return S_OK;
}

DWORD WINAPI MFS100Credential::BiometricThreadProc(LPVOID lpParam)
{
    MFS100Credential* pThis = (MFS100Credential*)lpParam;
    if (pThis != NULL)
    {
        pThis->RunBiometricListener();
    }
    return 0;
}

void MFS100Credential::RunBiometricListener()
{
    LogDebug(L"[MFS100Credential] Biometric listener thread started.");

    while (WaitForSingleObject(_hStopEvent, 200) == WAIT_TIMEOUT)
    {
        int score = 0;
        WCHAR szMatchedUser[256] = L"";

        BOOL bMatched = ConnectAndVerifyBiometric(&score, szMatchedUser, ARRAYSIZE(szMatchedUser));
        if (bMatched)
        {
            _bBiometricVerified = TRUE;
            StringCchPrintfW(_szStatusText, ARRAYSIZE(_szStatusText),
                             L"Fingerprint Verified! (Score: %d) - User: %s", score, szMatchedUser);

            if (_szUsername[0] == L'\0')
            {
                StringCchCopyW(_szUsername, ARRAYSIZE(_szUsername), szMatchedUser);
                if (_pEvents != NULL)
                {
                    _pEvents->SetFieldString(this, FID_USERNAME, _szUsername);
                }
            }

            if (_pEvents != NULL)
            {
                _pEvents->SetFieldString(this, FID_SMALL_TEXT, _szStatusText);
            }

            // Sleep to debounce
            WaitForSingleObject(_hStopEvent, 3000);
        }
        else
        {
            // Wait briefly before next capture poll
            WaitForSingleObject(_hStopEvent, 1000);
        }
    }

    LogDebug(L"[MFS100Credential] Biometric listener thread exiting.");
}

BOOL MFS100Credential::ConnectAndVerifyBiometric(int* pScore, WCHAR* pszMatchedUser, DWORD cchMatchedUser)
{
    HANDLE hPipe = CreateFileW(
        L"\\\\.\\pipe\\MFS100Bridge",
        GENERIC_READ | GENERIC_WRITE,
        0,
        NULL,
        OPEN_EXISTING,
        0,
        NULL);

    if (hPipe == INVALID_HANDLE_VALUE)
    {
        return FALSE; // Service not running or busy
    }

    // 1. Request capture with 8000ms timeout
    const char szCaptureReq[] = "{\"cmd\":\"capture\",\"timeoutMs\":8000}\n";
    DWORD dwWritten = 0;
    if (!WriteFile(hPipe, szCaptureReq, (DWORD)strlen(szCaptureReq), &dwWritten, NULL))
    {
        CloseHandle(hPipe);
        return FALSE;
    }

    // 2. Read capture response
    char szBuffer[32768];
    DWORD dwRead = 0;
    if (!ReadFile(hPipe, szBuffer, sizeof(szBuffer) - 1, &dwRead, NULL) || dwRead == 0)
    {
        CloseHandle(hPipe);
        return FALSE;
    }
    szBuffer[dwRead] = '\0';

    // Check if capture succeeded: look for "\"quality\":" and "\"template\":"
    const char* pQual = strstr(szBuffer, "\"quality\":");
    const char* pTmpl = strstr(szBuffer, "\"template\":\"");
    if (pQual == NULL || pTmpl == NULL)
    {
        CloseHandle(hPipe);
        return FALSE;
    }

    pTmpl += 12; // skip "\"template\":\""
    const char* pTmplEnd = strchr(pTmpl, '\"');
    if (pTmplEnd == NULL)
    {
        CloseHandle(hPipe);
        return FALSE;
    }

    size_t tmplLen = pTmplEnd - pTmpl;
    char szTemplateBase64[8192];
    if (tmplLen >= sizeof(szTemplateBase64))
    {
        CloseHandle(hPipe);
        return FALSE;
    }
    memcpy(szTemplateBase64, pTmpl, tmplLen);
    szTemplateBase64[tmplLen] = '\0';

    // 3. Request 1:N identification across enrolled accounts
    char szIdentifyReq[16384];
    StringCchPrintfA(szIdentifyReq, sizeof(szIdentifyReq),
                     "{\"cmd\":\"identify_user\",\"probe\":\"%s\",\"threshold\":1400}\n",
                     szTemplateBase64);

    if (!WriteFile(hPipe, szIdentifyReq, (DWORD)strlen(szIdentifyReq), &dwWritten, NULL))
    {
        CloseHandle(hPipe);
        return FALSE;
    }

    // 4. Read identification response
    if (!ReadFile(hPipe, szBuffer, sizeof(szBuffer) - 1, &dwRead, NULL) || dwRead == 0)
    {
        CloseHandle(hPipe);
        return FALSE;
    }
    szBuffer[dwRead] = '\0';
    CloseHandle(hPipe);

    // Look for "\"matched\":true"
    if (strstr(szBuffer, "\"matched\":true") != NULL)
    {
        // Extract score
        const char* pScoreStr = strstr(szBuffer, "\"score\":");
        if (pScoreStr != NULL)
        {
            *pScore = atoi(pScoreStr + 8);
        }
        else
        {
            *pScore = 10000;
        }

        // Return current user or identified user
        StringCchCopyW(pszMatchedUser, cchMatchedUser, _szUsername[0] != L'\0' ? _szUsername : L"soumy");
        return TRUE;
    }

    return FALSE;
}
