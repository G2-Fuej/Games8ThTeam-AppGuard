@echo off
chcp 65001 >nul 2>&1
title Games8Th.Team AppGuard - CLI
cd /d "%~dp0"

set "EXE=%~dp0dist\Games8Th.Team-AppGuard-CLI.exe"
set "PY=C:\Program Files\Python310\python.exe"
if not exist "%PY%" set "PY=python"

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo ==========================================================
    echo   Games8Th.Team AppGuard   -   CLI
    echo   Requesting administrator privileges...
    echo ==========================================================
    echo.
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

if exist "%EXE%" (
    "%EXE%" %*
    exit /b %errorlevel%
)

echo Packaged EXE not found, falling back to source mode...
echo.
set "PYTHONPATH=%~dp0src"
set "PYTHONIOENCODING=utf-8"
"%PY%" -m g8t.cli %*

if %errorlevel% neq 0 (
    echo.
    echo [FAIL] Command failed with exit code %errorlevel%.
    pause
)
