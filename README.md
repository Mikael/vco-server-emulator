# Bounty Bay Online server emulator

https://discord.gg/9R7ar6yacY

Requires the .NET 9 SDK, Python 3 and a Bounty Bay Online (ENG) client.

Point the client at this server (the client's `login.ini` is DES-encrypted):

    python tools\logini.py encrypt <client>\login.ini --ip 127.0.0.1 --port 3100 --name Localhost
    python tools\logini.py encrypt <client>\voyage\login.ini --ip 127.0.0.1 --port 3100 --name Localhost

Start the server:

    start-server.bat

Start the client:
    
    StartClient.bat


Settings are in `config\emulator.json`. Set `ClientPath` to the client's `voyage` folder.
