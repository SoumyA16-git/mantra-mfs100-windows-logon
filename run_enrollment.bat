@echo off
cd /d "%~dp0"
echo =======================================================
echo   Mantra MFS100 Standalone Biometric Suite
echo   Zero Credentials Stored ^| Per-SID Isolated Templates
echo =======================================================
echo.
src\EnrollmentTool\bin\x86\Release\net48\MFS100Enroll.exe %*
if errorlevel 1 (
    echo.
    echo [EXIT] Process exited with error code %errorlevel%.
)
echo.
pause
