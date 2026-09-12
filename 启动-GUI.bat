@echo off
chcp 65001 >nul 2>&1
title Games8Th.Team AppGuard
cd /d "%~dp0"

set "EXE=%~dp0dist\Games8Th.Team-AppGuard.exe"
set "PY=C:\Program Files\Python310\python.exe"
if not exist "%PY%" set "PY=python"

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo ==========================================================
    echo   Games8Th.Team AppGuard
    echo   Requesting administrator privileges...
    echo ==========================================================
    echo.
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo.
echo ==========================================================
echo    Games8Th.Team AppGuard   -   GUI Launcher
echo    Running as Administrator
echo ==========================================================
echo.

if exist "%EXE%" (
    echo Launching packaged executable...
    start "" "%EXE%"
    exit /b
)

echo Packaged EXE not found, falling back to source mode...
echo.
"%PY%" "%~dp0src\launcher.py" gui
if %errorlevel% neq 0 (
    echo.
    echo [FAIL] Launch failed. Check Python, or run build_exe.bat to build the EXE.
    pause
)
