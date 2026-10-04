@echo off
rem buka-wiki.cmd - regenerasi navigasi lalu buka wiki di browser.
rem Klik dua kali berkas ini, jangan app.html, supaya materi baru selalu ikut terbaca.

cd /d "%~dp0"

"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0buat-tree.ps1"
if errorlevel 1 goto gagal

start "" "%~dp0app.html"
exit /b 0

:gagal
echo.
echo Navigasi gagal dibuat ulang, app.html tidak dibuka.
echo Perbaiki pesan kesalahan di atas lalu jalankan lagi.
echo.
pause
exit /b 1
