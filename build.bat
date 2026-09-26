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

echo [2/3] Verifying embedded driver SHA-256...
if not exist "driver\build\Release\Games8thGuard.sys" (
  echo [FAIL] Signed driver missing: driver\build\Release\Games8thGuard.sys
  exit /b 2
)
certutil.exe -hashfile "driver\build\Release\Games8thGuard.sys" SHA256 | findstr /I /C:"cfcb98ec34428375e8374721dbbf9b588cb22e93b652f01b9bcf23a531297c97" >nul
if %ERRORLEVEL% NEQ 0 (
  echo [FAIL] Embedded driver SHA-256 does not match the approved signed artifact.
  exit /b 3
)
echo [OK] Embedded driver SHA-256 verified.

echo [3/3] Compiling single-file CLI...
"%CSC%" /target:exe /platform:x64 /warn:4 /warnaserror+ /out:Games8thBlocker.exe /nologo /optimize+ ^
  /win32manifest:"src\app.manifest" ^
  /win32icon:"assets\app.ico" ^
  /r:System.dll ^
  /r:mscorlib.dll ^
  /r:System.Management.dll ^
  /resource:"driver\build\Release\Games8thGuard.sys",Games8thTeamBlocker.Games8thGuard.sys ^
  src\FeilianCli.cs ^
  src\KernelDriver.cs ^
  src\EmbeddedDriverInstaller.cs

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
