#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <winbio.h>
#include <winbio_err.h>

#pragma comment(lib, "winbio.lib")

void PrintHr(const char* action, HRESULT hr)
{
    if (SUCCEEDED(hr))
    {
        printf("[SUCCESS] %s (0x%08X)\n", action, hr);
    }
    else
    {
        printf("[FAILED]  %s (0x%08X)\n", action, hr);
    }
}

int main()
{
    printf("==================================================\n");
    printf("  Mantra MFS100 WBF Diagnostic Verification Tool \n");
    printf("==================================================\n\n");

    // 1. Enumerate Biometric Units
    printf("Stage 1: Enumerating WBF Biometric Units...\n");
    PWINBIO_UNIT_SCHEMA unitSchemas = NULL;
    SIZE_T unitCount = 0;
    HRESULT hr = WinBioEnumBiometricUnits(WINBIO_TYPE_FINGERPRINT, &unitSchemas, &unitCount);
    PrintHr("WinBioEnumBiometricUnits", hr);

    if (FAILED(hr) || unitCount == 0)
    {
        printf("\n[ERROR] No fingerprint biometric units found by Windows Biometric Service (WbioSrvc)!\n");
        printf("Ensure the WBF driver/adapters and INF are registered and WbioSrvc is running.\n");
        return 1;
    }

    WINBIO_UNIT_ID targetUnitId = 0;
    for (SIZE_T i = 0; i < unitCount; i++)
    {
        printf("  Unit [%llu]: ID=%lu, Pool=%d, Device='%S', Description='%S'\n",
               i,
               unitSchemas[i].UnitId,
               unitSchemas[i].PoolType,
               unitSchemas[i].DeviceInstanceId,
               unitSchemas[i].Description);
        if (targetUnitId == 0)
        {
            targetUnitId = unitSchemas[i].UnitId;
        }
    }
    WinBioFree(unitSchemas);

    // 2. Open Biometric Session
    printf("\nStage 2: Opening WBF System Biometric Session...\n");
    WINBIO_SESSION_HANDLE sessionHandle = 0;
    hr = WinBioOpenSession(WINBIO_TYPE_FINGERPRINT, WINBIO_POOL_SYSTEM, WINBIO_FLAG_RAW, NULL, 0, NULL, &sessionHandle);
    PrintHr("WinBioOpenSession", hr);
    if (FAILED(hr))
    {
        printf("[ERROR] Failed to open WBF session: 0x%08X\n", hr);
        return 2;
    }

    // 3. Locate Sensor (Prompt touch)
    printf("\nStage 3: Testing Sensor Touch Detection (WinBioLocateSensor)...\n");
    printf("Please touch the Mantra MFS100 fingerprint sensor now...\n");
    WINBIO_UNIT_ID locatedUnit = 0;
    hr = WinBioLocateSensor(sessionHandle, &locatedUnit);
    PrintHr("WinBioLocateSensor", hr);
    if (SUCCEEDED(hr))
    {
        printf("[OK] Sensor touch successfully detected on Unit %lu!\n", locatedUnit);
    }

    // 4. Test Windows Hello Enrollment Cycle (3 touches)
    printf("\nStage 4: Testing WBF Enrollment Pipeline (WinBioEnrollBegin)...\n");
    hr = WinBioEnrollBegin(sessionHandle, WINBIO_ANSI_381_POS_RH_INDEX_FINGER, targetUnitId);
    PrintHr("WinBioEnrollBegin", hr);

    if (SUCCEEDED(hr))
    {
        WINBIO_REJECT_DETAIL rejectDetail = 0;
        for (int swipe = 1; swipe <= 3; swipe++)
        {
            printf("\n  [Touch %d/3] Please touch your enrolled finger on the sensor...\n", swipe);
            hr = WinBioEnrollCapture(sessionHandle, &rejectDetail);
            if (hr == WINBIO_I_MORE_DATA)
            {
                printf("  [OK] Touch %d registered. More sample data needed.\n", swipe);
            }
            else if (SUCCEEDED(hr))
            {
                printf("  [OK] Enrollment samples complete! Ready to commit.\n");
                break;
            }
            else
            {
                printf("  [FAIL] Capture rejected with detail: %lu (hr=0x%08X)\n", rejectDetail, hr);
                break;
            }
        }
        WinBioEnrollDiscard(sessionHandle);
        printf("  [INFO] Discarded diagnostic test enrollment.\n");
    }

    // 5. Cleanup
    WinBioCloseSession(sessionHandle);
    printf("\n==================================================\n");
    printf("[DONE] WBF Diagnostic Verification Finished.\n");
    printf("==================================================\n");
    return 0;
}
