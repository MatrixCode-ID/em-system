@echo off
where pwsh.exe >nul 2>nul
if errorlevel 1 (
  echo PowerShell 7 ^(pwsh^) is required.
  pause
  exit /b 1
)
pwsh.exe -NoLogo -NoProfile -File "%~dp0mysql-server-init\mysql-server-init.ps1" %*
set "result=%errorlevel%"
pause
exit /b %result%
