@echo off
setlocal EnableExtensions EnableDelayedExpansion
title Games8Th.Team - Load Games8thGuard.sys
color 0B

echo ================================================
echo  Games8Th.Team  Load Games8thGuard.sys
echo ================================================
echo.

cd /d "%~dp0"
set "ROOT=%~dp0.."
set "SYS=%~dp0build\Release\Games8thGuard.sys"
if not exist "%SYS%" set "SYS=%~dp0Games8thGuard.sys"
set "APP=%ROOT%\Games8thBlocker.exe"

rem Do not change testsigning, Secure Boot, or other code-integrity settings.
rem A kernel driver must already satisfy the active Windows signing policy.
fltmc >nul 2>&1
if errorlevel 1 (
    echo [UNVERIFIED] Administrator privileges are required.
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$p=Start-Process -FilePath '%~f0' -Verb RunAs -Wait -PassThru; exit $p.ExitCode"
    set "ELEVATED_RC=!ERRORLEVEL!"
    exit /b !ELEVATED_RC!
)

if not exist "%SYS%" (
    echo [UNVERIFIED] Games8thGuard.sys not found: %SYS%
    exit /b 2
)

echo [1/5] Checking Authenticode signature...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop'; $s=Get-AuthenticodeSignature -LiteralPath '%SYS%'; Write-Host ('  Status: ' + $s.Status); if($s.SignerCertificate){Write-Host ('  Signer: ' + $s.SignerCertificate.Subject)}; if($s.Status -ne 'Valid'){exit 2}"
if errorlevel 1 (
    echo [UNVERIFIED] Driver signature is not valid under the current Windows policy.
    echo  No test-signing or signature-bypass setting was changed.
    exit /b 577
)

echo [2/5] Creating driver service...
sc.exe stop Games8thGuard >nul 2>&1
sc.exe delete Games8thGuard >nul 2>&1
sc.exe create Games8thGuard type= kernel start= demand binPath= "%SYS%"
if errorlevel 1 (
    echo [UNVERIFIED] sc create failed.
    exit /b 1
)

for %%I in ("%SYS%") do set "SYS_FULL=%%~fI"
set "SYS_NATIVE=\??\!SYS_FULL!"
reg.exe add "HKLM\SYSTEM\CurrentControlSet\Services\Games8thGuard" /v ImagePath /t REG_EXPAND_SZ /d "!SYS_NATIVE!" /f >nul
if errorlevel 1 (
    echo [UNVERIFIED] Could not set the driver ImagePath.
    sc.exe delete Games8thGuard >nul 2>&1
    exit /b 1
)

echo [3/5] Starting driver service...
sc.exe start Games8thGuard
if errorlevel 1 (
    echo [UNVERIFIED] Driver service failed to start.
    sc.exe query Games8thGuard
    sc.exe delete Games8thGuard >nul 2>&1
    exit /b 1
)

echo [4/5] Checking service state...
sc.exe query Games8thGuard | findstr /I /C:"RUNNING" >nul
if errorlevel 1 (
    echo [UNVERIFIED] Service is not RUNNING.
    sc.exe query Games8thGuard
    sc.exe stop Games8thGuard >nul 2>&1
    sc.exe delete Games8thGuard >nul 2>&1
    exit /b 1
)

echo [5/5] Verifying device and QUERY_PATHS...
if not exist "%APP%" (
    echo [UNVERIFIED] User-mode verifier not found: %APP%
    sc.exe stop Games8thGuard >nul 2>&1
    sc.exe delete Games8thGuard >nul 2>&1
    exit /b 2
)
call "%APP%" cli driver-status
set "STATUS_RC=%ERRORLEVEL%"
if not "%STATUS_RC%"=="0" (
    echo [UNVERIFIED] Device/IOCTL verification failed. Exit code: %STATUS_RC%
    sc.exe stop Games8thGuard >nul 2>&1
    sc.exe delete Games8thGuard >nul 2>&1
    exit /b %STATUS_RC%
)

echo.
echo [OK] Driver loaded and verified by service, device, and QUERY_PATHS checks.
exit /b 0
