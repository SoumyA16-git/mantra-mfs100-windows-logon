@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ==================================================
echo   Registering Mantra MFS100 WBF Adapters...
echo ==================================================

net session >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Please right-click this script and select "Run as administrator"!
    pause
    exit /b 1
)

:: 1. Ensure MFS100 Bridge Service is installed and running
echo 1. Checking MFS100 Bridge Service...
sc query MFS100BridgeSvc >nul 2>&1
if errorlevel 1 (
    if exist "%~dp04_Install_And_Start_Service.bat" (
        echo    Installing MFS100 Bridge Service...
        call "%~dp04_Install_And_Start_Service.bat" >nul 2>&1
    )
) else (
    net start MFS100BridgeSvc >nul 2>&1
)

:: 2. Stop Windows Biometric Service during registration
echo 2. Stopping Windows Biometric Service (WbioSrvc)...
net stop WbioSrvc >nul 2>&1
timeout /t 1 /nobreak >nul

:: 3. Deploy Adapter DLLs to System32
echo 3. Copying WBF adapter binaries to System32...
copy /y "%~dp0MFS100SensorAdapter.dll" "%SystemRoot%\System32\MFS100SensorAdapter.dll"
copy /y "%~dp0MFS100EngineAdapter.dll" "%SystemRoot%\System32\MFS100EngineAdapter.dll"

:: 4. Configure WBF Databases in Registry
echo 4. Configuring WinBio Databases...
set DB_KEY=HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WinBio\Databases\{B16F0239-0144-4284-9D27-E4B283626155}
reg add "!DB_KEY!" /v "BiometricFactor" /t REG_DWORD /d 8 /f >nul
reg add "!DB_KEY!" /v "DatabaseType" /t REG_DWORD /d 1 /f >nul
reg add "!DB_KEY!" /v "FilePath" /t REG_SZ /d "" /f >nul
reg add "!DB_KEY!" /v "ConnectionString" /t REG_SZ /d "" /f >nul
reg add "!DB_KEY!" /v "Attributes" /t REG_DWORD /d 1 /f >nul
reg add "!DB_KEY!" /v "AutoCreate" /t REG_DWORD /d 1 /f >nul

set SVC_DB_KEY=HKLM\SYSTEM\CurrentControlSet\Services\WbioSrvc\Databases\{B16F0239-0144-4284-9D27-E4B283626155}
reg add "!SVC_DB_KEY!" /v "Attributes" /t REG_DWORD /d 1 /f >nul
reg add "!SVC_DB_KEY!" /v "AutoCreate" /t REG_DWORD /d 1 /f >nul
reg add "!SVC_DB_KEY!" /v "AutoName" /t REG_DWORD /d 0 /f >nul
reg add "!SVC_DB_KEY!" /v "BiometricType" /t REG_DWORD /d 8 /f >nul
reg add "!SVC_DB_KEY!" /v "ConnectionString" /t REG_SZ /d "" /f >nul
reg add "!SVC_DB_KEY!" /v "FilePath" /t REG_SZ /d "" /f >nul
reg add "!SVC_DB_KEY!" /v "Format" /t REG_SZ /d "00000000-0000-0000-0000-000000000000" /f >nul
reg add "!SVC_DB_KEY!" /v "InitialSize" /t REG_DWORD /d 32 /f >nul

:: 5. Configure WBF Biometric Configuration in Registry
echo 5. Configuring WBF Adapters in Registry...
set CFG_KEY=HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WinBio\Configurations\{B16F0239-0144-4284-9D27-E4B283626155}
reg add "!CFG_KEY!" /v "SensorAdapterBinary" /t REG_SZ /d "MFS100SensorAdapter.dll" /f >nul
reg add "!CFG_KEY!" /v "EngineAdapterBinary" /t REG_SZ /d "MFS100EngineAdapter.dll" /f >nul
reg add "!CFG_KEY!" /v "StorageAdapterBinary" /t REG_SZ /d "winbiostorageadapter.dll" /f >nul
reg add "!CFG_KEY!" /v "DatabaseId" /t REG_SZ /d "{B16F0239-0144-4284-9D27-E4B283626155}" /f >nul
reg add "!CFG_KEY!" /v "SensorMode" /t REG_DWORD /d 1 /f >nul
reg add "!CFG_KEY!" /v "SystemSensor" /t REG_DWORD /d 1 /f >nul

:: 6. Register Biometric Device Interface via RegisterWbfDevice.exe
echo 6. Registering GUID_DEVINTERFACE_BIOMETRIC_READER on Mantra node...
if exist "%~dp0RegisterWbfDevice.exe" (
    "%~dp0RegisterWbfDevice.exe"
) else (
    echo [ERROR] RegisterWbfDevice.exe not found!
)

:: 7. Restart Windows Biometric Service
echo 7. Starting Windows Biometric Service (WbioSrvc)...
net start WbioSrvc

echo.
echo ==================================================
echo [SUCCESS] WBF Adapters Registered!
echo ==================================================
echo.
echo Running diagnostic unit test (TestWbfUnit.exe)...
if exist "%~dp0TestWbfUnit.exe" (
    "%~dp0TestWbfUnit.exe"
) else (
    echo [WARN] TestWbfUnit.exe not found.
)
echo.
pause
