@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-all.ps1" %*
set BUILD_EXIT_CODE=%ERRORLEVEL%
echo.
if not "%CI%"=="true" pause
exit /b %BUILD_EXIT_CODE%