@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Start-AllStagesDemo.ps1" -Offline
if errorlevel 1 pause
