@echo off
title Games8Th.Team - Build Kernel Driver
color 0B

rem 固定工作目录为项目根，确保 driver\g8tguard.c 相对路径有效
cd /d "%~dp0.."

echo ================================================
echo  Games8Th.Team  Games8thGuard.sys  Build
echo ================================================
echo.

rem Locate vcvarsall.bat (installed by VS Build Tools)
set VCVARS="D:\DKTools\VC\Auxiliary\Build\vcvarsall.bat"
if not exist %VCVARS% set VCVARS="C:\Program Files\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
if not exist %VCVARS% set VCVARS="C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"

if not exist %VCVARS% (
    echo [ERROR] vcvarsall.bat not found. Install VS Build Tools first.
    pause
    exit /b 1
)

rem Locate WDK libs
set WDKROOT=C:\Program Files (x86)\Windows Kits\10
if not exist "%WDKROOT%\Include" (
    echo [ERROR] WDK not found.
    pause
    exit /b 1
)

rem Pick highest SDK version folder
for /f "delims=" %%i in ('dir /b /ad /o-n "%WDKROOT%\Include"') do (
    set SDKVER=%%i
    goto :found
)
:found
echo [OK] WDK SDK version: %SDKVER%

call %VCVARS% x64

cl /nologo /kernel /O1 /GS /Zl /W4 -DWIN32 -D_AMD64_ -DNDEBUG ^
   /I"%WDKROOT%\Include\%SDKVER%\km" /I"%WDKROOT%\Include\%SDKVER%\shared" ^
   /c driver\g8tguard.c -o driver\g8tguard.obj

if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] compile failed
    pause
    exit /b 1
)

link /nologo /driver /kernel /subsystem:native /entry:DriverEntry ^
   /LIBPATH:"%WDKROOT%\Lib\%SDKVER%\km\x64" ^
   driver\g8tguard.obj ntoskrnl.lib fwpkclnt.lib ^
   /out:driver\Games8thGuard.sys

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [OK] Build successful: driver\Games8thGuard.sys
    dir driver\Games8thGuard.sys
) else (
    echo [FAIL] link failed
)

pause
