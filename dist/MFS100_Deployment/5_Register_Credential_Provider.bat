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

:: Pre-emptively kill any running LogonUI holding file lock on MFS100CP.dll
taskkill /f /im LogonUI.exe >nul 2>&1
timeout /t 1 /nobreak >nul 2>&1

mkdir "C:\Program Files\MFS100" >nul 2>&1
copy /y "%~dp0MFS100CP.dll" "C:\Program Files\MFS100\MFS100CP.dll"
if %errorlevel% neq 0 (
    echo [ERROR] Could not overwrite C:\Program Files\MFS100\MFS100CP.dll (File locked).
    echo Retrying with force...
    taskkill /f /im LogonUI.exe >nul 2>&1
    timeout /t 2 /nobreak >nul 2>&1
    copy /y "%~dp0MFS100CP.dll" "C:\Program Files\MFS100\MFS100CP.dll"
)

:: 1. COM In-proc server registration (both HKLM\Software\Classes and HKCR)
reg add "HKLM\SOFTWARE\Classes\CLSID\{B16F0239-0144-4284-9D27-E4B283626155}" /ve /t REG_SZ /d "Mantra MFS100 Credential Provider" /f
reg add "HKLM\SOFTWARE\Classes\CLSID\{B16F0239-0144-4284-9D27-E4B283626155}\InprocServer32" /ve /t REG_SZ /d "C:\Program Files\MFS100\MFS100CP.dll" /f
reg add "HKLM\SOFTWARE\Classes\CLSID\{B16F0239-0144-4284-9D27-E4B283626155}\InprocServer32" /v "ThreadingModel" /t REG_SZ /d "Apartment" /f

:: 2. Windows Credential Provider registration
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{B16F0239-0144-4284-9D27-E4B283626155}" /ve /t REG_SZ /d "MFS100CredentialProvider" /f

:: 3. Run regsvr32
regsvr32.exe /s "C:\Program Files\MFS100\MFS100CP.dll"

:: 4. Disable Windows Hello PIN exclusive restriction so Windows 11 displays "Sign-in options"
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device" /v "DevicePasswordLessBuildVersion" /t REG_DWORD /d 0 /f >nul 2>&1

:: 5. Refresh LogonUI so the new tile is immediately loaded
taskkill /f /im LogonUI.exe >nul 2>&1

echo.
echo ==================================================
echo [SUCCESS] MFS100 Credential Provider Registered!
echo ==================================================
echo.
echo Next steps:
echo 1. Press Win + L to lock the screen.
echo 2. Click "Sign-in options" under your account.
echo 3. Click the Mantra MFS100 Biometric Login tile.
echo 4. Touch the Mantra scanner to log in!
echo.
pause
