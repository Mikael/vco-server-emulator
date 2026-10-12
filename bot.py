import socket
import struct
import sys
import time

from server import Deframer, LOGIN_PORT, START_POS, frame

name = sys.argv[1] if len(sys.argv) > 1 else 'botAnna'
seconds = float(sys.argv[2]) if len(sys.argv) > 2 else 40

s = socket.create_connection(('127.0.0.1', LOGIN_PORT))
s.settimeout(0.5)
de = Deframer()
seen = {}


def pump(duration):
    end = time.time() + duration
    while time.time() < end:
        try:
            data = s.recv(65536)
        except socket.timeout:
            continue
        if not data:
            raise SystemExit('server closed the connection')
        for body in de.feed(data):
            mid = struct.unpack_from('<H', body)[0]
            seen[mid] = seen.get(mid, 0) + 1
            if mid == 0x13:
                who = struct.unpack_from('<I', body, 2)[0]
                print('  heard object %d say %r' % (who, body[6:].split(b'\0')[0].decode('latin1')))


def send(body):
    s.sendall(frame(body))


pump(1)
send(struct.pack('<H', 7) + bytes(52))
pump(1)
send(struct.pack('<H', 2) + bytes(16) + name.encode().ljust(32, b'\0') + b'\0')
pump(2)
send(struct.pack('<H', 0x0f))
print('%s is in the world; received so far: %s' % (name, {hex(k): v for k, v in sorted(seen.items())}))

x0, z0 = START_POS[0] + 3, START_POS[2] + 3
corners = [(x0 + 4, z0), (x0 + 4, z0 + 4), (x0, z0 + 4), (x0, z0)]
end = time.time() + seconds
i = 0
while time.time() < end:
    x, z = corners[i % 4]
    send(struct.pack('<H2f', 0x12, x, z))
    if i % 2 == 0:
        send(struct.pack('<H', 0x0b) + ('hello from %s (%d)' % (name, i)).encode() + b'\0')
    pump(3)
    i += 1
print('done; received: %s' % {hex(k): v for k, v in sorted(seen.items())})
