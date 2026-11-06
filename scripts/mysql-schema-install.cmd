@echo off
where pwsh.exe >nul 2>nul
if errorlevel 1 (
  echo PowerShell 7 ^(pwsh^) is required.
  pause
  exit /b 1
)
pwsh.exe -NoLogo -NoProfile -File "%~dp0mysql-schema-install\mysql-schema-install.ps1" %*
set "result=%errorlevel%"
pause
exit /b %result%
