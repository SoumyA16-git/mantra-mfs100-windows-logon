#include "MFS100EngineAdapter.h"
#include <winbio_adapter.h>
#include <wincrypt.h>
#include <stdlib.h>

// Forward declarations
static HRESULT WINAPI EngineAdapterAttach(PWINBIO_PIPELINE Pipeline);
static HRESULT WINAPI EngineAdapterDetach(PWINBIO_PIPELINE Pipeline);
static HRESULT WINAPI EngineAdapterClearContext(PWINBIO_PIPELINE Pipeline);
static HRESULT WINAPI EngineAdapterQueryPreferredFormat(PWINBIO_PIPELINE Pipeline, PWINBIO_REGISTERED_FORMAT StandardFormat, PWINBIO_UUID VendorFormat);
static HRESULT WINAPI EngineAdapterQueryIndexVectorSize(PWINBIO_PIPELINE Pipeline, PSIZE_T IndexElementCount);
static HRESULT WINAPI EngineAdapterQueryHashAlgorithms(PWINBIO_PIPELINE Pipeline, PSIZE_T AlgorithmCount, PSIZE_T AlgorithmBufferSize, PUCHAR *AlgorithmBuffer);
static HRESULT WINAPI EngineAdapterSetHashAlgorithm(PWINBIO_PIPELINE Pipeline, SIZE_T AlgorithmBufferSize, PUCHAR AlgorithmBuffer);
static HRESULT WINAPI EngineAdapterQuerySampleHint(PWINBIO_PIPELINE Pipeline, PSIZE_T SampleHint);
static HRESULT WINAPI EngineAdapterAcceptSampleData(PWINBIO_PIPELINE Pipeline, PWINBIO_BIR SampleBuffer, SIZE_T SampleSize, WINBIO_BIR_PURPOSE Purpose, PWINBIO_REJECT_DETAIL RejectDetail);
static HRESULT WINAPI EngineAdapterExportEngineData(PWINBIO_PIPELINE Pipeline, WINBIO_BIR_DATA_FLAGS Flags, PWINBIO_BIR *SampleBuffer, PSIZE_T SampleSize);
static HRESULT WINAPI EngineAdapterVerifyFeatureSet(PWINBIO_PIPELINE Pipeline, PWINBIO_IDENTITY Identity, WINBIO_BIOMETRIC_SUBTYPE SubFactor, PBOOLEAN Match, PUCHAR *PayloadBlob, PSIZE_T PayloadBlobSize, PUCHAR *HashValue, PSIZE_T HashSize, PWINBIO_REJECT_DETAIL RejectDetail);
static HRESULT WINAPI EngineAdapterIdentifyFeatureSet(PWINBIO_PIPELINE Pipeline, PWINBIO_IDENTITY Identity, PWINBIO_BIOMETRIC_SUBTYPE SubFactor, PUCHAR *PayloadBlob, PSIZE_T PayloadBlobSize, PUCHAR *HashValue, PSIZE_T HashSize, PWINBIO_REJECT_DETAIL RejectDetail);
static HRESULT WINAPI EngineAdapterCreateEnrollment(PWINBIO_PIPELINE Pipeline);
static HRESULT WINAPI EngineAdapterUpdateEnrollment(PWINBIO_PIPELINE Pipeline, PWINBIO_REJECT_DETAIL RejectDetail);
static HRESULT WINAPI EngineAdapterGetEnrollmentStatus(PWINBIO_PIPELINE Pipeline, PWINBIO_REJECT_DETAIL RejectDetail);
static HRESULT WINAPI EngineAdapterGetEnrollmentHash(PWINBIO_PIPELINE Pipeline, PUCHAR *HashValue, PSIZE_T HashSize);
static HRESULT WINAPI EngineAdapterCheckForDuplicate(PWINBIO_PIPELINE Pipeline, PWINBIO_IDENTITY Identity, PWINBIO_BIOMETRIC_SUBTYPE SubFactor, PBOOLEAN Duplicate);
static HRESULT WINAPI EngineAdapterCommitEnrollment(PWINBIO_PIPELINE Pipeline, PWINBIO_IDENTITY Identity, WINBIO_BIOMETRIC_SUBTYPE SubFactor, PUCHAR PayloadBlob, SIZE_T PayloadBlobSize);
static HRESULT WINAPI EngineAdapterDiscardEnrollment(PWINBIO_PIPELINE Pipeline);
static HRESULT WINAPI EngineAdapterControlUnit(PWINBIO_PIPELINE Pipeline, ULONG ControlCode, PUCHAR SendBuffer, SIZE_T SendBufferSize, PUCHAR ReceiveBuffer, SIZE_T ReceiveBufferSize, PSIZE_T ReceiveDataSize, PULONG OperationStatus);
static HRESULT WINAPI EngineAdapterControlUnitPrivileged(PWINBIO_PIPELINE Pipeline, ULONG ControlCode, PUCHAR SendBuffer, SIZE_T SendBufferSize, PUCHAR ReceiveBuffer, SIZE_T ReceiveBufferSize, PSIZE_T ReceiveDataSize, PULONG OperationStatus);

// V2 / V3 stubs
static HRESULT WINAPI EngineAdapterNotifyPowerChange(PWINBIO_PIPELINE Pipeline, ULONG PowerEventType) { return S_OK; }
static HRESULT WINAPI EngineAdapterPipelineInit(PWINBIO_PIPELINE Pipeline) { return S_OK; }
static HRESULT WINAPI EngineAdapterPipelineCleanup(PWINBIO_PIPELINE Pipeline) { return S_OK; }
static HRESULT WINAPI EngineAdapterActivate(PWINBIO_PIPELINE Pipeline) { return S_OK; }
static HRESULT WINAPI EngineAdapterDeactivate(PWINBIO_PIPELINE Pipeline) { return S_OK; }
static HRESULT WINAPI EngineAdapterSetEnrollmentSelector(PWINBIO_PIPELINE Pipeline, ULONGLONG SelectorValue) { return S_OK; }
static HRESULT WINAPI EngineAdapterSetEnrollmentParameters(PWINBIO_PIPELINE Pipeline, PWINBIO_EXTENDED_ENROLLMENT_PARAMETERS Parameters) { return S_OK; }
static HRESULT WINAPI EngineAdapterRefreshCache(PWINBIO_PIPELINE Pipeline) { return S_OK; }
static HRESULT WINAPI EngineAdapterSetAccountPolicy(PWINBIO_PIPELINE Pipeline, PWINBIO_ACCOUNT_POLICY PolicyItemArray, SIZE_T PolicyItemCount) { return S_OK; }

static WINBIO_ENGINE_INTERFACE g_EngineInterface = {
    WINBIO_ENGINE_INTERFACE_VERSION_1,
    WINBIO_ADAPTER_TYPE_ENGINE,
    sizeof(WINBIO_ENGINE_INTERFACE),
    GUID_MFS100_ENGINE_ADAPTER,
    EngineAdapterAttach,
    EngineAdapterDetach,
    EngineAdapterClearContext,
    EngineAdapterQueryPreferredFormat,
    EngineAdapterQueryIndexVectorSize,
    EngineAdapterQueryHashAlgorithms,
    EngineAdapterSetHashAlgorithm,
    EngineAdapterQuerySampleHint,
    EngineAdapterAcceptSampleData,
    EngineAdapterExportEngineData,
    EngineAdapterVerifyFeatureSet,
    EngineAdapterIdentifyFeatureSet,
    EngineAdapterCreateEnrollment,
    EngineAdapterUpdateEnrollment,
    EngineAdapterGetEnrollmentStatus,
    EngineAdapterGetEnrollmentHash,
    EngineAdapterCheckForDuplicate,
    EngineAdapterCommitEnrollment,
    EngineAdapterDiscardEnrollment,
    EngineAdapterControlUnit,
    EngineAdapterControlUnitPrivileged
};

extern "C" HRESULT WINAPI WbioQueryEngineInterface(PWINBIO_ENGINE_INTERFACE *EngineInterface)
{
    WbfLog(L"[MFS100EngineAdapter] WbioQueryEngineInterface called.");
    if (EngineInterface == NULL) return E_POINTER;
    *EngineInterface = &g_EngineInterface;
    return S_OK;
}

static BOOL ReadJsonLine(HANDLE hPipe, char* pBuffer, DWORD maxBytes)
{
    while (TRUE)
    {
        DWORD total = 0;
        while (total + 1 < maxBytes)
        {
            char ch = 0;
            DWORD bytesRead = 0;
            if (!ReadFile(hPipe, &ch, 1, &bytesRead, NULL) || bytesRead == 0)
            {
                if (total > 0) break;
                return FALSE;
            }
            if (ch == '\n')
            {
                if (total > 0) break;
                continue;
            }
            if (ch != '\r') pBuffer[total++] = ch;
        }
        pBuffer[total] = '\0';
        if (total == 0) return FALSE;

        char* pJson = strchr(pBuffer, '{');
        if (pJson != NULL)
        {
            if (pJson != pBuffer)
            {
                memmove(pBuffer, pJson, strlen(pJson) + 1);
            }
            return TRUE;
        }
    }
}

static HANDLE ConnectToBridgePipe(void)
{
    for (int retry = 0; retry < 50; retry++)
    {
        HANDLE hPipe = CreateFileW(L"\\\\.\\pipe\\MFS100Bridge", GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING, 0, NULL);
        if (hPipe != INVALID_HANDLE_VALUE)
        {
            return hPipe;
        }
        DWORD dwErr = GetLastError();
        if (dwErr == ERROR_PIPE_BUSY)
        {
            WaitNamedPipeW(L"\\\\.\\pipe\\MFS100Bridge", 1000);
        }
        else
        {
            Sleep(50);
        }
    }
    return INVALID_HANDLE_VALUE;
}

static BOOL WorkerMatch(const char* szProbeB64, const char* szGalleryB64, int* pScore)
{
    *pScore = 0;
    HANDLE hPipe = ConnectToBridgePipe();
    if (hPipe == INVALID_HANDLE_VALUE)
    {
        WbfLog(L"[MFS100EngineAdapter] WorkerMatch failed to connect to pipe: %lu", GetLastError());
        return FALSE;
    }

    size_t cbReq = strlen(szProbeB64) + strlen(szGalleryB64) + 128;
    char* pszReq = (char*)HeapAlloc(GetProcessHeap(), 0, cbReq);
    if (!pszReq)
    {
        CloseHandle(hPipe);
        return FALSE;
    }

    StringCchPrintfA(pszReq, cbReq, "{\"cmd\":\"match\",\"probe\":\"%s\",\"gallery\":\"%s\"}\n", szProbeB64, szGalleryB64);
    DWORD dwWritten = 0;
    WriteFile(hPipe, pszReq, (DWORD)strlen(pszReq), &dwWritten, NULL);
    HeapFree(GetProcessHeap(), 0, pszReq);

    char szRsp[8192];
    BOOL bSuccess = FALSE;
    if (ReadJsonLine(hPipe, szRsp, sizeof(szRsp)))
    {
        WbfLog(L"[MFS100EngineAdapter] WorkerMatch raw response: %S", szRsp);
        const char* pScoreStr = strstr(szRsp, "\"score\":");
        if (!pScoreStr) pScoreStr = strstr(szRsp, "\"score\" :");
        if (pScoreStr)
        {
            while (*pScoreStr && (*pScoreStr < '0' || *pScoreStr > '9') && *pScoreStr != '-') pScoreStr++;
            *pScore = atoi(pScoreStr);
            bSuccess = TRUE;
        }
    }
    else
    {
        WbfLog(L"[MFS100EngineAdapter] WorkerMatch: ReadJsonLine failed: %lu", GetLastError());
    }
    CloseHandle(hPipe);
    WbfLog(L"[MFS100EngineAdapter] WorkerMatch result: score=%d (success=%d)", *pScore, bSuccess);
    return bSuccess;
}

static BOOL ComputeTemplateHash(PMFS100_ENGINE_CONTEXT pCtx, const void* pTemplate, SIZE_T cbTemplate)
{
    pCtx->cbEnrollmentHash = 0;
    if (!pTemplate || cbTemplate == 0) return FALSE;

    HCRYPTPROV hProv = 0;
    HCRYPTHASH hHash = 0;
    if (CryptAcquireContextW(&hProv, NULL, NULL, PROV_RSA_AES, CRYPT_VERIFYCONTEXT) ||
        CryptAcquireContextW(&hProv, NULL, NULL, PROV_RSA_FULL, CRYPT_VERIFYCONTEXT))
    {
        ALG_ID algId = CALG_SHA1;
        if (strcmp(pCtx->szSelectedHashOid, szOID_NIST_sha256) == 0)
        {
            algId = CALG_SHA_256;
        }

        if (CryptCreateHash(hProv, algId, 0, 0, &hHash))
        {
            if (CryptHashData(hHash, (const BYTE*)pTemplate, (DWORD)cbTemplate, 0))
            {
                DWORD dwHashLen = sizeof(pCtx->EnrollmentHash);
                if (CryptGetHashParam(hHash, HP_HASHVAL, pCtx->EnrollmentHash, &dwHashLen, 0))
                {
                    pCtx->cbEnrollmentHash = dwHashLen;
                }
            }
            CryptDestroyHash(hHash);
        }
        CryptReleaseContext(hProv, 0);
    }
    return (pCtx->cbEnrollmentHash > 0);
}

static BOOL CompareIdentity(const WINBIO_IDENTITY* pA, const WINBIO_IDENTITY* pB)
{
    if (!pA || !pB) return FALSE;
    if (pA->Type == WINBIO_ID_TYPE_WILDCARD || pB->Type == WINBIO_ID_TYPE_WILDCARD) return TRUE;
    if (pA->Type == WINBIO_ID_TYPE_NULL || pB->Type == WINBIO_ID_TYPE_NULL) return TRUE;
    if (pA->Type != pB->Type) return FALSE;
    if (pA->Type == WINBIO_ID_TYPE_SID)
    {
        if (pA->Value.AccountSid.Size > 0 && pA->Value.AccountSid.Size == pB->Value.AccountSid.Size)
        {
            if (memcmp(pA->Value.AccountSid.Data, pB->Value.AccountSid.Data, pA->Value.AccountSid.Size) == 0)
                return TRUE;
        }
        if (IsValidSid((PSID)pA->Value.AccountSid.Data) && IsValidSid((PSID)pB->Value.AccountSid.Data))
        {
            return EqualSid((PSID)pA->Value.AccountSid.Data, (PSID)pB->Value.AccountSid.Data);
        }
        return FALSE;
    }
    if (pA->Type == WINBIO_ID_TYPE_GUID)
    {
        return IsEqualGUID(pA->Value.TemplateGuid, pB->Value.TemplateGuid);
    }
    return FALSE;
}

static HRESULT WINAPI EngineAdapterAttach(PWINBIO_PIPELINE Pipeline)
{
    WbfLog(L"[MFS100EngineAdapter] EngineAdapterAttach called.");
    if (Pipeline == NULL) return E_POINTER;

    PMFS100_ENGINE_CONTEXT pCtx = (PMFS100_ENGINE_CONTEXT)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(MFS100_ENGINE_CONTEXT));
    if (!pCtx) return E_OUTOFMEMORY;

    pCtx->dwEnrollmentTarget = MFS100_ENROLLMENT_STEPS_NEEDED;
    StringCchCopyA(pCtx->szSelectedHashOid, ARRAYSIZE(pCtx->szSelectedHashOid), szOID_OIWSEC_sha1);
    Pipeline->EngineContext = (PWINIBIO_ENGINE_CONTEXT)pCtx;
    return S_OK;
}

static HRESULT WINAPI EngineAdapterDetach(PWINBIO_PIPELINE Pipeline)
{
    WbfLog(L"[MFS100EngineAdapter] EngineAdapterDetach called.");
    if (Pipeline == NULL || Pipeline->EngineContext == NULL) return E_POINTER;

    HeapFree(GetProcessHeap(), 0, Pipeline->EngineContext);
    Pipeline->EngineContext = NULL;
    return S_OK;
}

static HRESULT WINAPI EngineAdapterClearContext(PWINBIO_PIPELINE Pipeline)
{
    WbfLog(L"[MFS100EngineAdapter] EngineAdapterClearContext called.");
    if (Pipeline == NULL || Pipeline->EngineContext == NULL) return E_POINTER;

    PMFS100_ENGINE_CONTEXT pCtx = (PMFS100_ENGINE_CONTEXT)Pipeline->EngineContext;
    ZeroMemory(pCtx->CurrentFeatureSet, sizeof(pCtx->CurrentFeatureSet));
    pCtx->cbCurrentFeatureSet = 0;
    ZeroMemory(pCtx->MatchedPayloadBlob, sizeof(pCtx->MatchedPayloadBlob));
    pCtx->cbMatchedPayloadBlob = 0;
    ZeroMemory(pCtx->EnrollmentHash, sizeof(pCtx->EnrollmentHash));
    pCtx->cbEnrollmentHash = 0;
    return S_OK;
}

static HRESULT WINAPI EngineAdapterQueryPreferredFormat(PWINBIO_PIPELINE Pipeline, PWINBIO_REGISTERED_FORMAT StandardFormat, PWINBIO_UUID VendorFormat)
{
    if (StandardFormat == NULL || VendorFormat == NULL) return E_POINTER;
    StandardFormat->Owner = WINBIO_ANSI_381_FORMAT_OWNER;
    StandardFormat->Type = WINBIO_ANSI_381_FORMAT_TYPE;
    ZeroMemory(VendorFormat, sizeof(WINBIO_UUID));
    return S_OK;
}

static HRESULT WINAPI EngineAdapterQueryIndexVectorSize(PWINBIO_PIPELINE Pipeline, PSIZE_T IndexElementCount)
{
    if (IndexElementCount == NULL) return E_POINTER;
    *IndexElementCount = 0;
    return S_OK;
}

static const char g_szHashAlgorithms[] = szOID_OIWSEC_sha1 "\0" szOID_NIST_sha256 "\0";

static HRESULT WINAPI EngineAdapterQueryHashAlgorithms(PWINBIO_PIPELINE Pipeline, PSIZE_T AlgorithmCount, PSIZE_T AlgorithmBufferSize, PUCHAR *AlgorithmBuffer)
{
    if (AlgorithmCount == NULL || AlgorithmBufferSize == NULL || AlgorithmBuffer == NULL) return E_POINTER;
    *AlgorithmCount = 2;
    *AlgorithmBufferSize = sizeof(g_szHashAlgorithms);
    *AlgorithmBuffer = (PUCHAR)g_szHashAlgorithms;
    return S_OK;
}

static HRESULT WINAPI EngineAdapterSetHashAlgorithm(PWINBIO_PIPELINE Pipeline, SIZE_T AlgorithmBufferSize, PUCHAR AlgorithmBuffer)
{
    if (Pipeline == NULL || Pipeline->EngineContext == NULL) return E_POINTER;
    PMFS100_ENGINE_CONTEXT pCtx = (PMFS100_ENGINE_CONTEXT)Pipeline->EngineContext;
    if (AlgorithmBuffer && AlgorithmBufferSize > 0)
    {
        StringCchCopyA(pCtx->szSelectedHashOid, ARRAYSIZE(pCtx->szSelectedHashOid), (const char*)AlgorithmBuffer);
    }
    return S_OK;
}

static HRESULT WINAPI EngineAdapterQuerySampleHint(PWINBIO_PIPELINE Pipeline, PSIZE_T SampleHint)
{
    if (SampleHint == NULL) return E_POINTER;
    *SampleHint = MFS100_ENROLLMENT_STEPS_NEEDED;
    return S_OK;
}

static HRESULT WINAPI EngineAdapterAcceptSampleData(PWINBIO_PIPELINE Pipeline, PWINBIO_BIR SampleBuffer, SIZE_T SampleSize, WINBIO_BIR_PURPOSE Purpose, PWINBIO_REJECT_DETAIL RejectDetail)
{
    WbfLog(L"[MFS100EngineAdapter] EngineAdapterAcceptSampleData called.");
    if (Pipeline == NULL || Pipeline->EngineContext == NULL || SampleBuffer == NULL || RejectDetail == NULL) return E_POINTER;

    PMFS100_ENGINE_CONTEXT pCtx = (PMFS100_ENGINE_CONTEXT)Pipeline->EngineContext;
    *RejectDetail = 0;

    if (SampleBuffer->StandardDataBlock.Size == 0 || SampleBuffer->StandardDataBlock.Offset >= SampleSize)
    {
        *RejectDetail = WINBIO_FP_POOR_QUALITY;
        return WINBIO_E_BAD_CAPTURE;
    }

    PUCHAR pStandardData = ((PUCHAR)SampleBuffer) + SampleBuffer->StandardDataBlock.Offset;
    SIZE_T cbStandardData = SampleBuffer->StandardDataBlock.Size;

    if (cbStandardData > sizeof(pCtx->CurrentFeatureSet))
    {
        cbStandardData = sizeof(pCtx->CurrentFeatureSet);
    }

    memcpy(pCtx->CurrentFeatureSet, pStandardData, cbStandardData);
    pCtx->cbCurrentFeatureSet = cbStandardData;

    WbfLog(L"[MFS100EngineAdapter] Feature set extracted from sample successfully.");
    return S_OK;
}

static HRESULT WINAPI EngineAdapterExportEngineData(PWINBIO_PIPELINE Pipeline, WINBIO_BIR_DATA_FLAGS Flags, PWINBIO_BIR *SampleBuffer, PSIZE_T SampleSize)
{
    if (Pipeline == NULL || Pipeline->EngineContext == NULL || SampleBuffer == NULL || SampleSize == NULL) return E_POINTER;
    PMFS100_ENGINE_CONTEXT pCtx = (PMFS100_ENGINE_CONTEXT)Pipeline->EngineContext;

    PUCHAR pData = (Flags == WINBIO_DATA_FLAG_INTERMEDIATE) ? pCtx->CurrentFeatureSet : pCtx->EnrollmentTemplate;
    SIZE_T cbData = (Flags == WINBIO_DATA_FLAG_INTERMEDIATE) ? pCtx->cbCurrentFeatureSet : pCtx->cbEnrollmentTemplate;

    if (cbData == 0) return WINBIO_E_NO_CAPTURE_DATA;

    SIZE_T cbTotal = sizeof(WINBIO_BIR) + sizeof(WINBIO_BIR_HEADER) + cbData;
    PWINBIO_BIR pBir = (PWINBIO_BIR)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, cbTotal);
    if (!pBir) return E_OUTOFMEMORY;

    pBir->HeaderBlock.Offset = sizeof(WINBIO_BIR);
    pBir->HeaderBlock.Size = sizeof(WINBIO_BIR_HEADER);

    PWINBIO_BIR_HEADER pHeader = (PWINBIO_BIR_HEADER)(((PUCHAR)pBir) + pBir->HeaderBlock.Offset);
    pHeader->ValidFields = WINBIO_BIR_FIELD_BIOMETRIC_TYPE | WINBIO_BIR_FIELD_BIOMETRIC_SUBTYPE | WINBIO_BIR_FIELD_BIOMETRIC_PURPOSE | WINBIO_BIR_FIELD_CBEFF_HEADER_VERSION | WINBIO_BIR_FIELD_PATRON_HEADER_VERSION;
    pHeader->HeaderVersion = WINBIO_CBEFF_HEADER_VERSION;
    pHeader->PatronHeaderVersion = WINBIO_PATRON_HEADER_VERSION;
    pHeader->Type = WINBIO_TYPE_FINGERPRINT;
    pHeader->Purpose = WINBIO_PURPOSE_ENROLL_FOR_VERIFICATION;
    pHeader->DataFlags = Flags;

    pBir->StandardDataBlock.Offset = sizeof(WINBIO_BIR) + sizeof(WINBIO_BIR_HEADER);
    pBir->StandardDataBlock.Size = (ULONG)cbData;
    memcpy(((PUCHAR)pBir) + pBir->StandardDataBlock.Offset, pData, cbData);

    *SampleBuffer = pBir;
    *SampleSize = cbTotal;
    return S_OK;
}

static HRESULT WINAPI EngineAdapterCreateEnrollment(PWINBIO_PIPELINE Pipeline)
{
    WbfLog(L"[MFS100EngineAdapter] EngineAdapterCreateEnrollment called.");
    if (Pipeline == NULL || Pipeline->EngineContext == NULL) return E_POINTER;

    PMFS100_ENGINE_CONTEXT pCtx = (PMFS100_ENGINE_CONTEXT)Pipeline->EngineContext;
    pCtx->dwEnrollmentCount = 0;
    pCtx->dwEnrollmentTarget = MFS100_ENROLLMENT_STEPS_NEEDED;
    ZeroMemory(pCtx->EnrollmentTemplate, sizeof(pCtx->EnrollmentTemplate));
    pCtx->cbEnrollmentTemplate = 0;
    return S_OK;
}

static HRESULT WINAPI EngineAdapterUpdateEnrollment(PWINBIO_PIPELINE Pipeline, PWINBIO_REJECT_DETAIL RejectDetail)
{
    WbfLog(L"[MFS100EngineAdapter] EngineAdapterUpdateEnrollment called.");
    if (Pipeline == NULL || Pipeline->EngineContext == NULL || RejectDetail == NULL) return E_POINTER;

    PMFS100_ENGINE_CONTEXT pCtx = (PMFS100_ENGINE_CONTEXT)Pipeline->EngineContext;
    *RejectDetail = 0;

    if (pCtx->cbCurrentFeatureSet == 0)
    {
        *RejectDetail = WINBIO_FP_POOR_QUALITY;
        return WINBIO_E_BAD_CAPTURE;
    }

    pCtx->dwEnrollmentCount++;
    WbfLog(L"[MFS100EngineAdapter] Enrollment step completed.");

    if (pCtx->cbCurrentFeatureSet > 0)
    {
        memcpy(pCtx->EnrollmentTemplate, pCtx->CurrentFeatureSet, pCtx->cbCurrentFeatureSet);
        pCtx->cbEnrollmentTemplate = pCtx->cbCurrentFeatureSet;
    }

    if (pCtx->dwEnrollmentCount >= pCtx->dwEnrollmentTarget)
    {
        WbfLog(L"[MFS100EngineAdapter] Enrollment complete! Ready to commit.");
        return S_OK;
    }

    return WINBIO_I_MORE_DATA;
}

static HRESULT WINAPI EngineAdapterGetEnrollmentStatus(PWINBIO_PIPELINE Pipeline, PWINBIO_REJECT_DETAIL RejectDetail)
{
    if (Pipeline == NULL || Pipeline->EngineContext == NULL || RejectDetail == NULL) return E_POINTER;
    PMFS100_ENGINE_CONTEXT pCtx = (PMFS100_ENGINE_CONTEXT)Pipeline->EngineContext;
    *RejectDetail = 0;

    if (pCtx->dwEnrollmentCount >= pCtx->dwEnrollmentTarget)
    {
        return S_OK;
    }
    return WINBIO_I_MORE_DATA;
}

static HRESULT WINAPI EngineAdapterGetEnrollmentHash(PWINBIO_PIPELINE Pipeline, PUCHAR *HashValue, PSIZE_T HashSize)
{
    if (Pipeline == NULL || Pipeline->EngineContext == NULL || HashValue == NULL || HashSize == NULL) return E_POINTER;
    PMFS100_ENGINE_CONTEXT pCtx = (PMFS100_ENGINE_CONTEXT)Pipeline->EngineContext;

    if (pCtx->cbEnrollmentTemplate == 0) return WINBIO_E_NO_CAPTURE_DATA;

    HCRYPTPROV hProv = 0;
    HCRYPTHASH hHash = 0;
    if (CryptAcquireContextW(&hProv, NULL, NULL, PROV_RSA_AES, CRYPT_VERIFYCONTEXT) ||
        CryptAcquireContextW(&hProv, NULL, NULL, PROV_RSA_FULL, CRYPT_VERIFYCONTEXT))
    {
        ALG_ID algId = CALG_SHA1;
        if (strcmp(pCtx->szSelectedHashOid, szOID_NIST_sha256) == 0)
        {
            algId = CALG_SHA_256;
        }

        if (CryptCreateHash(hProv, algId, 0, 0, &hHash))
        {
            CryptHashData(hHash, pCtx->EnrollmentTemplate, (DWORD)pCtx->cbEnrollmentTemplate, 0);
            DWORD dwHashLen = sizeof(pCtx->EnrollmentHash);
            if (CryptGetHashParam(hHash, HP_HASHVAL, pCtx->EnrollmentHash, &dwHashLen, 0))
            {
                pCtx->cbEnrollmentHash = dwHashLen;
            }
            CryptDestroyHash(hHash);
        }
        CryptReleaseContext(hProv, 0);
    }

    *HashValue = pCtx->EnrollmentHash;
    *HashSize = pCtx->cbEnrollmentHash;
    return S_OK;
}

static HRESULT WINAPI EngineAdapterCheckForDuplicate(PWINBIO_PIPELINE Pipeline, PWINBIO_IDENTITY Identity, PWINBIO_BIOMETRIC_SUBTYPE SubFactor, PBOOLEAN Duplicate)
{
    if (Duplicate == NULL) return E_POINTER;
    *Duplicate = FALSE;
    return S_OK;
}

static HRESULT WINAPI EngineAdapterCommitEnrollment(PWINBIO_PIPELINE Pipeline, PWINBIO_IDENTITY Identity, WINBIO_BIOMETRIC_SUBTYPE SubFactor, PUCHAR PayloadBlob, SIZE_T PayloadBlobSize)
{
    WbfLog(L"[MFS100EngineAdapter] EngineAdapterCommitEnrollment called.");
    if (Pipeline == NULL || Pipeline->EngineContext == NULL || Identity == NULL) return E_POINTER;

    PMFS100_ENGINE_CONTEXT pCtx = (PMFS100_ENGINE_CONTEXT)Pipeline->EngineContext;
    if (pCtx->cbEnrollmentTemplate == 0) return WINBIO_E_NO_CAPTURE_DATA;

    WINBIO_STORAGE_RECORD record = { 0 };
    record.Identity = Identity;
    record.SubFactor = SubFactor;
    record.IndexVector = NULL;
    record.IndexElementCount = 0;
    record.TemplateBlob = pCtx->EnrollmentTemplate;
    record.TemplateBlobSize = pCtx->cbEnrollmentTemplate;
    record.PayloadBlob = PayloadBlob;
    record.PayloadBlobSize = PayloadBlobSize;

    HRESULT hr = S_OK;
    if (Pipeline->StorageInterface && Pipeline->StorageInterface->AddRecord)
    {
        hr = Pipeline->StorageInterface->AddRecord(Pipeline, &record);
        if (FAILED(hr))
        {
            WbfLog(L"[MFS100EngineAdapter] StorageInterface->AddRecord failed.");
            return hr;
        }
    }

    WbfLog(L"[MFS100EngineAdapter] Enrollment committed to storage successfully!");
    return S_OK;
}

static HRESULT WINAPI EngineAdapterDiscardEnrollment(PWINBIO_PIPELINE Pipeline)
{
    WbfLog(L"[MFS100EngineAdapter] EngineAdapterDiscardEnrollment called.");
    if (Pipeline == NULL || Pipeline->EngineContext == NULL) return E_POINTER;
    PMFS100_ENGINE_CONTEXT pCtx = (PMFS100_ENGINE_CONTEXT)Pipeline->EngineContext;
    pCtx->dwEnrollmentCount = 0;
    pCtx->cbEnrollmentTemplate = 0;
    return S_OK;
}

static HRESULT WINAPI EngineAdapterVerifyFeatureSet(PWINBIO_PIPELINE Pipeline, PWINBIO_IDENTITY Identity, WINBIO_BIOMETRIC_SUBTYPE SubFactor, PBOOLEAN Match, PUCHAR *PayloadBlob, PSIZE_T PayloadBlobSize, PUCHAR *HashValue, PSIZE_T HashSize, PWINBIO_REJECT_DETAIL RejectDetail)
{
    WbfLog(L"[MFS100EngineAdapter] EngineAdapterVerifyFeatureSet called: SubFactor=0x%02X, IdentityType=%lu",
           SubFactor, Identity ? Identity->Type : 0xFFFFFFFF);
    if (Pipeline == NULL || Pipeline->EngineContext == NULL || Match == NULL || RejectDetail == NULL) return E_POINTER;

    PMFS100_ENGINE_CONTEXT pCtx = (PMFS100_ENGINE_CONTEXT)Pipeline->EngineContext;
    *Match = FALSE;
    *RejectDetail = 0;
    if (PayloadBlob) *PayloadBlob = NULL;
    if (PayloadBlobSize) *PayloadBlobSize = 0;
    if (HashValue) *HashValue = NULL;
    if (HashSize) *HashSize = 0;

    if (pCtx->cbCurrentFeatureSet == 0)
    {
        WbfLog(L"[MFS100EngineAdapter] VerifyFeatureSet failed: cbCurrentFeatureSet is 0!");
        return WINBIO_E_NO_CAPTURE_DATA;
    }

    DWORD cchProbe = 0;
    CryptBinaryToStringA(pCtx->CurrentFeatureSet, (DWORD)pCtx->cbCurrentFeatureSet, CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, NULL, &cchProbe);
    char* pszProbe = (char*)HeapAlloc(GetProcessHeap(), 0, cchProbe + 1);
    if (!pszProbe) return E_OUTOFMEMORY;
    CryptBinaryToStringA(pCtx->CurrentFeatureSet, (DWORD)pCtx->cbCurrentFeatureSet, CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, pszProbe, &cchProbe);

    BOOLEAN bFound = FALSE;
    if (Pipeline->StorageInterface && Pipeline->StorageInterface->FirstRecord && Pipeline->StorageInterface->GetCurrentRecord)
    {
        HRESULT hr = Pipeline->StorageInterface->FirstRecord(Pipeline);
        while (SUCCEEDED(hr))
        {
            WINBIO_STORAGE_RECORD record = { 0 };
            HRESULT hrRecord = Pipeline->StorageInterface->GetCurrentRecord(Pipeline, &record);
            if (SUCCEEDED(hrRecord) && record.TemplateBlob && record.TemplateBlobSize > 0)
            {
                BOOL idMatch = (Identity == NULL || CompareIdentity(record.Identity, Identity));
                BOOL subMatch = (SubFactor == WINBIO_SUBTYPE_NO_INFORMATION ||
                                 SubFactor == WINBIO_SUBTYPE_ANY ||
                                 record.SubFactor == WINBIO_SUBTYPE_NO_INFORMATION ||
                                 record.SubFactor == WINBIO_SUBTYPE_ANY ||
                                 record.SubFactor == SubFactor);

                WbfLog(L"[MFS100EngineAdapter] Storage record: recSub=0x%02X reqSub=0x%02X (idMatch=%d, subMatch=%d, templSize=%llu, payloadSize=%llu)",
                       record.SubFactor, SubFactor, idMatch, subMatch, record.TemplateBlobSize, record.PayloadBlobSize);

                if (idMatch && subMatch)
                {
                    DWORD cchGallery = 0;
                    CryptBinaryToStringA(record.TemplateBlob, (DWORD)record.TemplateBlobSize, CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, NULL, &cchGallery);
                    char* pszGallery = (char*)HeapAlloc(GetProcessHeap(), 0, cchGallery + 1);
                    if (pszGallery)
                    {
                        CryptBinaryToStringA(record.TemplateBlob, (DWORD)record.TemplateBlobSize, CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, pszGallery, &cchGallery);
                        int score = 0;
                        if (WorkerMatch(pszProbe, pszGallery, &score))
                        {
                            WbfLog(L"[MFS100EngineAdapter] Verify: SubFactor=0x%02X match score=%d (threshold=200)",
                                   record.SubFactor, score);
                            if (score >= 200)
                            {
                                *Match = TRUE;
                                bFound = TRUE;

                                ComputeTemplateHash(pCtx, record.TemplateBlob, record.TemplateBlobSize);
                                if (HashValue) *HashValue = (pCtx->cbEnrollmentHash > 0) ? pCtx->EnrollmentHash : NULL;
                                if (HashSize) *HashSize = pCtx->cbEnrollmentHash;

                                if (record.PayloadBlob && record.PayloadBlobSize > 0)
                                {
                                    SIZE_T toCopy = min(record.PayloadBlobSize, sizeof(pCtx->MatchedPayloadBlob));
                                    memcpy(pCtx->MatchedPayloadBlob, record.PayloadBlob, toCopy);
                                    pCtx->cbMatchedPayloadBlob = toCopy;
                                    if (PayloadBlob) *PayloadBlob = pCtx->MatchedPayloadBlob;
                                    if (PayloadBlobSize) *PayloadBlobSize = pCtx->cbMatchedPayloadBlob;
                                }
                                else
                                {
                                    pCtx->cbMatchedPayloadBlob = 0;
                                    if (PayloadBlob) *PayloadBlob = NULL;
                                    if (PayloadBlobSize) *PayloadBlobSize = 0;
                                }

                                WbfLog(L"[MFS100EngineAdapter] VerifyFeatureSet matched! Score=%d HashSize=%llu PayloadSize=%llu",
                                       score, pCtx->cbEnrollmentHash, pCtx->cbMatchedPayloadBlob);
                                HeapFree(GetProcessHeap(), 0, pszGallery);
                                break;
                            }
                        }
                        HeapFree(GetProcessHeap(), 0, pszGallery);
                    }
                }
            }
            if (!Pipeline->StorageInterface->NextRecord) break;
            hr = Pipeline->StorageInterface->NextRecord(Pipeline);
        }
    }

    HeapFree(GetProcessHeap(), 0, pszProbe);
    WbfLog(L"[MFS100EngineAdapter] EngineAdapterVerifyFeatureSet returning Match=%d", *Match);
    return S_OK;
}

static HRESULT WINAPI EngineAdapterIdentifyFeatureSet(PWINBIO_PIPELINE Pipeline, PWINBIO_IDENTITY Identity, PWINBIO_BIOMETRIC_SUBTYPE SubFactor, PUCHAR *PayloadBlob, PSIZE_T PayloadBlobSize, PUCHAR *HashValue, PSIZE_T HashSize, PWINBIO_REJECT_DETAIL RejectDetail)
{
    WbfLog(L"[MFS100EngineAdapter] EngineAdapterIdentifyFeatureSet called.");
    if (Pipeline == NULL || Pipeline->EngineContext == NULL || Identity == NULL || SubFactor == NULL || RejectDetail == NULL) return E_POINTER;

    PMFS100_ENGINE_CONTEXT pCtx = (PMFS100_ENGINE_CONTEXT)Pipeline->EngineContext;
    *RejectDetail = 0;
    if (PayloadBlob) *PayloadBlob = NULL;
    if (PayloadBlobSize) *PayloadBlobSize = 0;
    if (HashValue) *HashValue = NULL;
    if (HashSize) *HashSize = 0;

    if (pCtx->cbCurrentFeatureSet == 0) return WINBIO_E_NO_CAPTURE_DATA;

    DWORD cchProbe = 0;
    CryptBinaryToStringA(pCtx->CurrentFeatureSet, (DWORD)pCtx->cbCurrentFeatureSet, CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, NULL, &cchProbe);
    char* pszProbe = (char*)HeapAlloc(GetProcessHeap(), 0, cchProbe + 1);
    if (!pszProbe) return E_OUTOFMEMORY;
    CryptBinaryToStringA(pCtx->CurrentFeatureSet, (DWORD)pCtx->cbCurrentFeatureSet, CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, pszProbe, &cchProbe);

    BOOLEAN bFound = FALSE;
    if (Pipeline->StorageInterface && Pipeline->StorageInterface->FirstRecord && Pipeline->StorageInterface->GetCurrentRecord)
    {
        HRESULT hr = Pipeline->StorageInterface->FirstRecord(Pipeline);
        while (SUCCEEDED(hr))
        {
            WINBIO_STORAGE_RECORD record = { 0 };
            HRESULT hrRecord = Pipeline->StorageInterface->GetCurrentRecord(Pipeline, &record);
            if (SUCCEEDED(hrRecord) && record.TemplateBlob && record.TemplateBlobSize > 0)
            {
                DWORD cchGallery = 0;
                CryptBinaryToStringA(record.TemplateBlob, (DWORD)record.TemplateBlobSize, CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, NULL, &cchGallery);
                char* pszGallery = (char*)HeapAlloc(GetProcessHeap(), 0, cchGallery + 1);
                if (pszGallery)
                {
                    CryptBinaryToStringA(record.TemplateBlob, (DWORD)record.TemplateBlobSize, CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, pszGallery, &cchGallery);
                    int score = 0;
                    if (WorkerMatch(pszProbe, pszGallery, &score))
                    {
                        WbfLog(L"[MFS100EngineAdapter] Identify: Record SubFactor=0x%02X match score=%d (threshold=200)",
                               record.SubFactor, score);
                        if (score >= 200)
                        {
                            if (record.Identity) *Identity = *record.Identity;
                            *SubFactor = record.SubFactor;
                            bFound = TRUE;

                            ComputeTemplateHash(pCtx, record.TemplateBlob, record.TemplateBlobSize);
                            if (HashValue) *HashValue = (pCtx->cbEnrollmentHash > 0) ? pCtx->EnrollmentHash : NULL;
                            if (HashSize) *HashSize = pCtx->cbEnrollmentHash;

                            if (record.PayloadBlob && record.PayloadBlobSize > 0)
                            {
                                SIZE_T toCopy = min(record.PayloadBlobSize, sizeof(pCtx->MatchedPayloadBlob));
                                memcpy(pCtx->MatchedPayloadBlob, record.PayloadBlob, toCopy);
                                pCtx->cbMatchedPayloadBlob = toCopy;
                                if (PayloadBlob) *PayloadBlob = pCtx->MatchedPayloadBlob;
                                if (PayloadBlobSize) *PayloadBlobSize = pCtx->cbMatchedPayloadBlob;
                            }
                            else
                            {
                                pCtx->cbMatchedPayloadBlob = 0;
                                if (PayloadBlob) *PayloadBlob = NULL;
                                if (PayloadBlobSize) *PayloadBlobSize = 0;
                            }

                            WbfLog(L"[MFS100EngineAdapter] Identify matched identity! Score=%d HashSize=%llu PayloadSize=%llu",
                                   score, pCtx->cbEnrollmentHash, pCtx->cbMatchedPayloadBlob);
                            HeapFree(GetProcessHeap(), 0, pszGallery);
                            break;
                        }
                    }
                    HeapFree(GetProcessHeap(), 0, pszGallery);
                }
            }
            if (!Pipeline->StorageInterface->NextRecord) break;
            hr = Pipeline->StorageInterface->NextRecord(Pipeline);
        }
    }

    HeapFree(GetProcessHeap(), 0, pszProbe);

    if (bFound)
    {
        return S_OK;
    }

    WbfLog(L"[MFS100EngineAdapter] Identify found no matching identity in storage.");
    return WINBIO_E_UNKNOWN_ID;
}

static HRESULT WINAPI EngineAdapterControlUnit(PWINBIO_PIPELINE Pipeline, ULONG ControlCode, PUCHAR SendBuffer, SIZE_T SendBufferSize, PUCHAR ReceiveBuffer, SIZE_T ReceiveBufferSize, PSIZE_T ReceiveDataSize, PULONG OperationStatus)
{
    return E_NOTIMPL;
}

static HRESULT WINAPI EngineAdapterControlUnitPrivileged(PWINBIO_PIPELINE Pipeline, ULONG ControlCode, PUCHAR SendBuffer, SIZE_T SendBufferSize, PUCHAR ReceiveBuffer, SIZE_T ReceiveBufferSize, PSIZE_T ReceiveDataSize, PULONG OperationStatus)
{
    return E_NOTIMPL;
}
