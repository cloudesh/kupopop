@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Server.ps1" -Local
if errorlevel 1 pause
