@echo off
setlocal
if not exist "E:\unity\Editor\Unity.exe" (
  echo Unity 6000.3.27f1 was not found. Open this folder through Unity Hub.
  pause
  exit /b 2
)
start "" "E:\unity\Editor\Unity.exe" -projectPath "%~dp0." "%~dp0Assets\Scenes\VintageJournalDesk.unity"
