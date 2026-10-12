import datetime
import os
import socket
import sys
import threading

LOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'capture.log')
lock = threading.Lock()


def log(msg):
    line = '%s %s' % (datetime.datetime.now().strftime('%H:%M:%S.%f')[:-3], msg)
    with lock:
        print(line, flush=True)
        with open(LOG, 'a', encoding='utf-8') as f:
            f.write(line + '\n')


def hexdump(data):
    rows = []
    for i in range(0, len(data), 16):
        chunk = data[i:i + 16]
        hexs = ' '.join('%02x' % b for b in chunk)
        text = ''.join(chr(b) if 32 <= b < 127 else '.' for b in chunk)
        rows.append('    %04x  %-47s  %s' % (i, hexs, text))
    return '\n'.join(rows)


def handle(conn, addr, port):
    tag = '[%d<-%s:%d]' % (port, addr[0], addr[1])
    log('%s CONNECTED' % tag)
    total = 0
    try:
        while True:
            data = conn.recv(65536)
            if not data:
                break
            total += len(data)
            log('%s RECV %d bytes\n%s' % (tag, len(data), hexdump(data)))
    except OSError as e:
        log('%s ERROR %s' % (tag, e))
    log('%s CLOSED after %d bytes' % (tag, total))
    conn.close()


def serve(port):
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind(('127.0.0.1', port))
    srv.listen(8)
    log('listening on 127.0.0.1:%d' % port)
    while True:
        conn, addr = srv.accept()
        threading.Thread(target=handle, args=(conn, addr, port), daemon=True).start()


if __name__ == '__main__':
    ports = [int(a) for a in sys.argv[1:]] or [3000]
    for p in ports[1:]:
        threading.Thread(target=serve, args=(p,), daemon=True).start()
    serve(ports[0])
