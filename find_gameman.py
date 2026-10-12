import ctypes
import ctypes.wintypes as wt
import struct

from dump_module import k32, modules, open_core


class MBI(ctypes.Structure):
    _fields_ = [('BaseAddress', ctypes.c_ulonglong), ('AllocationBase', ctypes.c_ulonglong),
                ('AllocationProtect', wt.DWORD), ('pad1', wt.DWORD), ('RegionSize', ctypes.c_ulonglong),
                ('State', wt.DWORD), ('Protect', wt.DWORD), ('Type', wt.DWORD), ('pad2', wt.DWORD)]


k32.VirtualQueryEx.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.POINTER(MBI), ctypes.c_size_t]
k32.VirtualQueryEx.restype = ctypes.c_size_t


def rd(h, addr, size):
    buf = ctypes.create_string_buffer(size)
    n = ctypes.c_size_t()
    if not k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, size, ctypes.byref(n)):
        return None
    return buf.raw[:n.value]


def regions(h):
    addr = 0x10000
    mbi = MBI()
    while addr < 0x7fff0000 and k32.VirtualQueryEx(h, ctypes.c_void_p(addr), ctypes.byref(mbi), ctypes.sizeof(mbi)):
        if mbi.State == 0x1000 and mbi.Protect in (0x04, 0x40) and mbi.Type == 0x20000:
            yield mbi.BaseAddress, mbi.RegionSize
        addr = mbi.BaseAddress + mbi.RegionSize


def main():
    h = open_core()
    mods = modules(h)
    voy, _ = mods['voyclient.dll']
    gp, gpsize = mods['gameproj.dll']
    sock_vt = struct.pack('<I', voy + 0xc3190)
    socks = set()
    blobs = []
    for base, size in regions(h):
        data = rd(h, base, size)
        if not data:
            continue
        blobs.append((base, data))
        i = data.find(sock_vt)
        while i >= 0:
            if i % 4 == 0:
                socks.add(base + i)
            i = data.find(sock_vt, i + 1)
    print('socket objects:', [hex(s) for s in socks])
    for base, data in blobs:
        for s in socks:
            pat = struct.pack('<I', s)
            i = data.find(pat)
            while i >= 0:
                obj = base + i - 0x38
                m = rd(h, obj, 0x60)
                if m:
                    vt0, vt50 = struct.unpack_from('<I', m, 0)[0], struct.unpack_from('<I', m, 0x50)[0]
                    if gp <= vt0 < gp + gpsize and gp <= vt50 < gp + gpsize:
                        tbl = rd(h, vt50, 0x30) or b''
                        print('DGameMan at %08x  state(+3c)=%d  vt=GameProj+%x  +0x50 vt=GameProj+%x' % (
                            obj, struct.unpack_from('<I', m, 0x3c)[0], vt0 - gp, vt50 - gp))
                        print('  methods:', ['%x' % (x - gp) for x in struct.unpack('<%dI' % (len(tbl) // 4), tbl)])
                i = data.find(pat, i + 1)


if __name__ == '__main__':
    main()
