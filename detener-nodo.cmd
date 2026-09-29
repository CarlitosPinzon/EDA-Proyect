@echo off
rem Doble clic: apaga el nodo remoto de TaquillaEDA en este Windows
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\detener-nodo.ps1"
echo.
pause
