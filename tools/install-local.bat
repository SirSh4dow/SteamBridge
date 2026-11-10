@echo off
rem Cria a copia do app em C:\Users\<usuario>\SteamBridge e o atalho do Menu Iniciar.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-local.ps1"
if errorlevel 1 pause
