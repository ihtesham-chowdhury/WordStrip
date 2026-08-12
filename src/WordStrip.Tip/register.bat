@echo off
rem Registers the WordStrip text service for testing. Right-click, "Run as administrator".
rem
rem This is the developer path. The shipping path is the opt-in step inside WordStrip's Settings window,
rem which elevates only for this operation - see CLAUDE_PROJECT_CONTEXT.md section 14. The tray application
rem itself must never run elevated: a keyboard hook installed by an elevated process cannot see input going
rem to non-elevated windows, so an elevated WordStrip stops working in every ordinary application.
rem
rem  ---> If anything goes wrong afterwards, run unregister.bat as administrator. <---

cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 (
    echo.
    echo   This must run as administrator.
    echo   Right-click register.bat and choose "Run as administrator".
    echo.
    pause
    exit /b 1
)

set DLL=%~dp0bin\x64\Release\WordStripTip.dll
if not exist "%DLL%" (
    echo ERROR: %DLL% not found. Run build.bat first.
    pause
    exit /b 1
)

echo Registering %DLL%
regsvr32 /s "%DLL%"
if errorlevel 1 (
    echo REGISTRATION FAILED
    pause
    exit /b 1
)

echo.
echo Verifying:
reg query "HKCR\CLSID\{85418D7E-C008-4E1B-981B-0DC9586800CC}\InprocServer32" 2>nul | findstr /C:"REG_SZ"
echo.
reg query "HKLM\SOFTWARE\Microsoft\CTF\TIP\{85418D7E-C008-4E1B-981B-0DC9586800CC}" >nul 2>&1 && echo   TSF registration  OK || echo   TSF registration  MISSING

echo.
echo Registered. It will not load into anything until it is selected as an input method:
echo   Win+Space to cycle, or Settings ^> Time ^& Language ^> Typing ^> Advanced keyboard settings.
echo.
pause
