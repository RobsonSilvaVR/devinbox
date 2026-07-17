@echo off
rem Abre o DevInbox instalando o .NET 10 Desktop Runtime antes, se necessario.
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0bootstrap.ps1"
