#ifndef NTDDI_VERSION
#define NTDDI_VERSION NTDDI_WIN7
#endif
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0601
#endif
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#ifndef ARGUMENT_PRESENT
#define ARGUMENT_PRESENT(ArgumentPointer) ((CHAR*)(ArgumentPointer) != (CHAR*)NULL)
#endif

#include <winbio_types.h>
#include <winbio_adapter.h>
#include <winbio_err.h>
#include <strsafe.h>

// GUID for MFS100 WBF Engine Adapter
// {A328956F-43BB-4F15-B2A8-A63051012C79}
static const GUID GUID_MFS100_ENGINE_ADAPTER = 
{ 0xa328956f, 0x43bb, 0x4f15, { 0xb2, 0xa8, 0xa6, 0x30, 0x51, 0x01, 0x2c, 0x79 } };

#define MFS100_ENROLLMENT_STEPS_NEEDED 3

// Context for MFS100 Engine Adapter instance
typedef struct _MFS100_ENGINE_CONTEXT {
    UCHAR CurrentFeatureSet[8192];
    SIZE_T cbCurrentFeatureSet;
    UCHAR EnrollmentTemplate[16384];
    SIZE_T cbEnrollmentTemplate;
    UCHAR EnrollmentHash[64];
    SIZE_T cbEnrollmentHash;
    UCHAR MatchedPayloadBlob[8192];
    SIZE_T cbMatchedPayloadBlob;
    char szSelectedHashOid[64];
    DWORD dwEnrollmentCount;
    DWORD dwEnrollmentTarget;
    WINBIO_REJECT_DETAIL LastRejectDetail;
} MFS100_ENGINE_CONTEXT, *PMFS100_ENGINE_CONTEXT;

inline void WbfLog(PCWSTR pszFormat, ...)
{
    WCHAR szBuffer[2048];
    va_list args;
    va_start(args, pszFormat);
    StringCchVPrintfW(szBuffer, ARRAYSIZE(szBuffer), pszFormat, args);
    va_end(args);
    OutputDebugStringW(szBuffer);

    CreateDirectoryW(L"C:\\ProgramData\\MFS100CP", NULL);
    CreateDirectoryW(L"C:\\ProgramData\\MFS100CP\\logs", NULL);
    FILE* fp = NULL;
    if (_wfopen_s(&fp, L"C:\\ProgramData\\MFS100CP\\logs\\wbf_adapters.log", L"a, ccs=UTF-8") == 0 && fp)
    {
        SYSTEMTIME st;
        GetLocalTime(&st);
        fwprintf(fp, L"%02d:%02d:%02d.%03d %s\n", st.wHour, st.wMinute, st.wSecond, st.wMilliseconds, szBuffer);
        fclose(fp);
    }
}


