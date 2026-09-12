@echo off
chcp 65001 >nul 2>&1
title Games8Th.Team AppGuard - Build EXE
cd /d "%~dp0"

set "PY=C:\Program Files\Python310\python.exe"
if not exist "%PY%" set "PY=python"
set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"

echo.
echo ==========================================================
echo    Games8Th.Team AppGuard   -   Build EXE
echo ==========================================================
echo.

echo [1/4] Generating logo assets and icon...
"%PY%" "src\make_logo.py"
if %errorlevel% neq 0 (
    echo [FAIL] Logo processing failed.
    pause
    exit /b 1
)
"%PY%" "src\make_icon.py"
if %errorlevel% neq 0 (
    echo [FAIL] Icon generation failed.
    pause
    exit /b 1
)

echo.
echo [2/4] Building GUI executable (windowed)...
"%PY%" -m PyInstaller --noconfirm --onefile --windowed ^
    --name "Games8Th.Team-AppGuard" ^
    --icon "%ROOT%\src\assets\app.ico" ^
    --paths "%ROOT%\src" ^
    --add-data "%ROOT%\src\assets;assets" ^
    --collect-submodules g8t ^
    --hidden-import g8t.cli ^
    --hidden-import g8t.gui.app ^
    --distpath "%ROOT%\dist" ^
    --workpath "%ROOT%\build\gui" ^
    --specpath "%ROOT%\build\gui" ^
    "%ROOT%\src\app_main.py"
if %errorlevel% neq 0 (
    echo [FAIL] GUI build failed.
    pause
    exit /b 1
)

echo.
echo [3/4] Building CLI executable (console)...
"%PY%" -m PyInstaller --noconfirm --onefile --console ^
    --name "Games8Th.Team-AppGuard-CLI" ^
    --icon "%ROOT%\src\assets\app.ico" ^
    --paths "%ROOT%\src" ^
    --add-data "%ROOT%\src\assets;assets" ^
    --collect-submodules g8t ^
    --distpath "%ROOT%\dist" ^
    --workpath "%ROOT%\build\cli" ^
    --specpath "%ROOT%\build\cli" ^
    "%ROOT%\src\cli_main.py"
if %errorlevel% neq 0 (
    echo [FAIL] CLI build failed.
    pause
    exit /b 1
)

echo.
echo [4/4] Build complete. Output:
echo ==========================================================
dir /b "%ROOT%\dist\*.exe"
echo ==========================================================
echo.
echo Run dist\Games8Th.Team-AppGuard.exe  (GUI, requests admin)
echo.
pause
