@echo off
rem ============================================================================================
rem  ESCAPE HATCH. If text input has gone wrong anywhere on this machine, run this.
rem
rem  Right-click this file and choose "Run as administrator".
rem
rem  A text service is loaded into every application that accepts text, so a broken one can break
rem  typing system-wide - including in whatever you would normally use to fix it. If it is bad
rem  enough that you cannot run this, boot into Safe Mode: text services are not loaded there, so
rem  typing works and this will run fine.
rem ============================================================================================

cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 (
    echo.
    echo   This must run as administrator.
    echo   Right-click unregister.bat and choose "Run as administrator".
    echo.
    pause
    exit /b 1
)

set DLL=%~dp0bin\x64\Release\WordStripTip.dll

echo Unregistering %DLL%
regsvr32 /u /s "%DLL%"
if errorlevel 1 (
    echo.
    echo regsvr32 reported a failure. Removing the registry entries directly instead.
    reg delete "HKCR\CLSID\{85418D7E-C008-4E1B-981B-0DC9586800CC}" /f >nul 2>&1
    reg delete "HKLM\SOFTWARE\Microsoft\CTF\TIP\{85418D7E-C008-4E1B-981B-0DC9586800CC}" /f >nul 2>&1
)

echo.
echo Checking what is left:
reg query "HKCR\CLSID\{85418D7E-C008-4E1B-981B-0DC9586800CC}" >nul 2>&1 && echo   CLSID  STILL PRESENT || echo   CLSID  gone
reg query "HKLM\SOFTWARE\Microsoft\CTF\TIP\{85418D7E-C008-4E1B-981B-0DC9586800CC}" >nul 2>&1 && echo   TIP    STILL PRESENT || echo   TIP    gone

echo.
echo Done. Applications already running keep the DLL loaded until they are restarted.
pause
