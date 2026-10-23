#pragma once
#include "common.h"
#include "MFS100Credential.h"

#include <vector>

class MFS100Provider : public ICredentialProvider,
                       public ICredentialProviderSetUserArray
{
public:
    // IUnknown
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv);
    IFACEMETHODIMP_(ULONG) AddRef();
    IFACEMETHODIMP_(ULONG) Release();

    // ICredentialProvider
    IFACEMETHODIMP SetUsageScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, DWORD dwFlags);
    IFACEMETHODIMP SetSerialization(const CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* pcpcs);
    IFACEMETHODIMP Advise(ICredentialProviderEvents* pcpe, UINT_PTR upAdviseContext);
    IFACEMETHODIMP UnAdvise();
    IFACEMETHODIMP GetFieldDescriptorCount(DWORD* pdwCount);
    IFACEMETHODIMP GetFieldDescriptorAt(DWORD dwIndex, CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR** ppcpfd);
    IFACEMETHODIMP GetCredentialCount(DWORD* pdwCount, DWORD* pdwDefault, BOOL* pbAutoLogonWithDefault);
    IFACEMETHODIMP GetCredentialAt(DWORD dwIndex, ICredentialProviderCredential** ppcpc);

    // ICredentialProviderSetUserArray (Windows 8, 10, 11)
    IFACEMETHODIMP SetUserArray(ICredentialProviderUserArray* users);

    MFS100Provider();

    void OnBiometricVerified(MFS100Credential* pCred);

private:
    ~MFS100Provider();
    LONG _cRef;
    CREDENTIAL_PROVIDER_USAGE_SCENARIO _cpus;
    ICredentialProviderEvents* _pEvents;
    UINT_PTR _upAdviseContext;

    BOOL _bAutoLogonReady;
    DWORD _dwAutoLogonIndex;

    std::vector<MFS100Credential*> _credentials;
};
