@echo off
cd /d "%~dp0"
start "Bounty Bay Online Server" cmd /c start-server.bat
timeout /t 6 /nobreak >nul
cd /d "%~dp0..\BBO\voyage"
start "" Core.exe $Snail$ 0
