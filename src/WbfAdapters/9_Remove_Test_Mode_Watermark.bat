@echo off
setlocal enabledelayedexpansion
title Mantra MFS100 - Test Mode Watermark Manager
cd /d "%~dp0"

:: Check for Administrator privileges
net session >nul 2>&1
if errorlevel 1 (
    echo.
    echo ================================================================
    echo   [ERROR] Administrator privileges required!
    echo ================================================================
    echo   Please right-click on this script and select "Run as administrator".
    echo ================================================================
    echo.
    pause
    exit /b 1
)

:MENU
cls
echo ================================================================
echo        Mantra MFS100 - Windows Test Mode Watermark Manager
echo ================================================================
echo.
echo   [1] Hide Desktop Watermark (RECOMMENDED)
echo       - Keeps Test Signing ACTIVE so Mantra fingerprint logon works.
echo       - Hides the "Test Mode" watermark from the bottom-right corner.
echo       - Launches Universal Watermark Disabler (UWD).
echo.
echo   [2] Turn OFF Test Mode Completely (bcdedit /set testsigning off)
echo       - Removes Test Mode completely.
echo       - CAUTION: Mantra driver will stop loading unless signed with
echo         a commercial code-signing certificate!
echo.
echo   [3] Turn ON Test Mode (bcdedit /set testsigning on)
echo       - Restores Test Signing mode for Mantra self-signed driver.
echo.
echo   [4] Exit
echo.
echo ================================================================
set /p "CHOICE=Enter your choice [1, 2, 3, or 4]: "

if "%CHOICE%"=="1" goto HIDE_WATERMARK
if "%CHOICE%"=="2" goto DISABLE_TEST_MODE
if "%CHOICE%"=="3" goto ENABLE_TEST_MODE
if "%CHOICE%"=="4" goto EXIT_SCRIPT

echo.
echo [!] Invalid selection. Please enter 1, 2, 3, or 4.
timeout /t 2 >nul
goto MENU

:HIDE_WATERMARK
cls
echo ================================================================
echo   Hiding "Test Mode" Desktop Watermark
echo ================================================================
echo.
echo   1. The Universal Watermark Disabler tool will open in a moment.
echo   2. Verify the Status says "Ready for installation".
echo   3. Click the "Install" button.
echo   4. Windows will prompt to sign you out to apply the change.
echo   5. Log back in - your desktop watermark will be completely gone!
echo.
echo   Note: Test Signing remains active, so your Mantra MFS100
echo   Windows Hello fingerprint scanner continues to work normally.
echo ================================================================
echo.

if not exist "uwd.exe" (
    echo [*] Downloading Universal Watermark Disabler...
    powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://github.com/pr701/universal-watermark-disabler/releases/latest/download/uwd.zip' -OutFile 'uwd.zip'"
    if exist "uwd.zip" (
        powershell -NoProfile -Command "Expand-Archive -Path 'uwd.zip' -DestinationPath '.' -Force; Remove-Item 'uwd.zip' -Force"
    )
)

if exist "uwd.exe" (
    echo [*] Launching Universal Watermark Disabler...
    start "" "uwd.exe"
    echo.
    echo [OK] UWD launched. Please click "Install" in the UWD window.
) else (
    echo [ERROR] Could not find or download uwd.exe.
    echo Please download it manually from:
    echo https://github.com/pr701/universal-watermark-disabler/releases
)

echo.
pause
goto MENU

:DISABLE_TEST_MODE
cls
echo ================================================================
echo   Turn OFF Test Signing Mode
echo ================================================================
echo.
echo   [WARNING]
echo   Turning off Test Signing will prevent Windows from loading the
echo   self-signed Mantra WBF driver upon next reboot!
echo   (Only do this if you have signed the driver with a commercial
echo    certificate or wish to disable the scanner).
echo.
set /p "CONFIRM=Are you sure you want to turn OFF Test Signing? (Y/N): "
if /i not "%CONFIRM%"=="Y" goto MENU

echo.
echo [*] Executing: bcdedit /set testsigning off
bcdedit /set testsigning off
if errorlevel 1 (
    echo [ERROR] Failed to update BCD settings.
) else (
    echo.
    echo ================================================================
    echo [SUCCESS] Test Signing has been disabled.
    echo Please restart your computer for the change to take effect.
    echo ================================================================
)
echo.
pause
goto MENU

:ENABLE_TEST_MODE
cls
echo ================================================================
echo   Turn ON Test Signing Mode
echo ================================================================
echo.
echo [*] Executing: bcdedit /set testsigning on
bcdedit /set testsigning on
if errorlevel 1 (
    echo.
    echo [ERROR] Failed to enable Test Signing.
    echo If you see a Secure Boot error, please restart your PC, enter BIOS,
    echo and set Secure Boot to "Disabled".
) else (
    echo.
    echo ================================================================
    echo [SUCCESS] Test Signing has been enabled.
    echo Please restart your computer for the change to take effect.
    echo ================================================================
)
echo.
pause
goto MENU

:EXIT_SCRIPT
exit /b 0
