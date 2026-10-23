@echo off
cd /d "%~dp0"
echo ==================================================
echo   Unregistering Mantra MFS100 Credential Provider...
echo ==================================================
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Please right-click this script and select "Run as administrator"!
    pause
    exit /b 1
)

regsvr32.exe /u /s "C:\Program Files\MFS100\MFS100CP.dll"
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{B16F0239-0144-4284-9D27-E4B283626155}" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Classes\CLSID\{B16F0239-0144-4284-9D27-E4B283626155}" /f >nul 2>&1

echo.
echo ==================================================
echo [SUCCESS] MFS100 Credential Provider Unregistered!
echo Native Windows logon tiles restored.
echo ==================================================
echo.
pause
