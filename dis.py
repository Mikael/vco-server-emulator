import struct, sys, capstone
path, a, b = sys.argv[1], int(sys.argv[2], 16), int(sys.argv[3], 16)
d = open(path, 'rb').read()
pe = struct.unpack_from('<I', d, 0x3c)[0]
nsec = struct.unpack_from('<H', d, pe+6)[0]
optsz = struct.unpack_from('<H', d, pe+20)[0]
opt = pe+24
base = struct.unpack_from('<I', d, opt+28)[0]
imp_rva = struct.unpack_from('<I', d, opt+104)[0]
secs = [struct.unpack_from('<8sIIII', d, opt+optsz+i*40) for i in range(nsec)]
def off(rva):
    for n, vs, va, rs, ro in secs:
        if va <= rva < va+max(vs, rs): return rva-va+ro
def rva(o):
    for n, vs, va, rs, ro in secs:
        if ro <= o < ro+rs: return o-ro+va
def cstr(o):
    e = d.index(b'\0', o); return d[o:e]
iat = {}
o = off(imp_rva)
while o:
    oft, _, _, nm, ft = struct.unpack_from('<IIIII', d, o)
    if not nm: break
    t, i = off(oft or ft), 0
    while True:
        v = struct.unpack_from('<I', d, t+i*4)[0]
        if not v: break
        iat[base+ft+i*4] = ('#%d' % (v & 0xffff)) if v & 0x80000000 else cstr(off(v)+2).decode('latin1')
        i += 1
    o += 20
def note(v):
    if v in iat: return 'IAT:' + iat[v]
    if v <= base: return None
    o = off(v-base)
    if o is None or o >= len(d): return None
    try: s = cstr(o)
    except ValueError: return None
    if 3 <= len(s) < 120 and all(32 <= c < 127 for c in s): return repr(s.decode('latin1'))
if len(sys.argv) > 4:
    while d[a:a+3] != b'\x55\x8b\xec': a -= 1
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
md.detail = True
def run(a, b):
    while a < b:
        last = None
        for ins in md.disasm(d[a:b], base + rva(a)):
            last = ins
            yield ins
        a = (off(last.address - base) + last.size) if last else a
        if a < b:
            a += 1

for ins in run(a, b):
    notes = []
    for op in ins.operands:
        v = op.imm if op.type == capstone.x86.X86_OP_IMM else (op.mem.disp if op.type == capstone.x86.X86_OP_MEM else None)
        if v is not None:
            n = note(v & 0xffffffff)
            if n: notes.append(n)
    print('%05x %08x  %-8s %-36s %s' % (off(ins.address-base), ins.address, ins.mnemonic, ins.op_str, ' ; '.join(notes)))

