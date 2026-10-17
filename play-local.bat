@echo off
cd /d "%~dp0"
start "Voyage Century Server" cmd /c start-server.bat
timeout /t 6 /nobreak >nul
cd /d "%~dp0..\voyage"
start "" Core.exe
