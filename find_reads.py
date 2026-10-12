import re
import sys

from asm import load

GETTERS = {'aa60': 'u8', 'ac80': 'u16', 'aea0': 'u32', 'b0c0': 'float', 'b2f0': 'str'}

lo, hi = int(sys.argv[1], 16), int(sys.argv[2], 16)
seg = [r for r in load('gameproj') if lo <= r[0] < hi]
last = None
for i, (off, text) in enumerate(seg):
    m = re.search(r"push 0x[0-9a-f]+ '(.*)'$", text)
    if m:
        last = (off, m.group(1))
    m = re.match(r'call 0x1000([ab][0-9a-f]{3})$', text)
    if m and m.group(1) in GETTERS and last and off - last[0] < 24:
        nxt = next((x[1] for x in seg[i + 1:i + 4] if x[1].startswith('j')), '')
        print('%x %-18s %-6s %s' % (last[0], last[1], GETTERS[m.group(1)], nxt))
