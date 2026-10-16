@echo off
setlocal
title Bounty Bay Online - join server 26.194.29.135

set "ROOT=%~dp0"
if exist "%ROOT%voyage\Core.exe" (
    set "VOYAGE=%ROOT%voyage"
) else if exist "%ROOT%Core.exe" (
    set "VOYAGE=%ROOT:~0,-1%"
    for %%I in ("%ROOT%..") do set "ROOT=%%~fI\"
) else (
    echo Put this file in your Bounty Bay Online folder, next to the "voyage" folder, and run it again.
    pause
    exit /b 1
)

set "B64=%TEMP%\bbo_login.b64"
set "INI=%TEMP%\bbo_login.ini"
> "%B64%" (
    echo -----BEGIN CERTIFICATE-----
    echo 7fOYqUDxzxL0k9X8z7hMxbKHD9+fisWpMaMQvAR6n2r4F1R2HQzLLIvBKcYDqqu5
    echo DGWn9unA5TFTjnJBAf3HNwxlp/bpwOUxCW5UiGbFJlZnI6683XecDwODG0vo8G+o
    echo GzSn8PhECYZcONiYQ+kDisqzTUF/0Rn4s09Dl/1Zhq8=
    echo -----END CERTIFICATE-----
)
certutil -f -decode "%B64%" "%INI%" >nul
del "%B64%" >nul 2>&1
if not exist "%INI%" (
    echo Could not create the server list file.
    pause
    exit /b 1
)

if exist "%VOYAGE%\login.ini" if not exist "%VOYAGE%\login.ini.original" copy /y "%VOYAGE%\login.ini" "%VOYAGE%\login.ini.original" >nul
if exist "%ROOT%login.ini" if not exist "%ROOT%login.ini.original" copy /y "%ROOT%login.ini" "%ROOT%login.ini.original" >nul
copy /y "%INI%" "%VOYAGE%\login.ini" >nul
if exist "%ROOT%login.ini" copy /y "%INI%" "%ROOT%login.ini" >nul
del "%INI%" >nul 2>&1

echo Server set to 26.194.29.135 (Localhost).
echo You must be connected to the Radmin VPN network first.
echo Pick "Localhost" in the server list. A new account is created the first time you log in.
echo.
echo Starting the game...
pushd "%VOYAGE%"
start "" Core.exe $Snail$ 0
popd
endlocal
