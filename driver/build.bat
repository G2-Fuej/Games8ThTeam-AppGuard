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

rem Pick the highest complete numeric SDK version.  Include also contains
rem non-version directories (for example wdf), and partial SDK installs may
rem have headers without matching kernel libraries.
set "SDKVER="
for /f "delims=" %%i in ('dir /b /ad /o-n "%WDKROOT%\Include\10.0.*" 2^>nul') do (
    if exist "%WDKROOT%\Include\%%i\km\ntddk.h" if exist "%WDKROOT%\Lib\%%i\km\x64\ntoskrnl.lib" (
        set "SDKVER=%%i"
        goto :found
    )
)
:found
if not defined SDKVER (
    echo [ERROR] No complete WDK version with km headers and x64 libraries was found.
    pause
    exit /b 1
)
echo [OK] WDK SDK version: %SDKVER%

call %VCVARS% x64

if not exist "driver\build\Release" mkdir "driver\build\Release"

cl /nologo /kernel /O1 /GS /guard:cf /Qspectre /Zl /W4 /WX /utf-8 /wd4324 -DWIN32 -D_AMD64_ -DNDEBUG ^
   /I"%WDKROOT%\Include\%SDKVER%\km" /I"%WDKROOT%\Include\%SDKVER%\shared" ^
   /c driver\g8tguard.c /Fo"driver\build\Release\g8tguard.obj"

if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] compile failed
    pause
    exit /b 1
)

link /nologo /driver /kernel /subsystem:native /entry:DriverEntry ^
   /guard:cf /dynamicbase /nxcompat /release /opt:ref /opt:icf ^
   /LIBPATH:"%WDKROOT%\Lib\%SDKVER%\km\x64" ^
   "driver\build\Release\g8tguard.obj" ntoskrnl.lib fwpkclnt.lib ntstrsafe.lib bufferoverflowfastfailk.lib ^
   /out:"driver\build\Release\Games8thGuard-unsigned.sys"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [OK] Unsigned build successful: driver\build\Release\Games8thGuard-unsigned.sys
    echo [INFO] Sign it separately, then replace Games8thGuard.sys only after verification.
    dir "driver\build\Release\Games8thGuard-unsigned.sys"
) else (
    echo [FAIL] link failed
)

pause
