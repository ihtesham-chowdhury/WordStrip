@echo off
rem Builds the WordStrip text service (x64 only - see CLAUDE_PROJECT_CONTEXT.md section 14).
rem
rem A batch file rather than a .csproj or CMake because this is one small DLL with no dependencies beyond
rem the Windows SDK, and because the rest of the toolchain here is dotnet - adding CMake would be a second
rem build system to keep working for no benefit at this size.
rem
rem Usage:  build.bat [Debug|Release]        default Release

setlocal

set CONFIG=%~1
if "%CONFIG%"=="" set CONFIG=Release

set VCVARS=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat
if not exist "%VCVARS%" (
    echo ERROR: MSVC not found at "%VCVARS%"
    echo Install Visual Studio Build Tools with the "Desktop development with C++" workload.
    exit /b 1
)

rem vcvarsall.bat itself internally probes for vswhere.exe and prints "'vswhere.exe' is not recognized" to
rem stderr when it is not on PATH - cosmetic, vcvarsall still sets up the environment correctly regardless.
rem Redirecting stderr too (not just stdout) keeps that noise from reaching a caller that treats any stderr
rem output as a failure - which a PowerShell script with $ErrorActionPreference = "Stop" piping this through
rem 2^>^&1 genuinely does, since PowerShell wraps native stderr lines as terminating ErrorRecords.
call "%VCVARS%" x64 >nul 2>&1
if errorlevel 1 ( echo ERROR: vcvarsall failed & exit /b 1 )

rem vcvarsall changes the working directory, so this must come AFTER the call. Putting it before is a
rem half-hour of wondering why the compiler cannot see source files that are plainly there.
cd /d "%~dp0"

set OUTDIR=%~dp0bin\x64\%CONFIG%
set OBJDIR=%OUTDIR%\obj
if not exist "%OBJDIR%" mkdir "%OBJDIR%"

if /i "%CONFIG%"=="Debug" (
    set CFLAGS=/Od /Zi /MTd /D_DEBUG
) else (
    set CFLAGS=/O2 /MT /DNDEBUG
)

rem /MT, not /MD, and that is load-bearing rather than a preference. This DLL is loaded into every
rem application on the machine that accepts text. Linking the CRT dynamically would inject a dependency on
rem one specific VC++ redistributable version into all of them; hosts without it fail to load the DLL and
rem report nothing at all.
echo === compiling (%CONFIG%, x64) ===
cl /nologo /c /EHsc /W4 /WX /std:c++17 /DUNICODE /D_UNICODE %CFLAGS% /Fo"%OBJDIR%\\" ^
   DllMain.cpp TextService.cpp LoadLog.cpp PipeClient.cpp
if errorlevel 1 ( echo BUILD FAILED & exit /b 1 )

rem Once the service is registered it is loaded into Chrome, Word, Explorer and everything else that accepts
rem text, so the output file is locked and the linker cannot overwrite it. Windows does allow a loaded DLL to
rem be RENAMED, though - running processes keep hold of the file they already opened. Moving the old one
rem aside gives the linker a clear path without asking anyone to close their browser.
rem
rem The leftovers cannot be deleted until every host has unloaded them, so sweep whatever is now free first.
del /q "%OUTDIR%\WordStripTip.old.*.dll" >nul 2>&1
if exist "%OUTDIR%\WordStripTip.dll" (
    ren "%OUTDIR%\WordStripTip.dll" "WordStripTip.old.%RANDOM%.dll" >nul 2>&1
    if exist "%OUTDIR%\WordStripTip.dll" (
        echo ERROR: could not move the previous WordStripTip.dll out of the way.
        echo        Run unregister.bat as administrator, then try again.
        exit /b 1
    )
)

echo === linking ===
link /nologo /DLL /MACHINE:X64 /DEF:WordStripTip.def /OUT:"%OUTDIR%\WordStripTip.dll" ^
     "%OBJDIR%\DllMain.obj" "%OBJDIR%\TextService.obj" "%OBJDIR%\LoadLog.obj" "%OBJDIR%\PipeClient.obj" ^
     ole32.lib oleaut32.lib uuid.lib advapi32.lib shell32.lib user32.lib
if errorlevel 1 ( echo LINK FAILED & exit /b 1 )

echo.
echo Built: %OUTDIR%\WordStripTip.dll
exit /b 0
