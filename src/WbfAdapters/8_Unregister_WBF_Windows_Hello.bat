@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ==================================================
echo   Unregistering Mantra MFS100 WBF Adapters...
echo ==================================================

net session >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Please right-click this script and select "Run as administrator"!
    pause
    exit /b 1
)

echo 1. Stopping Windows Biometric Service (WbioSrvc)...
net stop WbioSrvc >nul 2>&1
timeout /t 1 /nobreak >nul

echo 2. Removing WBF registry configurations...
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WinBio\Databases\{B16F0239-0144-4284-9D27-E4B283626155}" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WinBio\Configurations\{B16F0239-0144-4284-9D27-E4B283626155}" /f >nul 2>&1

echo 3. Cleaning up binaries...
del "%SystemRoot%\System32\MFS100SensorAdapter.dll" 2>nul
del "%SystemRoot%\System32\MFS100EngineAdapter.dll" 2>nul

echo 4. Restarting Windows Biometric Service (WbioSrvc)...
net start WbioSrvc >nul 2>&1

echo.
echo ==================================================
echo [SUCCESS] WBF Adapters Unregistered!
echo ==================================================
echo.
pause
