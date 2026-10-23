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

:: 1. Stop Windows Biometric Service during registration
echo 1. Stopping Windows Biometric Service (WbioSrvc)...
net stop WbioSrvc >nul 2>&1
timeout /t 1 /nobreak >nul

:: 2. Deploy Adapter DLLs to System32
echo 2. Copying WBF adapter binaries to System32...
copy /y "%~dp0MFS100SensorAdapter.dll" "%SystemRoot%\System32\MFS100SensorAdapter.dll"
copy /y "%~dp0MFS100EngineAdapter.dll" "%SystemRoot%\System32\MFS100EngineAdapter.dll"

:: 3. Configure WBF Database in Registry
echo 3. Configuring WinBio Database...
set DB_KEY=HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WinBio\Databases\{B16F0239-0144-4284-9D27-E4B283626155}
reg add "!DB_KEY!" /v "BiometricFactor" /t REG_DWORD /d 8 /f >nul
reg add "!DB_KEY!" /v "DatabaseType" /t REG_DWORD /d 1 /f >nul
reg add "!DB_KEY!" /v "FilePath" /t REG_SZ /d "" /f >nul
reg add "!DB_KEY!" /v "ConnectionString" /t REG_SZ /d "" /f >nul
reg add "!DB_KEY!" /v "Attributes" /t REG_DWORD /d 1 /f >nul

:: 4. Configure WBF Biometric Configuration in Registry
echo 4. Configuring WBF Adapters in Registry...
set CFG_KEY=HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WinBio\Configurations\{B16F0239-0144-4284-9D27-E4B283626155}
reg add "!CFG_KEY!" /v "SensorAdapterBinary" /t REG_SZ /d "MFS100SensorAdapter.dll" /f >nul
reg add "!CFG_KEY!" /v "EngineAdapterBinary" /t REG_SZ /d "MFS100EngineAdapter.dll" /f >nul
reg add "!CFG_KEY!" /v "StorageAdapterBinary" /t REG_SZ /d "winbiostorageadapter.dll" /f >nul
reg add "!CFG_KEY!" /v "DatabaseId" /t REG_SZ /d "{B16F0239-0144-4284-9D27-E4B283626155}" /f >nul
reg add "!CFG_KEY!" /v "SensorMode" /t REG_DWORD /d 1 /f >nul
reg add "!CFG_KEY!" /v "SystemSensor" /t REG_DWORD /d 1 /f >nul

:: 5. Install WBF Extension INF for device USB\VID_2C0F&PID_1005
echo 5. Installing WBF Driver Extension Package (mfs100_wbf.inf)...
pnputil /add-driver "%~dp0mfs100_wbf.inf" /install >nul 2>&1

:: 6. Restart Windows Biometric Service
echo 6. Starting Windows Biometric Service (WbioSrvc)...
net start WbioSrvc

echo.
echo ==================================================
echo [SUCCESS] WBF Adapters Registered!
echo ==================================================
echo.
echo Running diagnostic unit test (TestWbfUnit.exe)...
"%~dp0TestWbfUnit.exe"
echo.
pause
