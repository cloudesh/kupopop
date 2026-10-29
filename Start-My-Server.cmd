@echo off
title Kupopop - My Local Server
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0server\Start-Local.ps1"
if errorlevel 1 pause
