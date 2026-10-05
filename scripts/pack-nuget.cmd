@echo off
setlocal EnableExtensions DisableDelayedExpansion
where pwsh.exe >nul 2>nul
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) tidak ditemukan di PATH. Pasang dari https://aka.ms/powershell lalu coba lagi.
    pause
    exit /b 1
)
pwsh.exe -NoLogo -NoProfile -File "%~dp0pack-nuget\pack-nuget.ps1" %*
set "PACK_EXIT_CODE=%errorlevel%"
echo.
if not "%PACK_EXIT_CODE%"=="0" (
    echo Pack gagal. Exit code: %PACK_EXIT_CODE%
    echo Salin pesan error di atas sebelum menutup jendela.
) else (
    echo Selesai. Exit code: %PACK_EXIT_CODE%
)
pause
exit /b %PACK_EXIT_CODE%
