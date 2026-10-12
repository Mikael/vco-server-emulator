import ctypes
import ctypes.wintypes as wt
import os
import struct
import sys
import time

from dump_module import find_pid, modules

k32 = ctypes.WinDLL('kernel32', use_last_error=True)
LOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'crashwatch.log')

EXCEPTION_DEBUG_EVENT = 1
DBG_CONTINUE = 0x00010002
DBG_EXCEPTION_NOT_HANDLED = 0x80010001
ACCESS_VIOLATION = 0xC0000005


class EXCEPTION_RECORD(ctypes.Structure):
    _fields_ = [('ExceptionCode', wt.DWORD), ('ExceptionFlags', wt.DWORD), ('ExceptionRecord', ctypes.c_void_p),
                ('ExceptionAddress', ctypes.c_void_p), ('NumberParameters', wt.DWORD),
                ('ExceptionInformation', ctypes.c_size_t * 15)]


class EXCEPTION_DEBUG_INFO(ctypes.Structure):
    _fields_ = [('ExceptionRecord', EXCEPTION_RECORD), ('dwFirstChance', wt.DWORD)]


class DEBUG_EVENT(ctypes.Structure):
    _fields_ = [('dwDebugEventCode', wt.DWORD), ('dwProcessId', wt.DWORD), ('dwThreadId', wt.DWORD),
                ('pad', wt.DWORD), ('Exception', EXCEPTION_DEBUG_INFO), ('fill', ctypes.c_byte * 64)]


class WOW64_FLOATING_SAVE_AREA(ctypes.Structure):
    _fields_ = [('a', wt.DWORD * 7), ('RegisterArea', ctypes.c_byte * 80), ('Cr0NpxState', wt.DWORD)]


class WOW64_CONTEXT(ctypes.Structure):
    _fields_ = [('ContextFlags', wt.DWORD), ('Dr', wt.DWORD * 6), ('FloatSave', WOW64_FLOATING_SAVE_AREA),
                ('SegGs', wt.DWORD), ('SegFs', wt.DWORD), ('SegEs', wt.DWORD), ('SegDs', wt.DWORD),
                ('Edi', wt.DWORD), ('Esi', wt.DWORD), ('Ebx', wt.DWORD), ('Edx', wt.DWORD), ('Ecx', wt.DWORD),
                ('Eax', wt.DWORD), ('Ebp', wt.DWORD), ('Eip', wt.DWORD), ('SegCs', wt.DWORD), ('EFlags', wt.DWORD),
                ('Esp', wt.DWORD), ('SegSs', wt.DWORD), ('ExtendedRegisters', ctypes.c_byte * 512)]


k32.OpenProcess.restype = wt.HANDLE
k32.OpenThread.restype = wt.HANDLE
k32.ReadProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                  ctypes.POINTER(ctypes.c_size_t)]


def out(msg):
    print(msg, flush=True)
    with open(LOG, 'a', encoding='utf-8') as f:
        f.write(msg + '\n')


def main():
    watch = float(sys.argv[1]) if len(sys.argv) > 1 else 120
    pid = find_pid('Core.exe')
    hproc = k32.OpenProcess(0x1F0FFF, False, pid)
    if not k32.DebugActiveProcess(pid):
        raise SystemExit('DebugActiveProcess failed: %d' % ctypes.get_last_error())
    k32.DebugSetProcessKillOnExit(False)
    out('--- attached to pid %d at %s' % (pid, time.strftime('%H:%M:%S')))
    mods = {}

    def where(addr):
        nonlocal mods
        if not mods:
            mods = modules(hproc)
        for name, (base, size) in mods.items():
            if base and base <= addr < base + size:
                return '%s+%x' % (name, addr - base)
        return '%08x' % addr

    ev = DEBUG_EVENT()
    end = time.time() + watch
    seen = 0
    while time.time() < end:
        if not k32.WaitForDebugEvent(ctypes.byref(ev), 500):
            continue
        status = DBG_CONTINUE
        if ev.dwDebugEventCode == EXCEPTION_DEBUG_EVENT:
            rec = ev.Exception.ExceptionRecord
            status = DBG_EXCEPTION_NOT_HANDLED
            if rec.ExceptionCode == 0x80000003 or rec.ExceptionCode == 0x4000001F:
                status = DBG_CONTINUE
            elif rec.ExceptionCode == ACCESS_VIOLATION and seen < 12:
                seen += 1
                mods = {}
                ht = k32.OpenThread(0x1FFFFF, False, ev.dwThreadId)
                ctx = WOW64_CONTEXT()
                ctx.ContextFlags = 0x10007
                k32.Wow64GetThreadContext(ht, ctypes.byref(ctx))
                k32.CloseHandle(ht)
                kind = 'write' if rec.ExceptionInformation[0] else 'read'
                out('ACCESS VIOLATION at %s  %s of %08x' % (where(ctx.Eip), kind, rec.ExceptionInformation[1]))
                out('  eax=%08x ebx=%08x ecx=%08x edx=%08x esi=%08x edi=%08x ebp=%08x esp=%08x' % (
                    ctx.Eax, ctx.Ebx, ctx.Ecx, ctx.Edx, ctx.Esi, ctx.Edi, ctx.Ebp, ctx.Esp))
                buf = ctypes.create_string_buffer(4 * 96)
                n = ctypes.c_size_t()
                if k32.ReadProcessMemory(hproc, ctypes.c_void_p(ctx.Esp), buf, len(buf), ctypes.byref(n)):
                    words = struct.unpack('<%dI' % (n.value // 4), buf.raw[:n.value // 4 * 4])
                    rets = [where(w) for w in words if '+' in where(w) and not where(w).startswith(('ntdll', 'kernel'))]
                    out('  stack: ' + ' '.join(rets[:14]))
            elif rec.ExceptionCode == 0xE06D7363 and seen < 12:
                seen += 1
                mods = {}
                ht = k32.OpenThread(0x1FFFFF, False, ev.dwThreadId)
                ctx = WOW64_CONTEXT()
                ctx.ContextFlags = 0x10007
                k32.Wow64GetThreadContext(ht, ctypes.byref(ctx))
                k32.CloseHandle(ht)
                buf = ctypes.create_string_buffer(4 * 160)
                n = ctypes.c_size_t()
                rets = []
                if k32.ReadProcessMemory(hproc, ctypes.c_void_p(ctx.Esp), buf, len(buf), ctypes.byref(n)):
                    words = struct.unpack('<%dI' % (n.value // 4), buf.raw[:n.value // 4 * 4])
                    rets = [where(w) for w in words if '+' in where(w) and not where(w).startswith(('ntdll', 'kernel', 'msvc'))]
                out('C++ EXCEPTION  stack: ' + ' '.join(rets[:16]))
            elif rec.ExceptionCode not in (0x406D1388, 0xE06D7363) and seen < 12:
                out('exception %08x at %s' % (rec.ExceptionCode, where(rec.ExceptionAddress or 0)))
        elif ev.dwDebugEventCode == 5:
            out('process exited')
            k32.ContinueDebugEvent(ev.dwProcessId, ev.dwThreadId, DBG_CONTINUE)
            break
        k32.ContinueDebugEvent(ev.dwProcessId, ev.dwThreadId, status)
    k32.DebugActiveProcessStop(pid)
    out('--- detached')


if __name__ == '__main__':
    main()
