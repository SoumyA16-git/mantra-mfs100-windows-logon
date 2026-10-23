@echo off
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64
echo Visual Studio Environment Loaded. Compiler version:
cl.exe 2>&1 | findstr /i "compiler"
