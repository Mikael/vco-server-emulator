import re

from asm import load

ins = load('gameproj')
rows = {}
for i, (off, text) in enumerate(ins):
    direct = text.startswith('call 0x10005140')
    via_socket = bool(re.match(r'call dword ptr \[e.. \+ 0x40\]$', text))
    if not (direct or via_socket):
        continue
    mid = size = None
    for off2, t2 in reversed(ins[max(0, i - 60):i]):
        m = re.match(r'mov word ptr \[e[bs]p(?: [+-] 0x[0-9a-f]+)?\], (0x[0-9a-f]+|\d+)$', t2)
        if m and mid is None:
            mid = int(m.group(1), 0)
    for off2, t2 in reversed(ins[max(0, i - 8):i]):
        m = re.match(r'push (0x[0-9a-f]+|\d+)$', t2)
        if m:
            size = int(m.group(1), 0)
            break
    if mid is None or mid > 0x40:
        continue
    rows.setdefault(mid, []).append((off, size, 'Send' if direct else 'socket'))
for mid in sorted(rows):
    sites = rows[mid]
    print('client msg 0x%02x: %d site(s): %s' % (mid, len(sites), ', '.join(
        '%x len=%s via %s' % (o, s if s else '?', how) for o, s, how in sites[:8])))
