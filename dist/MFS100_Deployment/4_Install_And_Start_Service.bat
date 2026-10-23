@echo off
cd /d "%~dp0"
echo ==================================================
echo   Installing MFS100 Bridge Windows Service...
echo ==================================================
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Please right-click this script and select "Run as administrator"!
    pause
    exit /b 1
)

sc stop MFS100BridgeSvc >nul 2>&1
timeout /t 1 /nobreak >nul
taskkill /f /im MFS100BridgeSvc.exe >nul 2>&1
taskkill /f /im MFS100Worker.exe >nul 2>&1
timeout /t 1 /nobreak >nul
sc delete MFS100BridgeSvc >nul 2>&1
timeout /t 1 /nobreak >nul

mkdir "C:\Program Files\MFS100" >nul 2>&1
copy /y "%~dp0*.*" "C:\Program Files\MFS100\"

sc create MFS100BridgeSvc binPath= "\"C:\Program Files\MFS100\MFS100BridgeSvc.exe\"" start= auto DisplayName= "Mantra MFS100 Biometric Bridge Service"
sc description MFS100BridgeSvc "Provides Session-0 biometric matching and template brokerage for Mantra MFS100 Logon."
sc start MFS100BridgeSvc

echo.
echo Service status:
sc query MFS100BridgeSvc
echo.
pause
