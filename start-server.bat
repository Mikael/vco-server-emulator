@echo off
cd /d "%~dp0"
dotnet run --project src\BountyBay.Server -c Release
pause
