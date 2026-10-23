#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#include <windows.h>
#include <strsafe.h>
#include <shlwapi.h>
#include <credentialprovider.h>
#include <shlguid.h>
#include <ntsecapi.h>
#define SECURITY_WIN32
#include <security.h>

#include "guid.h"

// Field indices on the Logon UI tile
enum MFS100_FIELD_ID
{
    FID_LOGO = 0,
    FID_LARGE_TEXT,
    FID_SMALL_TEXT,
    FID_USERNAME,
    FID_PASSWORD,
    FID_SUBMIT,
    FID_NUM_FIELDS
};

// Logging helper for Winlogon debugging
inline void LogDebug(PCWSTR pszFormat, ...)
{
    WCHAR szBuffer[1024];
    va_list args;
    va_start(args, pszFormat);
    StringCchVPrintfW(szBuffer, ARRAYSIZE(szBuffer), pszFormat, args);
    va_end(args);

    OutputDebugStringW(szBuffer);

    // Also write to ProgramData log file if accessible
    WCHAR szLogPath[MAX_PATH];
    if (GetEnvironmentVariableW(L"ProgramData", szLogPath, MAX_PATH))
    {
        StringCchCatW(szLogPath, MAX_PATH, L"\\MFS100\\MFS100CP.log");
        CreateDirectoryW(L"C:\\ProgramData\\MFS100", NULL);
        HANDLE hFile = CreateFileW(szLogPath, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                   NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        if (hFile != INVALID_HANDLE_VALUE)
        {
            DWORD dwWritten = 0;
            char szAnsi[2048];
            int len = WideCharToMultiByte(CP_UTF8, 0, szBuffer, -1, szAnsi, sizeof(szAnsi), NULL, NULL);
            if (len > 0)
            {
                WriteFile(hFile, szAnsi, len - 1, &dwWritten, NULL);
                WriteFile(hFile, "\r\n", 2, &dwWritten, NULL);
            }
            CloseHandle(hFile);
        }
    }
}
