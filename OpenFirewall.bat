@echo off
net session >nul 2>&1
if errorlevel 1 (
    echo Right-click this file and choose "Run as administrator".
    pause
    exit /b 1
)
netsh advfirewall firewall delete rule name="Bounty Bay Online server" >nul 2>&1
netsh advfirewall firewall add rule name="Bounty Bay Online server" dir=in action=allow protocol=TCP localport=3100,3101
echo Ports 3100 and 3101 are open for players.
pause
