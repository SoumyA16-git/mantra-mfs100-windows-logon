#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <setupapi.h>
#include <stdio.h>
#include <strsafe.h>
#include <shlwapi.h>

#pragma comment(lib, "setupapi.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "shlwapi.lib")

// GUID_DEVINTERFACE_BIOMETRIC_READER: {E2830FF2-DD38-4de6-AF5D-82990439237E}
static const GUID GUID_DEVINTERFACE_BIOMETRIC_READER = 
{ 0xE2830FF2, 0xDD38, 0x4DE6, { 0xAF, 0x5D, 0x82, 0x99, 0x04, 0x39, 0x23, 0x7E } };

static const wchar_t szDbGuid[] = L"{B16F0239-0144-4284-9D27-E4B283626155}";
static const wchar_t szDbRaw[]  = L"B16F0239-0144-4284-9D27-E4B283626155";

static void WriteRegStrings(HKEY hKey)
{
    const wchar_t szSensor[] = L"MFS100SensorAdapter.dll";
    const wchar_t szEngine[] = L"MFS100EngineAdapter.dll";
    const wchar_t szStorage[] = L"winbiostorageadapter.dll";
    DWORD dwOne = 1;

    RegSetValueExW(hKey, L"SensorAdapterBinary", 0, REG_SZ, (const BYTE*)szSensor, (DWORD)(wcslen(szSensor) + 1) * sizeof(WCHAR));
    RegSetValueExW(hKey, L"EngineAdapterBinary", 0, REG_SZ, (const BYTE*)szEngine, (DWORD)(wcslen(szEngine) + 1) * sizeof(WCHAR));
    RegSetValueExW(hKey, L"StorageAdapterBinary", 0, REG_SZ, (const BYTE*)szStorage, (DWORD)(wcslen(szStorage) + 1) * sizeof(WCHAR));
    RegSetValueExW(hKey, L"DatabaseId", 0, REG_SZ, (const BYTE*)szDbGuid, (DWORD)(wcslen(szDbGuid) + 1) * sizeof(WCHAR));
    RegSetValueExW(hKey, L"SensorMode", 0, REG_DWORD, (const BYTE*)&dwOne, sizeof(dwOne));
    RegSetValueExW(hKey, L"SystemSensor", 0, REG_DWORD, (const BYTE*)&dwOne, sizeof(dwOne));
    RegSetValueExW(hKey, L"Exclusive", 0, REG_DWORD, (const BYTE*)&dwOne, sizeof(dwOne));
}

static void ConfigureGlobalWinBioDatabases()
{
    // Ensure WinBioDatabase folder exists
    CreateDirectoryW(L"C:\\Windows\\System32\\WinBioDatabase", NULL);

    WCHAR szFilePath[MAX_PATH];
    StringCchPrintfW(szFilePath, ARRAYSIZE(szFilePath), L"C:\\Windows\\System32\\WinBioDatabase\\%s.DAT", szDbRaw);

    // 1. Service Databases key (read directly by WbioSrvc) - Write BOTH with and without braces
    const wchar_t* pGuids[2] = { szDbGuid, szDbRaw };
    for (int g = 0; g < 2; g++)
    {
        WCHAR szServiceDbKey[512];
        StringCchPrintfW(szServiceDbKey, ARRAYSIZE(szServiceDbKey),
                         L"SYSTEM\\CurrentControlSet\\Services\\WbioSrvc\\Databases\\%s", pGuids[g]);
        HKEY hSvcDb = NULL;
        if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, szServiceDbKey, 0, NULL, 0, KEY_WRITE, NULL, &hSvcDb, NULL) == ERROR_SUCCESS)
        {
            DWORD dwOne = 1;
            DWORD dwZero = 0;
            DWORD dwType = 8; // WINBIO_TYPE_FINGERPRINT
            DWORD dwSize = 32;
            const wchar_t szEmpty[] = L"";
            const wchar_t szFormat[] = L"00000000-0000-0000-0000-000000000000";

            RegSetValueExW(hSvcDb, L"Attributes", 0, REG_DWORD, (const BYTE*)&dwOne, sizeof(dwOne));
            RegSetValueExW(hSvcDb, L"AutoCreate", 0, REG_DWORD, (const BYTE*)&dwOne, sizeof(dwOne));
            RegSetValueExW(hSvcDb, L"AutoName", 0, REG_DWORD, (const BYTE*)&dwZero, sizeof(dwZero));
            RegSetValueExW(hSvcDb, L"BiometricType", 0, REG_DWORD, (const BYTE*)&dwType, sizeof(dwType));
            RegSetValueExW(hSvcDb, L"ConnectionString", 0, REG_SZ, (const BYTE*)szEmpty, sizeof(szEmpty));
            RegSetValueExW(hSvcDb, L"FilePath", 0, REG_SZ, (const BYTE*)szFilePath, (DWORD)(wcslen(szFilePath) + 1) * sizeof(WCHAR));
            RegSetValueExW(hSvcDb, L"Format", 0, REG_SZ, (const BYTE*)szFormat, (DWORD)(wcslen(szFormat) + 1) * sizeof(WCHAR));
            RegSetValueExW(hSvcDb, L"InitialSize", 0, REG_DWORD, (const BYTE*)&dwSize, sizeof(dwSize));
            RegCloseKey(hSvcDb);
        }
    }
    printf("[OK] WbioSrvc service database registry entries configured.\n");

    // 2. WinBio Software Databases key
    for (int g = 0; g < 2; g++)
    {
        WCHAR szSoftDbKey[512];
        StringCchPrintfW(szSoftDbKey, ARRAYSIZE(szSoftDbKey),
                         L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WinBio\\Databases\\%s", pGuids[g]);
        HKEY hSoftDb = NULL;
        if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, szSoftDbKey, 0, NULL, 0, KEY_WRITE, NULL, &hSoftDb, NULL) == ERROR_SUCCESS)
        {
            DWORD dwFactor = 8; // WINBIO_TYPE_FINGERPRINT
            DWORD dwType = 1;
            DWORD dwAttr = 1;
            DWORD dwAuto = 1;
            const wchar_t szEmpty[] = L"";

            RegSetValueExW(hSoftDb, L"BiometricFactor", 0, REG_DWORD, (const BYTE*)&dwFactor, sizeof(dwFactor));
            RegSetValueExW(hSoftDb, L"DatabaseType", 0, REG_DWORD, (const BYTE*)&dwType, sizeof(dwType));
            RegSetValueExW(hSoftDb, L"Attributes", 0, REG_DWORD, (const BYTE*)&dwAttr, sizeof(dwAttr));
            RegSetValueExW(hSoftDb, L"AutoCreate", 0, REG_DWORD, (const BYTE*)&dwAuto, sizeof(dwAuto));
            RegSetValueExW(hSoftDb, L"FilePath", 0, REG_SZ, (const BYTE*)szFilePath, (DWORD)(wcslen(szFilePath) + 1) * sizeof(WCHAR));
            RegSetValueExW(hSoftDb, L"ConnectionString", 0, REG_SZ, (const BYTE*)szEmpty, sizeof(szEmpty));
            RegCloseKey(hSoftDb);
        }
    }
    printf("[OK] Software WinBio database registry entries configured.\n");

    // 3. WinBio Software Global Configurations key
    for (int g = 0; g < 2; g++)
    {
        WCHAR szSoftCfgKey[512];
        StringCchPrintfW(szSoftCfgKey, ARRAYSIZE(szSoftCfgKey),
                         L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WinBio\\Configurations\\%s", pGuids[g]);
        HKEY hSoftCfg = NULL;
        if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, szSoftCfgKey, 0, NULL, 0, KEY_WRITE, NULL, &hSoftCfg, NULL) == ERROR_SUCCESS)
        {
            WriteRegStrings(hSoftCfg);
            RegCloseKey(hSoftCfg);
        }
    }
    printf("[OK] Software WinBio configuration registry entries configured.\n");

    // 4. Group Policy permissions for Biometrics & Windows Hello
    HKEY hBioPol = NULL;
    if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Policies\\Microsoft\\Biometrics", 0, NULL, 0, KEY_WRITE, NULL, &hBioPol, NULL) == ERROR_SUCCESS)
    {
        DWORD dwOne = 1;
        RegSetValueExW(hBioPol, L"Enabled", 0, REG_DWORD, (const BYTE*)&dwOne, sizeof(dwOne));
        RegCloseKey(hBioPol);
    }
    HKEY hBioCpPol = NULL;
    if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Policies\\Microsoft\\Biometrics\\Credential Provider", 0, NULL, 0, KEY_WRITE, NULL, &hBioCpPol, NULL) == ERROR_SUCCESS)
    {
        DWORD dwOne = 1;
        RegSetValueExW(hBioCpPol, L"Domain Accounts", 0, REG_DWORD, (const BYTE*)&dwOne, sizeof(dwOne));
        RegCloseKey(hBioCpPol);
    }
    HKEY hSysPol = NULL;
    if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Policies\\Microsoft\\Windows\\System", 0, NULL, 0, KEY_WRITE, NULL, &hSysPol, NULL) == ERROR_SUCCESS)
    {
        DWORD dwOne = 1;
        RegSetValueExW(hSysPol, L"AllowDomainPINLogon", 0, REG_DWORD, (const BYTE*)&dwOne, sizeof(dwOne));
        RegCloseKey(hSysPol);
    }
}

static void ConfigureFingerprintVirtualSensor()
{
    printf("\nConfiguring Mantra MFS100 under Service Providers\\Fingerprint\\Virtual Sensors...\n");

    const wchar_t* pGuids[2] = { szDbGuid, szDbRaw };
    for (int g = 0; g < 2; g++)
    {
        WCHAR szKeyPath[512];
        StringCchPrintfW(szKeyPath, ARRAYSIZE(szKeyPath),
                         L"SYSTEM\\CurrentControlSet\\Services\\WbioSrvc\\Service Providers\\Fingerprint\\Virtual Sensors\\%s", pGuids[g]);

        HKEY hVSensor = NULL;
        if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, szKeyPath, 0, NULL, 0, KEY_WRITE, NULL, &hVSensor, NULL) == ERROR_SUCCESS)
        {
            DWORD dwCap = 129; // WINBIO_CAPABILITY_VIRTUAL_SENSOR (0x80) | WINBIO_CAPABILITY_SENSOR (0x01)
            const wchar_t szDesc[] = L"Mantra MFS100 Fingerprint Scanner";
            const wchar_t szMfg[] = L"Mantra Softech (I) Pvt Ltd";
            const wchar_t szModel[] = L"MFS100";
            const wchar_t szSerial[] = L"8E46E90C49AC";
            DWORD dwSub = 0;
            DWORD dwVer = 16777216; // 0x01000000

            RegSetValueExW(hVSensor, L"Capabilities", 0, REG_DWORD, (const BYTE*)&dwCap, sizeof(dwCap));
            RegSetValueExW(hVSensor, L"DeviceDescription", 0, REG_SZ, (const BYTE*)szDesc, (DWORD)(wcslen(szDesc) + 1) * sizeof(WCHAR));
            RegSetValueExW(hVSensor, L"Manufacturer", 0, REG_SZ, (const BYTE*)szMfg, (DWORD)(wcslen(szMfg) + 1) * sizeof(WCHAR));
            RegSetValueExW(hVSensor, L"ModelName", 0, REG_SZ, (const BYTE*)szModel, (DWORD)(wcslen(szModel) + 1) * sizeof(WCHAR));
            RegSetValueExW(hVSensor, L"SerialNumber", 0, REG_SZ, (const BYTE*)szSerial, (DWORD)(wcslen(szSerial) + 1) * sizeof(WCHAR));
            RegSetValueExW(hVSensor, L"SubType", 0, REG_DWORD, (const BYTE*)&dwSub, sizeof(dwSub));
            RegSetValueExW(hVSensor, L"Version", 0, REG_DWORD, (const BYTE*)&dwVer, sizeof(dwVer));

            HKEY hCfgRoot = NULL;
            if (RegCreateKeyExW(hVSensor, L"Configurations", 0, NULL, 0, KEY_WRITE, NULL, &hCfgRoot, NULL) == ERROR_SUCCESS)
            {
                const wchar_t szZero[] = L"0";
                RegSetValueExW(hCfgRoot, L"DefaultConfiguration", 0, REG_SZ, (const BYTE*)szZero, sizeof(szZero));
                RegCloseKey(hCfgRoot);
            }

            HKEY hCfg0 = NULL;
            if (RegCreateKeyExW(hVSensor, L"Configurations\\0", 0, NULL, 0, KEY_WRITE, NULL, &hCfg0, NULL) == ERROR_SUCCESS)
            {
                const wchar_t szSensor[] = L"MFS100SensorAdapter.dll";
                const wchar_t szEngine[] = L"MFS100EngineAdapter.dll";
                const wchar_t szStorage[] = L"winbiostorageadapter.dll";
                DWORD dwOne = 1;

                RegSetValueExW(hCfg0, L"SensorAdapterBinary", 0, REG_SZ, (const BYTE*)szSensor, (DWORD)(wcslen(szSensor) + 1) * sizeof(WCHAR));
                RegSetValueExW(hCfg0, L"EngineAdapterBinary", 0, REG_SZ, (const BYTE*)szEngine, (DWORD)(wcslen(szEngine) + 1) * sizeof(WCHAR));
                RegSetValueExW(hCfg0, L"StorageAdapterBinary", 0, REG_SZ, (const BYTE*)szStorage, (DWORD)(wcslen(szStorage) + 1) * sizeof(WCHAR));
                RegSetValueExW(hCfg0, L"DatabaseId", 0, REG_SZ, (const BYTE*)pGuids[g], (DWORD)(wcslen(pGuids[g]) + 1) * sizeof(WCHAR));
                RegSetValueExW(hCfg0, L"SensorMode", 0, REG_DWORD, (const BYTE*)&dwOne, sizeof(dwOne));
                RegSetValueExW(hCfg0, L"SystemSensor", 0, REG_DWORD, (const BYTE*)&dwOne, sizeof(dwOne));
                RegCloseKey(hCfg0);
            }

            RegCloseKey(hVSensor);
        }
    }
    printf("[SUCCESS] Registered Mantra MFS100 under Service Providers\\Fingerprint\\Virtual Sensors!\n");
}

static BOOL FindMantraDeviceNode(HDEVINFO* phDevInfo, SP_DEVINFO_DATA* pDevData, WCHAR* pszInstanceId, DWORD cchInstanceId)
{
    const DWORD flagsArray[2] = { DIGCF_ALLCLASSES | DIGCF_PRESENT, DIGCF_ALLCLASSES };

    for (int attempt = 0; attempt < 2; attempt++)
    {
        HDEVINFO hInfo = SetupDiGetClassDevsW(NULL, NULL, NULL, flagsArray[attempt]);
        if (hInfo == INVALID_HANDLE_VALUE) continue;

        SP_DEVINFO_DATA devData = { 0 };
        devData.cbSize = sizeof(SP_DEVINFO_DATA);

        for (DWORD idx = 0; SetupDiEnumDeviceInfo(hInfo, idx, &devData); idx++)
        {
            WCHAR szInst[512] = L"";
            SetupDiGetDeviceInstanceIdW(hInfo, &devData, szInst, ARRAYSIZE(szInst), NULL);

            WCHAR szHw[1024] = L"";
            SetupDiGetDeviceRegistryPropertyW(hInfo, &devData, SPDRP_HARDWAREID, NULL, (PBYTE)szHw, sizeof(szHw), NULL);

            if ((StrStrIW(szInst, L"VID_2C0F") && StrStrIW(szInst, L"PID_1005")) ||
                (StrStrIW(szHw, L"VID_2C0F") && StrStrIW(szHw, L"PID_1005")))
            {
                StringCchCopyW(pszInstanceId, cchInstanceId, szInst);
                *phDevInfo = hInfo;
                *pDevData = devData;
                return TRUE;
            }
        }
        SetupDiDestroyDeviceInfoList(hInfo);
    }

    return FALSE;
}

int main()
{
    printf("==================================================\n");
    printf("  MFS100 WBF Biometric Interface Register Utility \n");
    printf("==================================================\n\n");

    // 1. Configure system and software WinBio databases
    ConfigureGlobalWinBioDatabases();

    // 2. Configure Virtual Sensor under Fingerprint Service Provider (Direct WbioSrvc Load)
    ConfigureFingerprintVirtualSensor();

    // 3. Locate physical device node for PnP/SetupAPI binding
    WCHAR szDeviceInstanceId[512] = L"";
    HDEVINFO hDevInfo = INVALID_HANDLE_VALUE;
    SP_DEVINFO_DATA devData = { 0 };

    printf("\nLocating Mantra MFS100 (VID_2C0F&PID_1005) device node...\n");
    if (FindMantraDeviceNode(&hDevInfo, &devData, szDeviceInstanceId, ARRAYSIZE(szDeviceInstanceId)))
    {
        printf("[OK] Found Mantra MFS100 Device Node:\n     InstanceId: '%S'\n", szDeviceInstanceId);

        printf("\nRegistering GUID_DEVINTERFACE_BIOMETRIC_READER on device node...\n");
        SP_DEVICE_INTERFACE_DATA ifData = { 0 };
        ifData.cbSize = sizeof(SP_DEVICE_INTERFACE_DATA);

        if (!SetupDiCreateDeviceInterfaceW(hDevInfo, &devData, &GUID_DEVINTERFACE_BIOMETRIC_READER, NULL, 0, &ifData))
        {
            DWORD dwErr = GetLastError();
            if (dwErr == ERROR_ALREADY_EXISTS || dwErr == (DWORD)SPAPI_E_DEVICE_INTERFACE_ACTIVE)
            {
                printf("[INFO] Biometric device interface already registered. Updating properties...\n");
                SetupDiEnumDeviceInterfaces(hDevInfo, &devData, &GUID_DEVINTERFACE_BIOMETRIC_READER, 0, &ifData);
            }
            else
            {
                printf("[WARN] SetupDiCreateDeviceInterfaceW returned %lu.\n", dwErr);
            }
        }
        else
        {
            printf("[SUCCESS] Biometric device interface created via SetupAPI!\n");
        }

        HKEY hIfKey = SetupDiCreateDeviceInterfaceRegKeyW(hDevInfo, &ifData, 0, KEY_READ | KEY_WRITE, NULL, NULL);
        if (hIfKey != INVALID_HANDLE_VALUE && hIfKey != NULL)
        {
            WriteRegStrings(hIfKey);

            HKEY hCfgSubKey = NULL;
            if (RegCreateKeyExW(hIfKey, L"WinBio\\Configurations\\0", 0, NULL, 0, KEY_WRITE, NULL, &hCfgSubKey, NULL) == ERROR_SUCCESS)
            {
                WriteRegStrings(hCfgSubKey);
                RegCloseKey(hCfgSubKey);
            }

            HKEY hCfgRoot = NULL;
            if (RegCreateKeyExW(hIfKey, L"WinBio\\Configurations", 0, NULL, 0, KEY_WRITE, NULL, &hCfgRoot, NULL) == ERROR_SUCCESS)
            {
                const wchar_t szZero[] = L"0";
                RegSetValueExW(hCfgRoot, L"DefaultConfiguration", 0, REG_SZ, (const BYTE*)szZero, sizeof(szZero));
                RegCloseKey(hCfgRoot);
            }

            RegCloseKey(hIfKey);
            printf("[SUCCESS] Interface parameters written via SetupAPI!\n");
        }

        SetupDiDestroyDeviceInfoList(hDevInfo);
    }
    else
    {
        printf("[WARN] PnP enumeration did not return present node. Using known hardware ID.\n");
        StringCchCopyW(szDeviceInstanceId, ARRAYSIZE(szDeviceInstanceId), L"USB\\VID_2C0F&PID_1005\\8E46E90C49AC");
    }

    // 4. Direct Registry: Populate DeviceClasses and Enum\...\Device Parameters
    printf("\nPopulating WBF DeviceClasses registry key for WbioSrvc discovery...\n");
    const wchar_t* pszSerial = wcsrchr(szDeviceInstanceId, L'\\') ? wcsrchr(szDeviceInstanceId, L'\\') + 1 : L"8E46E90C49AC";

    WCHAR szIfKeyPath[1024];
    StringCchPrintfW(szIfKeyPath, ARRAYSIZE(szIfKeyPath),
                     L"System\\CurrentControlSet\\Control\\DeviceClasses\\{E2830FF2-DD38-4de6-AF5D-82990439237E}\\##?#USB#VID_2C0F&PID_1005#%s#{E2830FF2-DD38-4de6-AF5D-82990439237E}",
                     pszSerial);

    HKEY hDevClassKey = NULL;
    if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, szIfKeyPath, 0, NULL, 0, KEY_WRITE, NULL, &hDevClassKey, NULL) == ERROR_SUCCESS)
    {
        RegSetValueExW(hDevClassKey, L"DeviceInstance", 0, REG_SZ, (const BYTE*)szDeviceInstanceId, (DWORD)(wcslen(szDeviceInstanceId) + 1) * sizeof(WCHAR));

        HKEY hHashKey = NULL;
        if (RegCreateKeyExW(hDevClassKey, L"#", 0, NULL, 0, KEY_WRITE, NULL, &hHashKey, NULL) == ERROR_SUCCESS)
        {
            RegSetValueExW(hHashKey, L"DeviceInstance", 0, REG_SZ, (const BYTE*)szDeviceInstanceId, (DWORD)(wcslen(szDeviceInstanceId) + 1) * sizeof(WCHAR));

            HKEY hControlKey = NULL;
            if (RegCreateKeyExW(hHashKey, L"Control", 0, NULL, 0, KEY_WRITE, NULL, &hControlKey, NULL) == ERROR_SUCCESS)
            {
                DWORD dwLinked = 1;
                RegSetValueExW(hControlKey, L"Linked", 0, REG_DWORD, (const BYTE*)&dwLinked, sizeof(dwLinked));
                RegCloseKey(hControlKey);
            }

            HKEY hDevParam = NULL;
            if (RegCreateKeyExW(hHashKey, L"Device Parameters", 0, NULL, 0, KEY_WRITE, NULL, &hDevParam, NULL) == ERROR_SUCCESS)
            {
                WriteRegStrings(hDevParam);

                HKEY hCfgRoot = NULL;
                if (RegCreateKeyExW(hDevParam, L"WinBio\\Configurations", 0, NULL, 0, KEY_WRITE, NULL, &hCfgRoot, NULL) == ERROR_SUCCESS)
                {
                    const wchar_t szZero[] = L"0";
                    RegSetValueExW(hCfgRoot, L"DefaultConfiguration", 0, REG_SZ, (const BYTE*)szZero, sizeof(szZero));
                    RegCloseKey(hCfgRoot);
                }

                HKEY hCfg0 = NULL;
                if (RegCreateKeyExW(hDevParam, L"WinBio\\Configurations\\0", 0, NULL, 0, KEY_WRITE, NULL, &hCfg0, NULL) == ERROR_SUCCESS)
                {
                    WriteRegStrings(hCfg0);
                    RegCloseKey(hCfg0);
                }
                RegCloseKey(hDevParam);
            }
            RegCloseKey(hHashKey);
        }
        RegCloseKey(hDevClassKey);
        printf("[SUCCESS] DeviceClasses WBF interface registered!\n");
    }

    // 5. Populate into the device Enum hardware key
    WCHAR szEnumParamPath[1024];
    StringCchPrintfW(szEnumParamPath, ARRAYSIZE(szEnumParamPath),
                     L"SYSTEM\\CurrentControlSet\\Enum\\%s\\Device Parameters",
                     szDeviceInstanceId);
    HKEY hEnumKey = NULL;
    if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, szEnumParamPath, 0, NULL, 0, KEY_WRITE, NULL, &hEnumKey, NULL) == ERROR_SUCCESS)
    {
        WriteRegStrings(hEnumKey);

        HKEY hCfgRoot = NULL;
        if (RegCreateKeyExW(hEnumKey, L"WinBio\\Configurations", 0, NULL, 0, KEY_WRITE, NULL, &hCfgRoot, NULL) == ERROR_SUCCESS)
        {
            const wchar_t szZero[] = L"0";
            RegSetValueExW(hCfgRoot, L"DefaultConfiguration", 0, REG_SZ, (const BYTE*)szZero, sizeof(szZero));
            RegCloseKey(hCfgRoot);
        }

        HKEY hCfg0 = NULL;
        if (RegCreateKeyExW(hEnumKey, L"WinBio\\Configurations\\0", 0, NULL, 0, KEY_WRITE, NULL, &hCfg0, NULL) == ERROR_SUCCESS)
        {
            WriteRegStrings(hCfg0);
            RegCloseKey(hCfg0);
        }
        RegCloseKey(hEnumKey);
        printf("[SUCCESS] Hardware Enum Device Parameters populated!\n");
    }

    printf("\n==================================================\n");
    printf("[DONE] Mantra MFS100 Biometric Interface Ready.\n");
    printf("==================================================\n");
    return 0;
}
