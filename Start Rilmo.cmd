@echo off
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\launch-published.ps1"
if errorlevel 1 (
  echo Run build.ps1 -Publish to prepare and select the latest standalone build.
  pause
  exit /b 1
)
