@echo off
cd /d "%~dp0"
dotnet run --project src\VoyageCentury.Server -c Release
pause
