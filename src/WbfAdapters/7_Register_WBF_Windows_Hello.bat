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

:: 3. Deploy Adapter DLLs to WinBioPlugIns and System32
echo 3. Copying WBF adapter binaries to WinBioPlugIns and System32...
if not exist "%SystemRoot%\System32\WinBioPlugIns" mkdir "%SystemRoot%\System32\WinBioPlugIns"
copy /y "%~dp0MFS100SensorAdapter.dll" "%SystemRoot%\System32\WinBioPlugIns\MFS100SensorAdapter.dll"
copy /y "%~dp0MFS100EngineAdapter.dll" "%SystemRoot%\System32\WinBioPlugIns\MFS100EngineAdapter.dll"
copy /y "%~dp0MFS100SensorAdapter.dll" "%SystemRoot%\System32\MFS100SensorAdapter.dll"
copy /y "%~dp0MFS100EngineAdapter.dll" "%SystemRoot%\System32\MFS100EngineAdapter.dll"

:: 4. Clean up any invalid legacy entries
echo 4. Cleaning legacy sensor entries...
reg delete "HKLM\SYSTEM\CurrentControlSet\Services\WbioSrvc\Service Providers\Fingerprint\Virtual Sensors\B16F0239-0144-4284-9D27-E4B283626155" /f >nul 2>&1

:: 5. Register WBF Device, Databases, and Fingerprint Virtual Sensor via RegisterWbfDevice.exe
echo 5. Configuring WBF Device, Databases, and Virtual Sensor...
if exist "%~dp0RegisterWbfDevice.exe" (
    "%~dp0RegisterWbfDevice.exe"
) else (
    echo [ERROR] RegisterWbfDevice.exe not found!
)

:: 6. Restart Windows Biometric Service
echo 6. Starting Windows Biometric Service (WbioSrvc)...
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
