import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
lines = open(os.path.join(HERE, 'dumps', 'uilogic.asm'), encoding='utf-16' if open(
    os.path.join(HERE, 'dumps', 'uilogic.asm'), 'rb').read(2) == b'\xff\xfe' else 'utf-8', errors='replace').read().splitlines()

ins = []
for ln in lines:
    m = re.match(r'^([0-9a-f]+) ([0-9a-f]{8})\s+(\S+)\s*(.*?)\s*$', ln)
    if m:
        ins.append((int(m.group(2), 16), m.group(3), m.group(4)))

wrappers = {}
for i, (addr, mn, ops) in enumerate(ins):
    if mn == 'call' and ops.startswith('0x1007cc90'):
        nxt = [o for a, m2, o in ins[i + 1:i + 8] if m2 == 'call'][:1]
        if nxt and '+ 0x3c]' in nxt[0]:
            j = i
            while j > 0 and not (ins[j - 1][1] in ('nop', 'int3', 'ret')):
                j -= 1
            expect = None
            for a, m2, o in ins[i:i + 30]:
                mm = re.search(r'byte ptr \[eax \+ 0x10\], (\w+)', o)
                if m2 == 'cmp' and mm:
                    expect = int(mm.group(1), 0)
                    break
                if m2 == 'mov' and 'byte ptr [eax + 0x10]' in o:
                    expect = 0
                    break
            wrappers[ins[j][0]] = expect
print('wrappers (address: expected type):', {hex(k): v for k, v in wrappers.items()}, file=sys.stderr)

seen = {}
for i, (addr, mn, ops) in enumerate(ins):
    if mn == 'call':
        try:
            target = int(ops.split()[0], 16)
        except (ValueError, IndexError):
            continue
        if target in wrappers:
            for a, m2, o in reversed(ins[max(0, i - 14):i]):
                mm = re.search(r"'(.*)'$", o)
                if m2 == 'push' and mm:
                    seen.setdefault(mm.group(1), set()).add(wrappers[target])
                    break
for name in sorted(seen):
    print('%-24s type %s' % (name, ','.join(str(t) for t in sorted(seen[name], key=str))))
print(len(seen), 'names', file=sys.stderr)
