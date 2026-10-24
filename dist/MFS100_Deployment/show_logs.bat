@echo off
echo ==================================================
echo   Mantra MFS100 WBF and Bridge Service Logs
echo ==================================================
echo.
echo --- WinBioDatabase Files ---
dir /a "C:\Windows\System32\WinBioDatabase\*" 2>nul
echo.
echo --- WBF Adapters Log (Last 200 lines) ---
if exist "C:\ProgramData\MFS100CP\logs\wbf_adapters.log" (
    powershell -NoProfile -Command "Get-Content 'C:\ProgramData\MFS100CP\logs\wbf_adapters.log' -Tail 200"
) else (
    echo [No WBF Adapters log found]
)
echo.
echo --- Bridge Service Log (Last 100 lines) ---
powershell -NoProfile -Command "$f = Get-ChildItem 'C:\ProgramData\MFS100CP\logs\bridge_svc_*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if ($f) { Get-Content $f.FullName -Tail 100 } else { echo '[No Bridge log found]' }"
echo.
echo --- Worker Log (Last 100 lines) ---
powershell -NoProfile -Command "$f = Get-ChildItem 'C:\ProgramData\MFS100CP\logs\worker_*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if ($f) { Get-Content $f.FullName -Tail 100 } else { echo '[No Worker log found]' }"
echo.
echo ==================================================
pause
