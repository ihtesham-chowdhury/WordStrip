@echo off
rem Static checks on the built DLL, before it is ever registered.
rem
rem Worth doing separately because every one of these failures presents identically once the thing is
rem registered: the host simply does not load it, silently, with no error anywhere.

setlocal
set VCVARS=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat
call "%VCVARS%" x64 >nul
cd /d "%~dp0"

set DLL=bin\x64\Release\WordStripTip.dll
if not exist "%DLL%" ( echo ERROR: %DLL% not built & exit /b 1 )

echo === ARCHITECTURE (must say x64) ===
dumpbin /nologo /headers "%DLL%" | findstr /C:"machine" /C:"DLL"

echo.
echo === EXPORTS (must be four, undecorated) ===
dumpbin /nologo /exports "%DLL%" | findstr /C:"Dll"

echo.
echo === DEPENDENCIES (must NOT list any VCRUNTIME or MSVCP) ===
dumpbin /nologo /dependents "%DLL%" | findstr /I ".dll"

echo.
echo === SIZE ===
for %%F in ("%DLL%") do echo %%~zF bytes
exit /b 0
