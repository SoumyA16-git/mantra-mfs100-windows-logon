@echo off
setlocal enabledelayedexpansion

call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64
if errorlevel 1 (
    echo [ERROR] Failed to set up VC environment.
    exit /b 1
)

cd /d "%~dp0"
echo Compiling MFS100CP.dll (x64 Native Credential Provider - Static CRT /MT)...

cl.exe /nologo /O2 /W3 /D_UNICODE /DUNICODE /DWIN32 /D_WINDOWS /D_USRDLL /DMFS100CP_EXPORTS /EHsc /MT ^
    /I. ^
    Dll.cpp MFS100Provider.cpp MFS100Credential.cpp ^
    /link /DLL /DEF:MFS100CP.def /OUT:MFS100CP.dll ^
    shlwapi.lib secur32.lib advapi32.lib ole32.lib user32.lib gdi32.lib

if errorlevel 1 (
    echo [ERROR] Compilation failed!
    exit /b 1
)

echo.
echo ==================================================
echo [SUCCESS] MFS100CP.dll (x64 Static CRT) compiled!
echo ==================================================
echo Copying to dist\MFS100_Deployment...
copy /y MFS100CP.dll "..\..\dist\MFS100_Deployment\MFS100CP.dll"
