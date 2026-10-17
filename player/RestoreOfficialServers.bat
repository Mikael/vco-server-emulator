@echo off
setlocal
set "ROOT=%~dp0"
if exist "%ROOT%voyage\login.ini.original" copy /y "%ROOT%voyage\login.ini.original" "%ROOT%voyage\login.ini" >nul
if exist "%ROOT%login.ini.original" copy /y "%ROOT%login.ini.original" "%ROOT%login.ini" >nul
echo The original server list is restored.
pause
endlocal
