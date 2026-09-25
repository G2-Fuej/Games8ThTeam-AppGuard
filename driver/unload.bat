@echo off
title Games8Th.Team - Unload Kernel Driver
color 0C

echo ================================================
echo  Games8Th.Team  Unload Games8thGuard.sys
echo ================================================
echo.

sc stop Games8thGuard
sc delete Games8thGuard
echo.
echo [OK] Driver stopped and removed.
echo  Test signing stays ON unless you run:  bcdedit /set testsigning off
pause