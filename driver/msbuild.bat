@echo off
title Games8Th.Team - MSBuild Kernel Driver
color 0B
setlocal

set MSBUILD="D:\DKTools\MSBuild\Current\Bin\MSBuild.exe"
if not exist %MSBUILD% (
    echo [ERROR] MSBuild.exe not found at %MSBUILD%
    exit /b 1
)

cd /d "%~dp0"

echo ================================================
echo  Building Games8thGuard.sys via MSBuild
echo ================================================

%MSBUILD% g8tguard.vcxproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /v:minimal /nologo

set RC=%ERRORLEVEL%
echo.
if %RC% EQU 0 (
    echo [OK] MSBuild succeeded.
) else (
    echo [FAIL] MSBuild exit code %RC%
)
exit /b %RC%
