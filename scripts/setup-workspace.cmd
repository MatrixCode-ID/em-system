@echo off
setlocal EnableExtensions DisableDelayedExpansion
where pwsh.exe >nul 2>nul
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) tidak ditemukan di PATH. Pasang dari https://aka.ms/powershell lalu coba lagi.
    pause
    exit /b 1
)
pwsh.exe -NoLogo -NoProfile -File "%~dp0setup-workspace\setup-workspace.ps1" %*
set "SETUP_EXIT_CODE=%errorlevel%"
echo.
if not "%SETUP_EXIT_CODE%"=="0" (
    echo Setup gagal. Exit code: %SETUP_EXIT_CODE%
    echo Salin pesan error di atas sebelum menutup jendela.
) else (
    echo Selesai. Exit code: %SETUP_EXIT_CODE%
)
pause
exit /b %SETUP_EXIT_CODE%
