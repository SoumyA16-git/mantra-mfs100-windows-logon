@echo off
setlocal enabledelayedexpansion

call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64
if errorlevel 1 (
    echo [ERROR] Failed to set up VC environment.
    exit /b 1
)

cd /d "%~dp0"
echo ==================================================
echo   Building Mantra MFS100 WBF Adapters (x64 /MT)
echo ==================================================

echo 1. Compiling MFS100SensorAdapter.dll...
cl.exe /nologo /O2 /W3 /D_UNICODE /DUNICODE /DWIN32 /D_WINDOWS /D_USRDLL /EHsc /MT ^
    /I. ^
    MFS100SensorAdapter.cpp ^
    /link /DLL /INTEGRITYCHECK /DEF:MFS100SensorAdapter.def /OUT:MFS100SensorAdapter.dll ^
    advapi32.lib crypt32.lib user32.lib winbio.lib

if errorlevel 1 (
    echo [ERROR] MFS100SensorAdapter.dll compilation failed!
    exit /b 1
)

echo.
echo 2. Compiling MFS100EngineAdapter.dll...
cl.exe /nologo /O2 /W3 /D_UNICODE /DUNICODE /DWIN32 /D_WINDOWS /D_USRDLL /EHsc /MT ^
    /I. ^
    MFS100EngineAdapter.cpp ^
    /link /DLL /INTEGRITYCHECK /DEF:MFS100EngineAdapter.def /OUT:MFS100EngineAdapter.dll ^
    advapi32.lib crypt32.lib user32.lib winbio.lib

if errorlevel 1 (
    echo [ERROR] MFS100EngineAdapter.dll compilation failed!
    exit /b 1
)

echo.
echo 3. Compiling TestWbfUnit.exe...
cl.exe /nologo /O2 /W3 /D_UNICODE /DUNICODE /DWIN32 /D_CONSOLE /EHsc /MT ^
    /I. ^
    TestWbfUnit.cpp ^
    /link /OUT:TestWbfUnit.exe ^
    advapi32.lib user32.lib winbio.lib

if errorlevel 1 (
    echo [ERROR] TestWbfUnit.exe compilation failed!
    exit /b 1
)

echo.
echo 4. Compiling RegisterWbfDevice.exe...
cl.exe /nologo /O2 /W3 /D_UNICODE /DUNICODE /DWIN32 /D_CONSOLE /EHsc /MT ^
    /I. ^
    RegisterWbfDevice.cpp ^
    /link /OUT:RegisterWbfDevice.exe ^
    setupapi.lib advapi32.lib user32.lib shlwapi.lib

if errorlevel 1 (
    echo [ERROR] RegisterWbfDevice.exe compilation failed!
    exit /b 1
)

echo.
echo ==================================================
echo [SUCCESS] All WBF Components Compiled Successfully!
echo ==================================================
echo Copying to dist\MFS100_Deployment...
mkdir "..\..\dist\MFS100_Deployment" 2>nul
copy /y MFS100SensorAdapter.dll "..\..\dist\MFS100_Deployment\"
copy /y MFS100EngineAdapter.dll "..\..\dist\MFS100_Deployment\"
copy /y TestWbfUnit.exe "..\..\dist\MFS100_Deployment\"
copy /y RegisterWbfDevice.exe "..\..\dist\MFS100_Deployment\"
copy /y mfs100_wbf.inf "..\..\dist\MFS100_Deployment\"
del *.obj *.exp *.lib 2>nul

echo.
echo 5. Digitally signing WBF adapters with Force Integrity...
powershell.exe -ExecutionPolicy Bypass -File "%~dp0SignAdapters.ps1"

