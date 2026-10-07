@echo off
setlocal EnableExtensions DisableDelayedExpansion
where pwsh.exe >nul 2>nul
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) was not found on PATH. Install it from https://aka.ms/powershell and try again.
    pause
    exit /b 1
)
pwsh.exe -NoLogo -NoProfile -File "%~dp0pack-nuget\pack-nuget.ps1" %*
set "PACK_EXIT_CODE=%errorlevel%"
echo.
if not "%PACK_EXIT_CODE%"=="0" (
    echo Pack failed. Exit code: %PACK_EXIT_CODE%
    echo Copy the error message above before closing the window.
) else (
    echo Done. Exit code: %PACK_EXIT_CODE%
)
pause
exit /b %PACK_EXIT_CODE%
