import struct
import sys

from dump_module import modules, open_core
from find_gameman import rd, regions

h = open_core()
gp, gpsize = modules(h)['gameproj.dll']
vt = gp + (int(sys.argv[1], 16) if len(sys.argv) > 1 else 0x916e0)
pat = struct.pack('<I', vt)
for base, size in regions(h):
    data = rd(h, base, size)
    if not data:
        continue
    i = data.find(pat)
    while i >= 0:
        if i % 4 == 0:
            o = rd(h, base + i, 0x280)
            if o and len(o) >= 0x280:
                u = lambda off: struct.unpack_from('<I', o, off)[0]
                f = lambda off: struct.unpack_from('<f', o, off)[0]
                print('object at %08x' % (base + i))
                print('  id=(%d,%d) model(+b8)=%d ready(+bc)=%d flag(+10c)=%d  d0=%.2f' % (
                    u(0x30), u(0x34), u(0xb8), o[0xbc], o[0x10c], f(0xd0)))
                print('  pos(+184)=(%.2f, %.2f, %.2f)  dest(+174)=(%.2f, %.2f, %.2f)  orient(+190)=%.1f speed(+180)=%.2f' % (
                    f(0x184), f(0x188), f(0x18c), f(0x174), f(0x178), f(0x17c), f(0x190), f(0x180)))
                print('  cur(+19c)=(%.2f, %.2f, %.2f)  +fc=(%.2f, %.2f, %.2f)  cam dist(+130)=%.2f +12c=%.2f +134=%d' % (
                    f(0x19c), f(0x1a0), f(0x1a4), f(0xfc), f(0x100), f(0x104), f(0x130), f(0x12c), u(0x134)))
                print('  action(+16c)=%d (+170)=%d moving(+169)=%d' % (u(0x16c), u(0x170), o[0x169]))
        i = data.find(pat, i + 1)
