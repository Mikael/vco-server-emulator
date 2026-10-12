import ctypes
import ctypes.wintypes as wt
import subprocess
import sys

k32 = ctypes.WinDLL('kernel32', use_last_error=True)
psapi = ctypes.WinDLL('psapi', use_last_error=True)
k32.OpenProcess.restype = wt.HANDLE
k32.ReadProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                  ctypes.POINTER(ctypes.c_size_t)]


class MODULEINFO(ctypes.Structure):
    _fields_ = [('base', ctypes.c_void_p), ('size', wt.DWORD), ('entry', ctypes.c_void_p)]


def find_pid(name):
    out = subprocess.check_output(['tasklist', '/FI', 'IMAGENAME eq %s' % name, '/FO', 'CSV', '/NH'], text=True)
    for line in out.splitlines():
        parts = line.strip('"').split('","')
        if len(parts) > 1 and parts[0].lower() == name.lower():
            return int(parts[1])
    raise SystemExit('%s is not running' % name)


def open_core():
    h = k32.OpenProcess(0x0410, False, find_pid('Core.exe'))
    if not h:
        raise SystemExit('OpenProcess failed: %d' % ctypes.get_last_error())
    return h


def modules(h):
    arr = (wt.HMODULE * 1024)()
    need = wt.DWORD()
    psapi.EnumProcessModulesEx(h, arr, ctypes.sizeof(arr), ctypes.byref(need), 3)
    res = {}
    for i in range(need.value // ctypes.sizeof(wt.HMODULE)):
        buf = ctypes.create_unicode_buffer(260)
        psapi.GetModuleBaseNameW(h, wt.HMODULE(arr[i]), buf, 260)
        mi = MODULEINFO()
        psapi.GetModuleInformation(h, wt.HMODULE(arr[i]), ctypes.byref(mi), ctypes.sizeof(mi))
        res[buf.value.lower()] = (mi.base, mi.size)
    return res


def read(h, addr, size):
    out = bytearray()
    for a in range(addr, addr + size, 0x1000):
        buf = ctypes.create_string_buffer(0x1000)
        n = ctypes.c_size_t()
        ok = k32.ReadProcessMemory(h, ctypes.c_void_p(a), buf, 0x1000, ctypes.byref(n))
        out += buf.raw if ok else b'\0' * 0x1000
    return bytes(out[:size])


if __name__ == '__main__':
    h = open_core()
    mods = modules(h)
    if len(sys.argv) < 3:
        for n, (b, s) in sorted(mods.items(), key=lambda x: x[1]):
            print('%08x %08x %s' % (b, s, n))
        raise SystemExit
    base, size = mods[sys.argv[1].lower()]
    data = read(h, base, size)
    open(sys.argv[2], 'wb').write(data)
    print('dumped %s base %08x size %x -> %s' % (sys.argv[1], base, size, sys.argv[2]))
