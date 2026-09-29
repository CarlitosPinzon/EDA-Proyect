@echo off
rem Doble clic: enciende el nodo remoto de TaquillaEDA en este Windows (ver ENCENDER-NODO-WINDOWS.md)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\iniciar-nodo.ps1" %*
echo.
pause
