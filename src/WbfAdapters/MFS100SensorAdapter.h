#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#ifndef ARGUMENT_PRESENT
#define ARGUMENT_PRESENT(ArgumentPointer) ((CHAR*)(ArgumentPointer) != (CHAR*)NULL)
#endif

#include <winbio_types.h>
#include <winbio_adapter.h>
#include <winbio_err.h>
#include <strsafe.h>

// GUIDs for MFS100 WBF Adapters
// {56EFA0F1-177D-4DE7-B3CA-6DC918C4E612}
static const GUID GUID_MFS100_SENSOR_ADAPTER = 
{ 0x56efa0f1, 0x177d, 0x4de7, { 0xb3, 0xca, 0x6d, 0xc9, 0x18, 0xc4, 0xe6, 0x12 } };

// Context for MFS100 Sensor Adapter instance
typedef struct _MFS100_SENSOR_CONTEXT {
    HANDLE hPipe;
    HANDLE hCancelEvent;
    PWINBIO_BIR pCurrentSample;
    SIZE_T cbCurrentSample;
    WINBIO_SENSOR_STATUS Status;
    WINBIO_BIOMETRIC_SUBTYPE Subtype;
} MFS100_SENSOR_CONTEXT, *PMFS100_SENSOR_CONTEXT;

// Helper to log debug strings to OutputDebugStringW
inline void WbfLog(PCWSTR pszFormat, ...)
{
    WCHAR szBuffer[1024];
    va_list args;
    va_start(args, pszFormat);
    StringCchVPrintfW(szBuffer, ARRAYSIZE(szBuffer), pszFormat, args);
    va_end(args);
    OutputDebugStringW(szBuffer);
}
