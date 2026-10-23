@echo off
cd /d "%~dp0"
echo ==================================================
echo   Registering Mantra MFS100 Credential Provider...
echo ==================================================
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Please right-click this script and select "Run as administrator"!
    pause
    exit /b 1
)

mkdir "C:\Program Files\MFS100" >nul 2>&1
copy /y "%~dp0MFS100CP.dll" "C:\Program Files\MFS100\MFS100CP.dll"

:: 1. COM In-proc server registration (both HKLM\Software\Classes and HKCR)
reg add "HKLM\SOFTWARE\Classes\CLSID\{B16F0239-0144-4284-9D27-E4B283626155}" /ve /t REG_SZ /d "Mantra MFS100 Credential Provider" /f
reg add "HKLM\SOFTWARE\Classes\CLSID\{B16F0239-0144-4284-9D27-E4B283626155}\InprocServer32" /ve /t REG_SZ /d "C:\Program Files\MFS100\MFS100CP.dll" /f
reg add "HKLM\SOFTWARE\Classes\CLSID\{B16F0239-0144-4284-9D27-E4B283626155}\InprocServer32" /v "ThreadingModel" /t REG_SZ /d "Apartment" /f

:: 2. Windows Credential Provider registration
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{B16F0239-0144-4284-9D27-E4B283626155}" /ve /t REG_SZ /d "MFS100CredentialProvider" /f

:: 3. Run regsvr32
regsvr32.exe /s "C:\Program Files\MFS100\MFS100CP.dll"

echo.
echo ==================================================
echo [SUCCESS] MFS100 Credential Provider Registered!
echo ==================================================
echo.
echo Next steps:
echo 1. If LogonUI is already active, restart the VM or press
echo    Ctrl+Alt+Del to refresh the sign-in providers.
echo 2. On the logon screen, click "Sign-in options"
echo    to select the Mantra MFS100 Fingerprint tile!
echo.
pause
