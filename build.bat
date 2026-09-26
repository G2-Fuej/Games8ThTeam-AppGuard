@echo off
title Games8Th.Team - Build
color 0A
echo ================================================
echo   Games8Th.Team  AppBlocker  Build
echo ================================================
echo.

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set FRAMEWORK=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319

echo [1/2] Copying logo assets...
if not exist "assets" mkdir assets
if exist "ref\assets\sized\mark_64.png" copy /Y "ref\assets\sized\mark_64.png" "assets\mark_64.png" >nul
if exist "ref\assets\logo_mark.png" copy /Y "ref\assets\logo_mark.png" "assets\logo_mark.png" >nul
if exist "ref\assets\app.ico" copy /Y "ref\assets\app.ico" "assets\app.ico" >nul

echo [2/2] Compiling...
"%CSC%" /target:exe /out:Games8thBlocker.exe /nologo /optimize+ ^
  /win32manifest:"src\app.manifest" ^
  /win32icon:"assets\app.ico" ^
  /r:System.dll ^
  /r:mscorlib.dll ^
  /r:System.Management.dll ^
  src\FeilianCli.cs ^
  src\KernelDriver.cs

if %ERRORLEVEL% EQU 0 (
    echo.
    echo  [OK] Build successful: Games8thBlocker.exe
    echo.
) else (
    echo.
    echo  [FAIL] Build failed with error code %ERRORLEVEL%
    echo.
)

pause
