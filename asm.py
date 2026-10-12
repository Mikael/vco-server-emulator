import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def load(name):
    path = os.path.join(HERE, 'dumps', name if name.endswith('.asm') else name + '.asm')
    raw = open(path, 'rb').read()
    txt = raw.decode('utf-16') if raw[:2] == b'\xff\xfe' else raw.decode('utf-8', 'replace')
    out = []
    for ln in txt.splitlines():
        m = re.match(r'^([0-9a-f]+) ([0-9a-f]{8})\s+(.*?)\s*$', ln)
        if m:
            out.append((int(m.group(1), 16), re.sub(r'\s{2,}', ' ', m.group(3))))
    return out


def show(rows):
    for off, text in rows:
        sys.stdout.buffer.write(('%05x %s\n' % (off, text)).encode('utf-8', 'replace'))


if __name__ == '__main__':
    ins = load(sys.argv[1])
    cmd = sys.argv[2]
    if cmd == 'str':
        show([r for r in ins if "'" in r[1] and sys.argv[3] in r[1].split("'", 1)[1]])
    elif cmd == 'at':
        a = int(sys.argv[3], 16)
        n = int(sys.argv[4]) if len(sys.argv) > 4 else 60
        i = next((k for k, r in enumerate(ins) if r[0] >= a), len(ins))
        show(ins[i:i + n])
    elif cmd == 'calls':
        t = int(sys.argv[3], 16) & 0xfffff
        show([r for r in ins if re.match(r'(call|jmp) 0x1[0-9a-f]{2}%05x$' % t, r[1])])
    elif cmd == 'grep':
        lo = int(sys.argv[4], 16) if len(sys.argv) > 4 else 0
        hi = int(sys.argv[5], 16) if len(sys.argv) > 5 else 1 << 30
        show([r for r in ins if lo <= r[0] < hi and re.search(sys.argv[3], r[1])])
