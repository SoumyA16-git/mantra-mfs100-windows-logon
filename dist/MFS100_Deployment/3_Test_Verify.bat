@echo off
cd /d "%~dp0"
echo ==================================================
echo   MFS100 Biometric 1:N Identification Test
echo ==================================================
MFS100Enroll.exe --identify
echo.
pause
