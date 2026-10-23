#include "MFS100Credential.h"
#include "MFS100Provider.h"

MFS100Credential::MFS100Credential() :
    _cRef(1),
    _cpus(CPUS_INVALID),
    _pProvider(NULL),
    _pEvents(NULL),
    _bBiometricVerified(FALSE),
    _bIsSelected(FALSE),
    _hBiometricThread(NULL),
    _hStopEvent(NULL),
    _hActivePipe(INVALID_HANDLE_VALUE)
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

HRESULT MFS100Credential::Initialize(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, PCWSTR pszUser, PCWSTR pszSid, MFS100Provider* pProvider)
{
    _cpus = cpus;
    _pProvider = pProvider;

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

    // Do NOT start biometric thread here!
    // Biometric hardware capture must only run when this tile is actively selected by the user.
    return S_OK;
}

HRESULT MFS100Credential::UnAdvise()
{
    LogDebug(L"[MFS100Credential] UnAdvise called.");

    StopBiometricListener();

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

    // Start biometric listener now that user has focused/selected the Mantra tile
    StartBiometricListener();

    if (pbAutoLogon != NULL)
    {
        if (_bBiometricVerified && _szPassword[0] != L'\0')
        {
            *pbAutoLogon = TRUE;
            LogDebug(L"[MFS100Credential] SetSelected: _bBiometricVerified with password, returning pbAutoLogon=TRUE");
        }
        else
        {
            *pbAutoLogon = FALSE;
        }
    }
    return S_OK;
}

HRESULT MFS100Credential::SetDeselected()
{
    LogDebug(L"[MFS100Credential] SetDeselected called.");
    _bIsSelected = FALSE;

    // Immediately abort sensor capture so optical LED turns off when user switches to PIN or another tile!
    StopBiometricListener();

    return S_OK;
}

void MFS100Credential::StartBiometricListener()
{
    LogDebug(L"[MFS100Credential] StartBiometricListener called.");
    if (_hBiometricThread != NULL) return;

    if (_hStopEvent != NULL)
    {
        ResetEvent(_hStopEvent);
    }
    _bBiometricVerified = FALSE;
    _hBiometricThread = CreateThread(NULL, 0, BiometricThreadProc, this, 0, NULL);
}

void MFS100Credential::StopBiometricListener()
{
    LogDebug(L"[MFS100Credential] StopBiometricListener called.");
    if (_hStopEvent != NULL)
    {
        SetEvent(_hStopEvent);
    }

    // Immediately abort active pipe handle and cancel pending I/O so ReadFile unblocks instantly!
    if (_hActivePipe != INVALID_HANDLE_VALUE && _hActivePipe != NULL)
    {
        HANDLE hPipeToClose = _hActivePipe;
        _hActivePipe = INVALID_HANDLE_VALUE;
        CancelIoEx(hPipeToClose, NULL);
        CloseHandle(hPipeToClose);
        LogDebug(L"[MFS100Credential] Aborted active pipe connection to stop sensor capture.");
    }

    if (_hBiometricThread != NULL)
    {
        WaitForSingleObject(_hBiometricThread, 1500);
        CloseHandle(_hBiometricThread);
        _hBiometricThread = NULL;
    }
}

HRESULT MFS100Credential::GetFieldState(DWORD dwIndex, CREDENTIAL_PROVIDER_FIELD_STATE* pcpfs, CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE* pcpfis)
{
    if (pcpfs == NULL || pcpfis == NULL) return E_POINTER;

    LogDebug(L"[MFS100Credential] GetFieldState called for FieldID=%d", dwIndex);
    switch (dwIndex)
    {
    case FID_LOGO:
        *pcpfs = CPFS_DISPLAY_IN_BOTH;
        *pcpfis = CPFIS_NONE;
        return S_OK;

    case FID_LARGE_TEXT:
    case FID_SMALL_TEXT:
    case FID_SUBMIT:
        *pcpfs = CPFS_DISPLAY_IN_SELECTED_TILE;
        *pcpfis = CPFIS_NONE;
        return S_OK;

    case FID_USERNAME:
        // When unlocking or when bound to a specific user SID, NEVER show an editable username field!
        if (_cpus == CPUS_UNLOCK_WORKSTATION || _szUserSid[0] != L'\0')
        {
            *pcpfs = CPFS_HIDDEN;
            *pcpfis = CPFIS_NONE;
        }
        else
        {
            *pcpfs = CPFS_DISPLAY_IN_SELECTED_TILE;
            *pcpfis = CPFIS_NONE;
        }
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
    LogDebug(L"[MFS100Credential] GetStringValue called for FieldID=%d", dwIndex);
    if (ppsz == NULL) return E_POINTER;
    *ppsz = NULL;

    PCWSTR pszSource = L"";

    switch (dwIndex)
    {
    case FID_LOGO:
        pszSource = L"";
        break;
    case FID_LARGE_TEXT:
        pszSource = L"Mantra MFS100 Biometric Login";
        break;
    case FID_SMALL_TEXT:
        pszSource = _szStatusText;
        break;
    case FID_USERNAME:
        pszSource = _szUsername;
        break;
    case FID_PASSWORD:
        pszSource = _szPassword;
        break;
    case FID_SUBMIT:
        pszSource = L"Logon";
        break;
    default:
        pszSource = L"";
        break;
    }

    HRESULT hr = SHStrDupW(pszSource, ppsz);
    if (SUCCEEDED(hr))
    {
        LogDebug(L"[MFS100Credential] GetStringValue for FieldID=%d returned: '%s'", dwIndex, *ppsz);
    }
    else
    {
        LogDebug(L"[MFS100Credential] GetStringValue for FieldID=%d failed: 0x%08X", dwIndex, hr);
    }
    return hr;
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
        return S_OK;
    }
}

static HBITMAP CreateBiometricTileBitmap()
{
    const int w = 96;
    const int h = 96;
    BITMAPINFO bmi = { 0 };
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = w;
    bmi.bmiHeader.biHeight = -h; // top-down
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    void* pBits = NULL;
    HDC hdcMem = CreateCompatibleDC(NULL);
    HBITMAP hbmp = CreateDIBSection(hdcMem, &bmi, DIB_RGB_COLORS, &pBits, NULL, 0);
    if (!hbmp)
    {
        if (hdcMem) DeleteDC(hdcMem);
        return NULL;
    }
    HBITMAP hbmpOld = (HBITMAP)SelectObject(hdcMem, hbmp);

    // Background: dark blue/slate
    HBRUSH hbrBg = CreateSolidBrush(RGB(24, 28, 36));
    RECT rc = { 0, 0, w, h };
    FillRect(hdcMem, &rc, hbrBg);
    DeleteObject(hbrBg);

    // Cyan/blue biometric scanner outline
    HPEN hPen = CreatePen(PS_SOLID, 4, RGB(0, 162, 237));
    HPEN hPenOld = (HPEN)SelectObject(hdcMem, hPen);
    HBRUSH hbrHollow = (HBRUSH)GetStockObject(HOLLOW_BRUSH);
    SelectObject(hdcMem, hbrHollow);

    RoundRect(hdcMem, 12, 12, 84, 84, 24, 24);

    // Fingerprint ridges inside
    Arc(hdcMem, 24, 20, 72, 76, 28, 48, 68, 48);
    Arc(hdcMem, 32, 28, 64, 68, 36, 48, 60, 48);
    Arc(hdcMem, 40, 36, 56, 60, 42, 48, 54, 48);

    SelectObject(hdcMem, hPenOld);
    DeleteObject(hPen);
    SelectObject(hdcMem, hbmpOld);
    DeleteDC(hdcMem);

    return hbmp;
}

HRESULT MFS100Credential::GetBitmapValue(DWORD dwIndex, HBITMAP* phbmp)
{
    LogDebug(L"[MFS100Credential] GetBitmapValue called for FieldID=%d", dwIndex);
    if (phbmp == NULL) return E_POINTER;
    *phbmp = NULL;

    if (dwIndex == FID_LOGO)
    {
        *phbmp = CreateBiometricTileBitmap();
        LogDebug(L"[MFS100Credential] GetBitmapValue created bitmap handle: 0x%p", *phbmp);
        if (*phbmp != NULL) return S_OK;
    }
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
    LogDebug(L"[MFS100Credential] GetSubmitButtonValue called for FieldID=%d", dwIndex);
    if (pdwAdjacentTo == NULL) return E_POINTER;
    if (dwIndex == FID_SUBMIT)
    {
        *pdwAdjacentTo = FID_PASSWORD;
        return S_OK;
    }
    return E_NOTIMPL;
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

    PCWSTR pszTargetSid = (_szUserSid[0] != L'\0') ? _szUserSid : L"S-1-5-21-3272715156-751205776-2342815491-1001";
    HRESULT hr = SHStrDupW(pszTargetSid, ppszSid);
    LogDebug(L"[MFS100Credential] GetUserSid: '%s' (hr=0x%08X)", pszTargetSid, hr);
    return hr;
}

static void SavePasswordToVault(PCWSTR pszSid, PCWSTR pszPassword)
{
    if (pszSid == NULL || pszSid[0] == L'\0' || pszPassword == NULL || pszPassword[0] == L'\0')
        return;

    HANDLE hPipe = CreateFileW(L"\\\\.\\pipe\\MFS100Bridge", GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING, 0, NULL);
    if (hPipe == INVALID_HANDLE_VALUE) return;

    char szSidA[128];
    char szPassA[256];
    WideCharToMultiByte(CP_UTF8, 0, pszSid, -1, szSidA, sizeof(szSidA), NULL, NULL);
    WideCharToMultiByte(CP_UTF8, 0, pszPassword, -1, szPassA, sizeof(szPassA), NULL, NULL);

    char szReq[1024];
    StringCchPrintfA(szReq, sizeof(szReq), "{\"cmd\":\"save_vault_credential\",\"sid\":\"%s\",\"password\":\"%s\"}\n", szSidA, szPassA);
    DWORD dwWritten = 0;
    WriteFile(hPipe, szReq, (DWORD)strlen(szReq), &dwWritten, NULL);
    CloseHandle(hPipe);
    LogDebug(L"[MFS100Credential] Saved password to vault for SID '%s'.", pszSid);
}

HRESULT MFS100Credential::ReportResult(NTSTATUS ntsStatus, NTSTATUS ntsSubstatus, PWSTR* ppszOptionalStatusText, CREDENTIAL_PROVIDER_STATUS_ICON* pcpsi)
{
    LogDebug(L"[MFS100Credential] ReportResult: Status=0x%08X, Substatus=0x%08X", ntsStatus, ntsSubstatus);
    if (ntsStatus == 0 && _szPassword[0] != L'\0' && _szUserSid[0] != L'\0')
    {
        LogDebug(L"[MFS100Credential] Logon succeeded! Automatically caching password into DPAPI vault for one-touch unlock...");
        SavePasswordToVault(_szUserSid, _szPassword);
    }
    _bBiometricVerified = FALSE;
    SecureZeroMemory(_szPassword, sizeof(_szPassword));
    return S_OK;
}

static HRESULT ProtectPassword(PCWSTR pszPlain, PWSTR* ppszProtected)
{
    if (pszPlain == NULL || pszPlain[0] == L'\0') return E_INVALIDARG;

    DWORD cchPlain = (DWORD)wcslen(pszPlain) + 1;
    DWORD cchNeeded = 0;

    // First call to determine required buffer size
    CredProtectW(FALSE, (PWSTR)pszPlain, cchPlain, NULL, &cchNeeded, NULL);
    if (cchNeeded == 0)
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    PWSTR pszProtected = (PWSTR)CoTaskMemAlloc(cchNeeded * sizeof(WCHAR));
    if (pszProtected == NULL) return E_OUTOFMEMORY;

    if (!CredProtectW(FALSE, (PWSTR)pszPlain, cchPlain, pszProtected, &cchNeeded, NULL))
    {
        DWORD dwErr = GetLastError();
        CoTaskMemFree(pszProtected);
        return HRESULT_FROM_WIN32(dwErr);
    }

    *ppszProtected = pszProtected;
    return S_OK;
}

static HRESULT RetrieveNegotiateAuthPackage(ULONG* pulAuthPackage)
{
    if (pulAuthPackage == NULL) return E_POINTER;

    HANDLE hLsa = NULL;
    NTSTATUS status = LsaConnectUntrusted(&hLsa);
    if (status != 0)
    {
        LogDebug(L"[MFS100Credential] LsaConnectUntrusted failed: 0x%08X", status);
        return HRESULT_FROM_WIN32(LsaNtStatusToWinError(status));
    }

    LSA_STRING pkgName;
    pkgName.Buffer = (PCHAR)NEGOSSP_NAME_A;
    pkgName.Length = (USHORT)strlen(pkgName.Buffer);
    pkgName.MaximumLength = pkgName.Length + 1;

    status = LsaLookupAuthenticationPackage(hLsa, &pkgName, pulAuthPackage);
    LsaDeregisterLogonProcess(hLsa);

    if (status != 0)
    {
        LogDebug(L"[MFS100Credential] LsaLookupAuthenticationPackage (Negotiate) failed: 0x%08X", status);
        return HRESULT_FROM_WIN32(LsaNtStatusToWinError(status));
    }

    LogDebug(L"[MFS100Credential] Negotiate AuthPackage resolved: %lu", *pulAuthPackage);
    return S_OK;
}

HRESULT MFS100Credential::GetSerialization(CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE* pcpgsr,
                                           CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* pcpcs,
                                           PWSTR* ppszOptionalStatusText,
                                           CREDENTIAL_PROVIDER_STATUS_ICON* pcpsi)
{
    LogDebug(L"[MFS100Credential] GetSerialization called. Verified=%d", _bBiometricVerified);

    if (pcpgsr == NULL || pcpcs == NULL) return E_POINTER;

    // 1. Resolve domain and username parts
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

    // 2. Protect the password using CredProtectW
    PWSTR pszProtectedPassword = NULL;
    HRESULT hr = ProtectPassword(_szPassword, &pszProtectedPassword);
    if (FAILED(hr))
    {
        LogDebug(L"[MFS100Credential] ProtectPassword failed: 0x%08X", hr);
        return hr;
    }

    // 3. Resolve Negotiate authentication package
    ULONG ulAuthPackage = 0;
    hr = RetrieveNegotiateAuthPackage(&ulAuthPackage);
    if (FAILED(hr))
    {
        CoTaskMemFree(pszProtectedPassword);
        return hr;
    }

    // 4. Calculate total serialization size:
    // Structure + Domain string + Username string + Protected password string
    DWORD cbDomain = (DWORD)(wcslen(szDomainPart) * sizeof(WCHAR));
    DWORD cbUser   = (DWORD)(wcslen(szUserPart) * sizeof(WCHAR));
    DWORD cbPass   = (DWORD)(wcslen(pszProtectedPassword) * sizeof(WCHAR));

    DWORD cbTotal = sizeof(KERB_INTERACTIVE_UNLOCK_LOGON) + cbDomain + cbUser + cbPass;
    KERB_INTERACTIVE_UNLOCK_LOGON* pLogon = (KERB_INTERACTIVE_UNLOCK_LOGON*)CoTaskMemAlloc(cbTotal);
    if (pLogon == NULL)
    {
        CoTaskMemFree(pszProtectedPassword);
        return E_OUTOFMEMORY;
    }

    ZeroMemory(pLogon, cbTotal);

    // MessageType based on usage scenario
    if (_cpus == CPUS_UNLOCK_WORKSTATION || _cpus == CPUS_CREDUI)
    {
        pLogon->Logon.MessageType = KerbWorkstationUnlockLogon;
    }
    else
    {
        pLogon->Logon.MessageType = KerbInteractiveLogon;
    }

    // Position pBuffer right after the KERB_INTERACTIVE_UNLOCK_LOGON structure
    BYTE* pBuffer = (BYTE*)pLogon + sizeof(KERB_INTERACTIVE_UNLOCK_LOGON);

    // Domain (Offset relative to start of pLogon)
    pLogon->Logon.LogonDomainName.Length = (USHORT)cbDomain;
    pLogon->Logon.LogonDomainName.MaximumLength = (USHORT)cbDomain;
    pLogon->Logon.LogonDomainName.Buffer = (PWSTR)(pBuffer - (BYTE*)pLogon);
    memcpy(pBuffer, szDomainPart, cbDomain);
    pBuffer += cbDomain;

    // Username (Offset relative to start of pLogon)
    pLogon->Logon.UserName.Length = (USHORT)cbUser;
    pLogon->Logon.UserName.MaximumLength = (USHORT)cbUser;
    pLogon->Logon.UserName.Buffer = (PWSTR)(pBuffer - (BYTE*)pLogon);
    memcpy(pBuffer, szUserPart, cbUser);
    pBuffer += cbUser;

    // Protected Password (Offset relative to start of pLogon)
    pLogon->Logon.Password.Length = (USHORT)cbPass;
    pLogon->Logon.Password.MaximumLength = (USHORT)cbPass;
    pLogon->Logon.Password.Buffer = (PWSTR)(pBuffer - (BYTE*)pLogon);
    memcpy(pBuffer, pszProtectedPassword, cbPass);

    // Clean up temporary protected password copy
    SecureZeroMemory(pszProtectedPassword, cbPass);
    CoTaskMemFree(pszProtectedPassword);

    // 5. Fill output serialization structure
    pcpcs->clsidCredentialProvider = CLSID_MFS100CredentialProvider;
    pcpcs->ulAuthenticationPackage = ulAuthPackage;
    pcpcs->cbSerialization = cbTotal;
    pcpcs->rgbSerialization = (BYTE*)pLogon;

    *pcpgsr = CPGSR_RETURN_CREDENTIAL_FINISHED;

    LogDebug(L"[MFS100Credential] Serialization packaged successfully for '%s\\%s' (Scenario=%d, TotalBytes=%lu).",
             szDomainPart, szUserPart, _cpus, cbTotal);
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
        if (_bBiometricVerified)
        {
            WaitForSingleObject(_hStopEvent, 1000);
            continue;
        }

        int score = 0;
        WCHAR szMatchedUser[256] = L"";

        BOOL bMatched = ConnectAndVerifyBiometric(&score, szMatchedUser, ARRAYSIZE(szMatchedUser));
        if (bMatched)
        {
            _bBiometricVerified = TRUE;
            LogDebug(L"[MFS100Credential] Fingerprint verified! Score=%d, User=%s, HasPassword=%d",
                     score, szMatchedUser, (_szPassword[0] != L'\0'));

            if (_szUsername[0] == L'\0')
            {
                StringCchCopyW(_szUsername, ARRAYSIZE(_szUsername), szMatchedUser);
                if (_pEvents != NULL)
                {
                    _pEvents->SetFieldString(this, FID_USERNAME, _szUsername);
                }
            }

            if (_szPassword[0] != L'\0')
            {
                StringCchPrintfW(_szStatusText, ARRAYSIZE(_szStatusText),
                                 L"Fingerprint Verified! (Score: %d) - Unlocking Windows...", score);
                if (_pEvents != NULL)
                {
                    _pEvents->SetFieldString(this, FID_SMALL_TEXT, _szStatusText);
                    _pEvents->SetFieldString(this, FID_PASSWORD, _szPassword);
                }

                // Trigger immediate auto-logon submission!
                if (_pProvider != NULL)
                {
                    _pProvider->OnBiometricVerified(this);
                }
            }
            else
            {
                StringCchPrintfW(_szStatusText, ARRAYSIZE(_szStatusText),
                                 L"Fingerprint Verified! (Score: %d) - Enter password to unlock", score);
                if (_pEvents != NULL)
                {
                    _pEvents->SetFieldString(this, FID_SMALL_TEXT, _szStatusText);
                }
            }

            // CRITICAL: Stop the scanning loop so the scanner optical LED turns off and never scans repeatedly!
            while (_bBiometricVerified && WaitForSingleObject(_hStopEvent, 500) == WAIT_TIMEOUT)
            {
                // Idle while waiting for logon to finish or workstation to unlock
            }
        }
        else
        {
            // Wait briefly before next capture poll
            WaitForSingleObject(_hStopEvent, 1000);
        }
    }

    LogDebug(L"[MFS100Credential] Biometric listener thread exiting.");
}

static BOOL ReadJsonLineFromPipe(HANDLE hPipe, char* pBuffer, DWORD maxBytes)
{
    if (hPipe == INVALID_HANDLE_VALUE || hPipe == NULL || pBuffer == NULL || maxBytes == 0)
        return FALSE;

    for (int retry = 0; retry < 50; retry++)
    {
        DWORD totalRead = 0;
        while (totalRead + 1 < maxBytes)
        {
            char ch = 0;
            DWORD bytesRead = 0;
            if (!ReadFile(hPipe, &ch, 1, &bytesRead, NULL) || bytesRead == 0)
            {
                if (totalRead > 0) break;
                return FALSE;
            }
            if (ch == '\n')
            {
                if (totalRead > 0) break;
                continue;
            }
            if (ch != '\r')
            {
                pBuffer[totalRead++] = ch;
            }
        }
        pBuffer[totalRead] = '\0';

        // Trim trailing whitespace
        while (totalRead > 0 && (pBuffer[totalRead - 1] == ' ' || pBuffer[totalRead - 1] == '\t'))
        {
            pBuffer[--totalRead] = '\0';
        }

        if (totalRead > 0 && pBuffer[0] == '{' && pBuffer[totalRead - 1] == '}')
        {
            return TRUE;
        }

        if (totalRead > 0)
        {
            LogDebug(L"[MFS100Credential] Skipped chatter line from pipe: %hs", pBuffer);
        }
        else
        {
            return FALSE;
        }
    }
    return FALSE;
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

    _hActivePipe = hPipe;
    LogDebug(L"[MFS100Credential] Connected to bridge pipe. Requesting capture...");

    // 1. Request capture with 8000ms timeout
    const char szCaptureReq[] = "{\"cmd\":\"capture\",\"timeoutMs\":8000}\n";
    DWORD dwWritten = 0;
    if (!WriteFile(hPipe, szCaptureReq, (DWORD)strlen(szCaptureReq), &dwWritten, NULL))
    {
        LogDebug(L"[MFS100Credential] WriteFile capture request failed: %lu", GetLastError());
        _hActivePipe = INVALID_HANDLE_VALUE;
        CloseHandle(hPipe);
        return FALSE;
    }

    // 2. Read capture response (filtering any native SDK stdout chatter)
    char szBuffer[32768];
    if (!ReadJsonLineFromPipe(hPipe, szBuffer, sizeof(szBuffer)))
    {
        LogDebug(L"[MFS100Credential] ReadJsonLineFromPipe capture response failed or pipe disconnected: %lu", GetLastError());
        _hActivePipe = INVALID_HANDLE_VALUE;
        CloseHandle(hPipe);
        return FALSE;
    }
    LogDebug(L"[MFS100Credential] Capture response received: %hs", szBuffer);

    // Check if capture succeeded: look for "\"quality\":" and either "\"isoTemplate\":\"" or "\"template\":\""
    const char* pQual = strstr(szBuffer, "\"quality\":");
    const char* pTmpl = strstr(szBuffer, "\"isoTemplate\":\"");
    if (pTmpl != NULL)
    {
        pTmpl += 15; // skip "\"isoTemplate\":\""
    }
    else
    {
        pTmpl = strstr(szBuffer, "\"template\":\"");
        if (pTmpl != NULL)
        {
            pTmpl += 12; // skip "\"template\":\""
        }
    }

    if (pQual == NULL || pTmpl == NULL)
    {
        LogDebug(L"[MFS100Credential] Capture returned no template or error.");
        _hActivePipe = INVALID_HANDLE_VALUE;
        CloseHandle(hPipe);
        return FALSE;
    }

    const char* pTmplEnd = strchr(pTmpl, '\"');
    if (pTmplEnd == NULL)
    {
        _hActivePipe = INVALID_HANDLE_VALUE;
        CloseHandle(hPipe);
        return FALSE;
    }

    size_t tmplLen = pTmplEnd - pTmpl;
    char szTemplateBase64[8192];
    if (tmplLen >= sizeof(szTemplateBase64))
    {
        _hActivePipe = INVALID_HANDLE_VALUE;
        CloseHandle(hPipe);
        return FALSE;
    }
    memcpy(szTemplateBase64, pTmpl, tmplLen);
    szTemplateBase64[tmplLen] = '\0';

    // 3. Request 1:N identification across enrolled accounts (threshold 500)
    char szIdentifyReq[16384];
    StringCchPrintfA(szIdentifyReq, sizeof(szIdentifyReq),
                     "{\"cmd\":\"identify_user\",\"probe\":\"%s\",\"threshold\":500}\n",
                     szTemplateBase64);

    LogDebug(L"[MFS100Credential] Requesting 1:N biometric identification against enrolled templates (threshold=500)...");
    if (!WriteFile(hPipe, szIdentifyReq, (DWORD)strlen(szIdentifyReq), &dwWritten, NULL))
    {
        LogDebug(L"[MFS100Credential] WriteFile identify request failed: %lu", GetLastError());
        _hActivePipe = INVALID_HANDLE_VALUE;
        CloseHandle(hPipe);
        return FALSE;
    }

    // 4. Read identification response
    if (!ReadJsonLineFromPipe(hPipe, szBuffer, sizeof(szBuffer)))
    {
        LogDebug(L"[MFS100Credential] ReadJsonLineFromPipe identify response failed: %lu", GetLastError());
        _hActivePipe = INVALID_HANDLE_VALUE;
        CloseHandle(hPipe);
        return FALSE;
    }
    _hActivePipe = INVALID_HANDLE_VALUE;
    CloseHandle(hPipe);

    LogDebug(L"[MFS100Credential] Identify response received: %hs", szBuffer);

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

        // Extract unsealed password if vault released it
        if (strstr(szBuffer, "\"hasPassword\":true") != NULL)
        {
            const char* pPass = strstr(szBuffer, "\"password\":\"");
            if (pPass != NULL)
            {
                pPass += 12; // skip "\"password\":\""
                const char* pPassEnd = strchr(pPass, '\"');
                if (pPassEnd != NULL && pPassEnd > pPass)
                {
                    char szAnsiPass[256];
                    size_t passLen = pPassEnd - pPass;
                    if (passLen < sizeof(szAnsiPass))
                    {
                        memcpy(szAnsiPass, pPass, passLen);
                        szAnsiPass[passLen] = '\0';
                        MultiByteToWideChar(CP_UTF8, 0, szAnsiPass, -1, _szPassword, ARRAYSIZE(_szPassword));
                        LogDebug(L"[MFS100Credential] Vault password successfully unsealed for user.");
                    }
                }
            }
        }

        // Return current user or identified user
        StringCchCopyW(pszMatchedUser, cchMatchedUser, _szUsername[0] != L'\0' ? _szUsername : L"soumy");
        return TRUE;
    }
    else
    {
        LogDebug(L"[MFS100Credential] Fingerprint did not match any enrolled user or score below threshold.");
    }

    return FALSE;
}
