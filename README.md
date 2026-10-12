# Voyage Century Online server emulator

https://discord.gg/9R7ar6yacY

Requires the .NET 9 SDK. Place this folder next to the client's `voyage` folder, or set `ClientPath` in `config/emulator.json`.

Start the server:

    start-server.bat

Start the client (with `voyage/login.ini` pointing at 127.0.0.1 port 3000):

    voyage\Core.exe

`play-local.bat` does both.
