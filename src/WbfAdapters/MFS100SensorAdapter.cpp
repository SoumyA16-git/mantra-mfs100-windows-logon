#include "MFS100SensorAdapter.h"
#include <winbio_adapter.h>
#include <wincrypt.h>

// Forward declarations of static interface functions
static HRESULT WINAPI SensorAdapterAttach(PWINBIO_PIPELINE Pipeline);
static HRESULT WINAPI SensorAdapterDetach(PWINBIO_PIPELINE Pipeline);
static HRESULT WINAPI SensorAdapterClearContext(PWINBIO_PIPELINE Pipeline);
static HRESULT WINAPI SensorAdapterQueryStatus(PWINBIO_PIPELINE Pipeline, PWINBIO_SENSOR_STATUS Status);
static HRESULT WINAPI SensorAdapterReset(PWINBIO_PIPELINE Pipeline);
static HRESULT WINAPI SensorAdapterSetMode(PWINBIO_PIPELINE Pipeline, WINBIO_SENSOR_MODE Mode);
static HRESULT WINAPI SensorAdapterSetIndicatorStatus(PWINBIO_PIPELINE Pipeline, WINBIO_INDICATOR_STATUS IndicatorStatus);
static HRESULT WINAPI SensorAdapterGetIndicatorStatus(PWINBIO_PIPELINE Pipeline, PWINBIO_INDICATOR_STATUS IndicatorStatus);
static HRESULT WINAPI SensorAdapterStartCapture(PWINBIO_PIPELINE Pipeline, WINBIO_BIR_PURPOSE Purpose, LPOVERLAPPED *Overlapped);
static HRESULT WINAPI SensorAdapterFinishCapture(PWINBIO_PIPELINE Pipeline, PWINBIO_REJECT_DETAIL RejectDetail);
static HRESULT WINAPI SensorAdapterExportSensorData(PWINBIO_PIPELINE Pipeline, PWINBIO_BIR *SampleBuffer, PSIZE_T SampleSize);
static HRESULT WINAPI SensorAdapterCancel(PWINBIO_PIPELINE Pipeline);
static HRESULT WINAPI SensorAdapterPushDataToEngine(PWINBIO_PIPELINE Pipeline, WINBIO_BIR_PURPOSE Purpose, WINBIO_BIR_DATA_FLAGS Flags, PWINBIO_REJECT_DETAIL RejectDetail);
static HRESULT WINAPI SensorAdapterControlUnit(PWINBIO_PIPELINE Pipeline, ULONG ControlCode, PUCHAR SendBuffer, SIZE_T SendBufferSize, PUCHAR ReceiveBuffer, SIZE_T ReceiveBufferSize, PSIZE_T ReceiveDataSize, PULONG OperationStatus);
static HRESULT WINAPI SensorAdapterControlUnitPrivileged(PWINBIO_PIPELINE Pipeline, ULONG ControlCode, PUCHAR SendBuffer, SIZE_T SendBufferSize, PUCHAR ReceiveBuffer, SIZE_T ReceiveBufferSize, PSIZE_T ReceiveDataSize, PULONG OperationStatus);

// Internal state for capture task
typedef struct _CAPTURE_TASK {
    PWINBIO_PIPELINE Pipeline;
    WINBIO_BIR_PURPOSE Purpose;
    OVERLAPPED Overlapped;
    HANDLE hThread;
    HANDLE hActivePipe;
    BOOL bCompleted;
    HRESULT hrResult;
    WINBIO_REJECT_DETAIL RejectDetail;
} CAPTURE_TASK, *PCAPTURE_TASK;

static CAPTURE_TASK g_CaptureTask = { 0 };

static WINBIO_SENSOR_INTERFACE g_SensorInterface = {
    WINBIO_SENSOR_INTERFACE_VERSION_1,
    WINBIO_ADAPTER_TYPE_SENSOR,
    sizeof(WINBIO_SENSOR_INTERFACE),
    GUID_MFS100_SENSOR_ADAPTER,
    SensorAdapterAttach,
    SensorAdapterDetach,
    SensorAdapterClearContext,
    SensorAdapterQueryStatus,
    SensorAdapterReset,
    SensorAdapterSetMode,
    SensorAdapterSetIndicatorStatus,
    SensorAdapterGetIndicatorStatus,
    SensorAdapterStartCapture,
    SensorAdapterFinishCapture,
    SensorAdapterExportSensorData,
    SensorAdapterCancel,
    SensorAdapterPushDataToEngine,
    SensorAdapterControlUnit,
    SensorAdapterControlUnitPrivileged
};

extern "C" HRESULT WINAPI WbioQuerySensorInterface(PWINBIO_SENSOR_INTERFACE *SensorInterface)
{
    WbfLog(L"[MFS100SensorAdapter] WbioQuerySensorInterface called.");
    if (SensorInterface == NULL) return E_POINTER;
    *SensorInterface = &g_SensorInterface;
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
        // If line contains no '{', it was non-JSON chatter; loop to read next line
    }
}

static PWINBIO_BIR CreateBirFromTemplate(const char* szIsoBase64, WINBIO_BIR_PURPOSE Purpose, SIZE_T* pcbTotal)
{
    DWORD cbBinary = 0;
    if (!CryptStringToBinaryA(szIsoBase64, 0, CRYPT_STRING_BASE64, NULL, &cbBinary, NULL, NULL) || cbBinary == 0)
    {
        return NULL;
    }

    BYTE* pBinary = (BYTE*)HeapAlloc(GetProcessHeap(), 0, cbBinary);
    if (!pBinary) return NULL;
    CryptStringToBinaryA(szIsoBase64, 0, CRYPT_STRING_BASE64, pBinary, &cbBinary, NULL, NULL);

    SIZE_T cbHeader = sizeof(WINBIO_BIR_HEADER);
    SIZE_T cbTotal = sizeof(WINBIO_BIR) + cbHeader + cbBinary;

    PWINBIO_BIR pBir = (PWINBIO_BIR)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, cbTotal);
    if (!pBir)
    {
        HeapFree(GetProcessHeap(), 0, pBinary);
        return NULL;
    }

    BYTE* pPayload = (BYTE*)pBir;

    pBir->HeaderBlock.Offset = (ULONG)sizeof(WINBIO_BIR);
    pBir->HeaderBlock.Size = (ULONG)cbHeader;

    PWINBIO_BIR_HEADER pHeader = (PWINBIO_BIR_HEADER)(pPayload + pBir->HeaderBlock.Offset);
    pHeader->ValidFields = WINBIO_BIR_FIELD_BIOMETRIC_TYPE | WINBIO_BIR_FIELD_BIOMETRIC_SUBTYPE | WINBIO_BIR_FIELD_BIOMETRIC_PURPOSE | WINBIO_BIR_FIELD_CBEFF_HEADER_VERSION | WINBIO_BIR_FIELD_PATRON_HEADER_VERSION;
    pHeader->HeaderVersion = WINBIO_CBEFF_HEADER_VERSION;
    pHeader->PatronHeaderVersion = WINBIO_PATRON_HEADER_VERSION;
    pHeader->Type = WINBIO_TYPE_FINGERPRINT;
    pHeader->Subtype = WINBIO_SUBTYPE_NO_INFORMATION;
    pHeader->Purpose = Purpose;
    pHeader->DataFlags = WINBIO_DATA_FLAG_RAW;

    pBir->StandardDataBlock.Offset = (ULONG)(sizeof(WINBIO_BIR) + cbHeader);
    pBir->StandardDataBlock.Size = (ULONG)cbBinary;
    memcpy(pPayload + pBir->StandardDataBlock.Offset, pBinary, cbBinary);

    HeapFree(GetProcessHeap(), 0, pBinary);

    if (pcbTotal) *pcbTotal = cbTotal;
    return pBir;
}

static HANDLE ConnectBridgePipe()
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
            Sleep(100);
        }
    }
    return INVALID_HANDLE_VALUE;
}

static DWORD WINAPI CaptureThreadProc(LPVOID lpParam)
{
    PCAPTURE_TASK pTask = (PCAPTURE_TASK)lpParam;
    PMFS100_SENSOR_CONTEXT pCtx = (PMFS100_SENSOR_CONTEXT)pTask->Pipeline->SensorContext;

    WbfLog(L"[MFS100SensorAdapter] Capture thread started. Connecting to MFS100 bridge...");

    HANDLE hPipe = ConnectBridgePipe();
    if (hPipe == INVALID_HANDLE_VALUE)
    {
        WbfLog(L"[MFS100SensorAdapter] Failed to connect to bridge pipe: %lu", GetLastError());
        pTask->hrResult = WINBIO_E_DEVICE_FAILURE;
        pTask->RejectDetail = 0;
        pTask->bCompleted = TRUE;
        SetEvent(pTask->Overlapped.hEvent);
        return 0;
    }
    pTask->hActivePipe = hPipe;

    const char szReq[] = "{\"cmd\":\"capture\",\"timeoutMs\":20000}\n";
    DWORD dwWritten = 0;
    WriteFile(hPipe, szReq, (DWORD)strlen(szReq), &dwWritten, NULL);

    char szBuffer[16384];
    if (ReadJsonLine(hPipe, szBuffer, sizeof(szBuffer)))
    {
        WbfLog(L"[MFS100SensorAdapter] Capture response received: %hs", szBuffer);

        const char* pTmpl = strstr(szBuffer, "\"isoTemplate\":\"");
        if (pTmpl != NULL)
        {
            pTmpl += 15;
            const char* pEnd = strchr(pTmpl, '\"');
            if (pEnd != NULL)
            {
                char szIsoB64[4096];
                size_t len = pEnd - pTmpl;
                if (len < sizeof(szIsoB64))
                {
                    memcpy(szIsoB64, pTmpl, len);
                    szIsoB64[len] = '\0';

                    if (pCtx->pCurrentSample)
                    {
                        HeapFree(GetProcessHeap(), 0, pCtx->pCurrentSample);
                        pCtx->pCurrentSample = NULL;
                    }

                    pCtx->pCurrentSample = CreateBirFromTemplate(szIsoB64, pTask->Purpose, &pCtx->cbCurrentSample);
                    if (pCtx->pCurrentSample)
                    {
                        pTask->hrResult = S_OK;
                        pTask->RejectDetail = 0;
                        WbfLog(L"[MFS100SensorAdapter] BIR created successfully (%llu bytes).", pCtx->cbCurrentSample);
                    }
                    else
                    {
                        pTask->hrResult = WINBIO_E_BAD_CAPTURE;
                        pTask->RejectDetail = WINBIO_FP_POOR_QUALITY;
                    }
                }
            }
        }
        else
        {
            if (strstr(szBuffer, "-1141") != NULL || strstr(szBuffer, "cancel") != NULL || strstr(szBuffer, "Cancel") != NULL)
            {
                pTask->hrResult = WINBIO_E_CANCELED;
                pTask->RejectDetail = 0;
                WbfLog(L"[MFS100SensorAdapter] Capture was cancelled (sleep or user). Returning WINBIO_E_CANCELED.");
            }
            else if (strstr(szBuffer, "-1140") != NULL || strstr(szBuffer, "timeout") != NULL || strstr(szBuffer, "Timeout") != NULL)
            {
                pTask->hrResult = WINBIO_E_CANCELED;
                pTask->RejectDetail = 0;
                WbfLog(L"[MFS100SensorAdapter] Capture timed out without finger. Returning WINBIO_E_CANCELED.");
            }
            else
            {
                pTask->hrResult = WINBIO_E_BAD_CAPTURE;
                pTask->RejectDetail = WINBIO_FP_TOO_FAST;
            }
        }
    }
    else
    {
        WbfLog(L"[MFS100SensorAdapter] ReadJsonLine returned FALSE (error=%lu)", GetLastError());
        pTask->hrResult = WINBIO_E_CANCELED;
        pTask->RejectDetail = 0;
    }

    CloseHandle(hPipe);
    pTask->hActivePipe = NULL;
    pTask->bCompleted = TRUE;
    SetEvent(pTask->Overlapped.hEvent);
    return 0;
}

static HRESULT WINAPI SensorAdapterAttach(PWINBIO_PIPELINE Pipeline)
{
    WbfLog(L"[MFS100SensorAdapter] SensorAdapterAttach called.");
    if (Pipeline == NULL) return E_POINTER;

    PMFS100_SENSOR_CONTEXT pCtx = (PMFS100_SENSOR_CONTEXT)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(MFS100_SENSOR_CONTEXT));
    if (!pCtx) return E_OUTOFMEMORY;

    pCtx->hCancelEvent = CreateEvent(NULL, TRUE, FALSE, NULL);
    pCtx->Status = WINBIO_SENSOR_READY;
    pCtx->Subtype = WINBIO_SUBTYPE_NO_INFORMATION;

    Pipeline->SensorContext = (PWINIBIO_SENSOR_CONTEXT)pCtx;
    return S_OK;
}

static HRESULT WINAPI SensorAdapterDetach(PWINBIO_PIPELINE Pipeline)
{
    WbfLog(L"[MFS100SensorAdapter] SensorAdapterDetach called.");
    if (Pipeline == NULL || Pipeline->SensorContext == NULL) return E_POINTER;

    HANDLE hCancel = OpenEventW(EVENT_MODIFY_STATE, FALSE, L"Global\\MFS100_CancelCapture");
    if (hCancel)
    {
        SetEvent(hCancel);
        CloseHandle(hCancel);
    }

    PMFS100_SENSOR_CONTEXT pCtx = (PMFS100_SENSOR_CONTEXT)Pipeline->SensorContext;
    if (pCtx->hCancelEvent) CloseHandle(pCtx->hCancelEvent);
    if (pCtx->pCurrentSample) HeapFree(GetProcessHeap(), 0, pCtx->pCurrentSample);

    HeapFree(GetProcessHeap(), 0, pCtx);
    Pipeline->SensorContext = NULL;
    return S_OK;
}

static HRESULT WINAPI SensorAdapterClearContext(PWINBIO_PIPELINE Pipeline)
{
    WbfLog(L"[MFS100SensorAdapter] SensorAdapterClearContext called.");
    if (Pipeline == NULL || Pipeline->SensorContext == NULL) return E_POINTER;

    PMFS100_SENSOR_CONTEXT pCtx = (PMFS100_SENSOR_CONTEXT)Pipeline->SensorContext;
    if (pCtx->pCurrentSample)
    {
        HeapFree(GetProcessHeap(), 0, pCtx->pCurrentSample);
        pCtx->pCurrentSample = NULL;
        pCtx->cbCurrentSample = 0;
    }
    pCtx->Status = WINBIO_SENSOR_READY;
    return S_OK;
}

static HRESULT WINAPI SensorAdapterQueryStatus(PWINBIO_PIPELINE Pipeline, PWINBIO_SENSOR_STATUS Status)
{
    if (Pipeline == NULL || Status == NULL) return E_POINTER;
    PMFS100_SENSOR_CONTEXT pCtx = (PMFS100_SENSOR_CONTEXT)Pipeline->SensorContext;
    *Status = (pCtx != NULL) ? pCtx->Status : WINBIO_SENSOR_READY;
    WbfLog(L"[MFS100SensorAdapter] SensorAdapterQueryStatus returned: %lu", *Status);
    return S_OK;
}

static HRESULT WINAPI SensorAdapterReset(PWINBIO_PIPELINE Pipeline)
{
    WbfLog(L"[MFS100SensorAdapter] SensorAdapterReset called.");
    if (Pipeline && Pipeline->SensorContext)
    {
        ((PMFS100_SENSOR_CONTEXT)Pipeline->SensorContext)->Status = WINBIO_SENSOR_READY;
    }
    return SensorAdapterClearContext(Pipeline);
}

static HRESULT WINAPI SensorAdapterSetMode(PWINBIO_PIPELINE Pipeline, WINBIO_SENSOR_MODE Mode)
{
    WbfLog(L"[MFS100SensorAdapter] SensorAdapterSetMode: %d", Mode);
    return S_OK;
}

static HRESULT WINAPI SensorAdapterSetIndicatorStatus(PWINBIO_PIPELINE Pipeline, WINBIO_INDICATOR_STATUS IndicatorStatus)
{
    return S_OK;
}

static HRESULT WINAPI SensorAdapterGetIndicatorStatus(PWINBIO_PIPELINE Pipeline, PWINBIO_INDICATOR_STATUS IndicatorStatus)
{
    if (IndicatorStatus == NULL) return E_POINTER;
    *IndicatorStatus = WINBIO_INDICATOR_ON;
    return S_OK;
}

static HRESULT WINAPI SensorAdapterStartCapture(PWINBIO_PIPELINE Pipeline, WINBIO_BIR_PURPOSE Purpose, LPOVERLAPPED *Overlapped)
{
    WbfLog(L"[MFS100SensorAdapter] SensorAdapterStartCapture called. Purpose=0x%X", Purpose);
    if (Pipeline == NULL || Overlapped == NULL) return E_POINTER;

    if (Pipeline->SensorContext)
    {
        ((PMFS100_SENSOR_CONTEXT)Pipeline->SensorContext)->Status = WINBIO_SENSOR_BUSY;
    }

    ZeroMemory(&g_CaptureTask, sizeof(CAPTURE_TASK));
    g_CaptureTask.Pipeline = Pipeline;
    g_CaptureTask.Purpose = Purpose;
    g_CaptureTask.Overlapped.hEvent = CreateEvent(NULL, TRUE, FALSE, NULL);

    *Overlapped = &g_CaptureTask.Overlapped;

    g_CaptureTask.hThread = CreateThread(NULL, 0, CaptureThreadProc, &g_CaptureTask, 0, NULL);
    if (!g_CaptureTask.hThread)
    {
        CloseHandle(g_CaptureTask.Overlapped.hEvent);
        if (Pipeline->SensorContext)
        {
            ((PMFS100_SENSOR_CONTEXT)Pipeline->SensorContext)->Status = WINBIO_SENSOR_READY;
        }
        return HRESULT_FROM_WIN32(GetLastError());
    }

    return S_OK;
}

static HRESULT WINAPI SensorAdapterFinishCapture(PWINBIO_PIPELINE Pipeline, PWINBIO_REJECT_DETAIL RejectDetail)
{
    WbfLog(L"[MFS100SensorAdapter] SensorAdapterFinishCapture called.");
    if (RejectDetail) *RejectDetail = g_CaptureTask.RejectDetail;

    if (g_CaptureTask.hThread)
    {
        WaitForSingleObject(g_CaptureTask.hThread, INFINITE);
        CloseHandle(g_CaptureTask.hThread);
        g_CaptureTask.hThread = NULL;
    }
    if (g_CaptureTask.Overlapped.hEvent)
    {
        CloseHandle(g_CaptureTask.Overlapped.hEvent);
        g_CaptureTask.Overlapped.hEvent = NULL;
    }

    if (Pipeline && Pipeline->SensorContext)
    {
        ((PMFS100_SENSOR_CONTEXT)Pipeline->SensorContext)->Status = WINBIO_SENSOR_READY;
    }

    WbfLog(L"[MFS100SensorAdapter] SensorAdapterFinishCapture result: hr=0x%08X reject=%lu", g_CaptureTask.hrResult, g_CaptureTask.RejectDetail);
    return g_CaptureTask.hrResult;
}

static HRESULT WINAPI SensorAdapterExportSensorData(PWINBIO_PIPELINE Pipeline, PWINBIO_BIR *SampleBuffer, PSIZE_T SampleSize)
{
    if (Pipeline == NULL || SampleBuffer == NULL || SampleSize == NULL) return E_POINTER;
    PMFS100_SENSOR_CONTEXT pCtx = (PMFS100_SENSOR_CONTEXT)Pipeline->SensorContext;
    if (!pCtx || !pCtx->pCurrentSample) return WINBIO_E_NO_CAPTURE_DATA;

    *SampleBuffer = pCtx->pCurrentSample;
    *SampleSize = pCtx->cbCurrentSample;
    return S_OK;
}

static HRESULT WINAPI SensorAdapterCancel(PWINBIO_PIPELINE Pipeline)
{
    WbfLog(L"[MFS100SensorAdapter] SensorAdapterCancel called.");

    // Signal global cancel event so worker immediately stops optical scan (turns off red LED)
    HANDLE hCancel = OpenEventW(EVENT_MODIFY_STATE, FALSE, L"Global\\MFS100_CancelCapture");
    if (hCancel)
    {
        SetEvent(hCancel);
        CloseHandle(hCancel);
    }

    if (g_CaptureTask.hActivePipe != NULL)
    {
        CancelIoEx(g_CaptureTask.hActivePipe, NULL);
        CloseHandle(g_CaptureTask.hActivePipe);
        g_CaptureTask.hActivePipe = NULL;
    }
    if (Pipeline && Pipeline->SensorContext)
    {
        ((PMFS100_SENSOR_CONTEXT)Pipeline->SensorContext)->Status = WINBIO_SENSOR_READY;
    }
    return S_OK;
}

static HRESULT WINAPI SensorAdapterPushDataToEngine(PWINBIO_PIPELINE Pipeline, WINBIO_BIR_PURPOSE Purpose, WINBIO_BIR_DATA_FLAGS Flags, PWINBIO_REJECT_DETAIL RejectDetail)
{
    WbfLog(L"[MFS100SensorAdapter] SensorAdapterPushDataToEngine called.");
    if (Pipeline == NULL || Pipeline->SensorContext == NULL) return E_POINTER;
    PMFS100_SENSOR_CONTEXT pCtx = (PMFS100_SENSOR_CONTEXT)Pipeline->SensorContext;
    if (!pCtx->pCurrentSample) return WINBIO_E_NO_CAPTURE_DATA;

    return WbioEngineAcceptSampleData(Pipeline, pCtx->pCurrentSample, pCtx->cbCurrentSample, Purpose, RejectDetail);
}

static HRESULT WINAPI SensorAdapterControlUnit(PWINBIO_PIPELINE Pipeline, ULONG ControlCode, PUCHAR SendBuffer, SIZE_T SendBufferSize, PUCHAR ReceiveBuffer, SIZE_T ReceiveBufferSize, PSIZE_T ReceiveDataSize, PULONG OperationStatus)
{
    return E_NOTIMPL;
}

static HRESULT WINAPI SensorAdapterControlUnitPrivileged(PWINBIO_PIPELINE Pipeline, ULONG ControlCode, PUCHAR SendBuffer, SIZE_T SendBufferSize, PUCHAR ReceiveBuffer, SIZE_T ReceiveBufferSize, PSIZE_T ReceiveDataSize, PULONG OperationStatus)
{
    if (OperationStatus) *OperationStatus = 0;
    return WINBIO_E_UNSUPPORTED_DATA_FORMAT;
}
