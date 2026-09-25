@echo off
title Games8Th.Team - Load Games8thGuard.sys (Test Mode)
color 0B

echo ================================================
echo  Games8Th.Team  Load Kernel Driver (Test Sign)
echo ================================================
echo.

cd /d "%~dp0"

rem 1. Enable test signing (requires reboot if first time)
echo [1/4] Enabling test signing mode...
bcdedit /set testsigning on 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo  [..] bcdedit needs admin, retrying elevated...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

rem 2. Create service
echo [2/4] Creating driver service...
sc stop Games8thGuard 2>nul
sc delete Games8thGuard 2>nul
set "SYS=%~dp0build\Release\Games8thGuard.sys"
if not exist "%SYS%" set "SYS=%~dp0Games8thGuard.sys"
if not exist "%SYS%" (
    echo [FAIL] Games8thGuard.sys not found. Build it first ^(msbuild.bat^).
    pause
    exit /b 1
)
echo  [OK] Using driver: %SYS%
sc create Games8thGuard type= kernel binPath= "%SYS%" start= demand
if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] sc create failed.
    pause
    exit /b 1
)
for %%I in ("%SYS%") do set "SYS_FULL=%%~fI"
set "SYS_NATIVE=\??\%SYS_FULL%"
reg add "HKLM\SYSTEM\CurrentControlSet\Services\Games8thGuard" /v ImagePath /t REG_EXPAND_SZ /d "%SYS_NATIVE%" /f >nul
if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] Could not set the native driver ImagePath.
    sc delete Games8thGuard >nul 2>nul
    pause
    exit /b 1
)

rem 3. Start service
echo [3/4] Starting driver service...
sc start Games8thGuard
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [FAIL] Driver failed to start.
    echo  If error mentions the file is not signed / corrupt, you must:
    echo   1. REBOOT the machine to apply test signing:  bcdedit /set testsigning on
    echo   2. Then re-run this script
    pause
    exit /b 1
)

rem 4. Verify
echo [4/4] Verifying driver...
sc query Games8thGuard
echo.
echo [OK] Driver loaded successfully. Check kernel debug output.
pause
