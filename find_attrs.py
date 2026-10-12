import glob
import os
import re
import struct
import sys

DLL_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'voyage', 'DLL')
SLOTS = (0x10, 0x14, 0x18, 0x1c, 0x20, 0x24, 0x28, 0x2c, 0x30, 0x34, 0x38)


def scan(path):
    d = open(path, 'rb').read()
    pe = struct.unpack_from('<I', d, 0x3c)[0]
    nsec = struct.unpack_from('<H', d, pe + 6)[0]
    opt = pe + 24
    base = struct.unpack_from('<I', d, opt + 28)[0]
    so = opt + struct.unpack_from('<H', d, pe + 20)[0]
    secs = [struct.unpack_from('<8sIIII', d, so + i * 40) for i in range(nsec)]

    def off(rva):
        for _, vs, va, rs, ro in secs:
            if va <= rva < va + max(vs, rs):
                return rva - va + ro

    out = {}
    slot_bytes = bytes(SLOTS)
    for m in re.finditer(rb'\x68(.{4})(?=.{0,10}?\xff[\x50-\x57]([' + re.escape(slot_bytes) + rb']))', d, re.S):
        o = off(struct.unpack('<I', m.group(1))[0] - base)
        if o is None:
            continue
        s = d[o:d.find(b'\0', o)]
        if 2 <= len(s) <= 15 and re.fullmatch(rb'[A-Z][A-Za-z0-9_]*', s):
            out.setdefault(s.decode(), set()).add(m.group(2)[0])
    return out


if __name__ == '__main__':
    want = sys.argv[1:] or ['GameProj.dll']
    names = {}
    for p in sorted(glob.glob(os.path.join(DLL_DIR, '*.dll'))):
        if os.path.basename(p) in want or want == ['all']:
            for s, slots in scan(p).items():
                names.setdefault(s, set()).update(slots)
    for s in sorted(names):
        print('%-16s %s' % (s, ' '.join('%x' % x for x in sorted(names[s]))))
    print(len(names), 'names', file=sys.stderr)
