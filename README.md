# vco-server-emulator

Local server for the 2007 Voyage Century Online client. Game files are not included.

## Setup

Put this folder inside your game folder, next to `voyage\`.

In `voyage\login.ini` set:

```
ip=127.0.0.1
port=3000
```

## Start the server

```
python server.py
```

Needs Python 3. It listens on 127.0.0.1 ports 3000 and 3001.

## Start the client

```
start_offline.bat
```

Click ENTER, pick the "Localhost" server, log in with any account, password and code.
