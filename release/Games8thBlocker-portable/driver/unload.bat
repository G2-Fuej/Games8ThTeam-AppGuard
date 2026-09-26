@echo off
setlocal EnableExtensions
title Games8Th.Team - Unload Games8thGuard.sys
color 0C

echo ================================================
echo  Games8Th.Team  Unload Games8thGuard.sys
echo ================================================
echo.

fltmc >nul 2>&1
if errorlevel 1 (
    echo [UNVERIFIED] Administrator privileges are required.
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b 740
)

sc.exe stop Games8thGuard >nul 2>&1
sc.exe delete Games8thGuard >nul 2>&1
timeout /t 1 /nobreak >nul
sc.exe query Games8thGuard >nul 2>&1
if not errorlevel 1 (
    echo [UNVERIFIED] Games8thGuard service still exists.
    exit /b 1
)

echo [OK] Driver service stopped and removed.
exit /b 0
