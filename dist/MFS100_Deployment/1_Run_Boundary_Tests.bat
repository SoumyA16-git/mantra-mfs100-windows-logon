@echo off
cd /d "%~dp0"
echo ==================================================
echo   MFS100 Automated Security & Biometric Tests
echo ==================================================
MFS100Enroll.exe --boundary-test
echo.
pause
