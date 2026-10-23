#pragma once
#include "common.h"

class MFS100Credential : public ICredentialProviderCredential2
{
public:
    // IUnknown
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv);
    IFACEMETHODIMP_(ULONG) AddRef();
    IFACEMETHODIMP_(ULONG) Release();

    // ICredentialProviderCredential
    IFACEMETHODIMP Advise(ICredentialProviderCredentialEvents* pcpce);
    IFACEMETHODIMP UnAdvise();
    IFACEMETHODIMP SetSelected(BOOL* pbAutoLogon);
    IFACEMETHODIMP SetDeselected();
    IFACEMETHODIMP GetFieldState(DWORD dwIndex, CREDENTIAL_PROVIDER_FIELD_STATE* pcpfs, CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE* pcpfis);
    IFACEMETHODIMP GetStringValue(DWORD dwIndex, PWSTR* ppsz);
    IFACEMETHODIMP SetStringValue(DWORD dwIndex, PCWSTR psz);
    IFACEMETHODIMP GetBitmapValue(DWORD dwIndex, HBITMAP* phbmp);
    IFACEMETHODIMP GetCheckboxValue(DWORD dwIndex, BOOL* pbChecked, PWSTR* ppszLabel);
    IFACEMETHODIMP SetCheckboxValue(DWORD dwIndex, BOOL bChecked);
    IFACEMETHODIMP GetSubmitButtonValue(DWORD dwIndex, DWORD* pdwAdjacentTo);
    IFACEMETHODIMP GetComboBoxValueCount(DWORD dwIndex, DWORD* pcItems, DWORD* pdwSelectedItem);
    IFACEMETHODIMP GetComboBoxValueAt(DWORD dwIndex, DWORD dwItemIndex, PWSTR* ppszItem);
    IFACEMETHODIMP SetComboBoxSelectedValue(DWORD dwIndex, DWORD dwSelectedItem);
    IFACEMETHODIMP CommandLinkClicked(DWORD dwIndex);
    IFACEMETHODIMP GetSerialization(CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE* pcpgsr,
                                    CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* pcpcs,
                                    PWSTR* ppszOptionalStatusText,
                                    CREDENTIAL_PROVIDER_STATUS_ICON* pcpsi);
    IFACEMETHODIMP ReportResult(NTSTATUS ntsStatus, NTSTATUS ntsSubstatus,
                                PWSTR* ppszOptionalStatusText,
                                CREDENTIAL_PROVIDER_STATUS_ICON* pcpsi);

    // ICredentialProviderCredential2
    IFACEMETHODIMP GetUserSid(PWSTR* ppszSid);

    MFS100Credential();
    HRESULT Initialize(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, PCWSTR pszUser, PCWSTR pszSid, class MFS100Provider* pProvider);

private:
    ~MFS100Credential();
    LONG _cRef;
    CREDENTIAL_PROVIDER_USAGE_SCENARIO _cpus;
    class MFS100Provider* _pProvider;
    ICredentialProviderCredentialEvents* _pEvents;

    WCHAR _szUsername[256];
    WCHAR _szDomain[256];
    WCHAR _szPassword[256];
    WCHAR _szUserSid[128];
    WCHAR _szStatusText[256];

    BOOL _bBiometricVerified;
    BOOL _bIsSelected;

    HANDLE _hBiometricThread;
    HANDLE _hStopEvent;
    HANDLE _hActivePipe;

    void StartBiometricListener();
    void StopBiometricListener();

    static DWORD WINAPI BiometricThreadProc(LPVOID lpParam);
    void RunBiometricListener();

    BOOL ConnectAndVerifyBiometric(int* pScore, WCHAR* pszMatchedUser, DWORD cchMatchedUser);
};
