@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -File "%~dp0Tools\Start-AllStagesDemo.ps1" -Profile PlayerB
if errorlevel 1 pause
