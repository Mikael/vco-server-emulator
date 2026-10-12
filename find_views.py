import re
import sys

from asm import load


def scan(listing, target_re):
    ins = load(listing)
    out = {}
    for i, (off, text) in enumerate(ins):
        if not re.match(target_re, text):
            continue
        vid = None
        for off2, t2 in reversed(ins[max(0, i - 6):i]):
            m = re.match(r'push (0x[0-9a-f]+|\d+)$', t2)
            if m:
                vid = int(m.group(1), 0)
                break
        if vid is None or vid > 0x40:
            continue
        label = ''
        for off2, t2 in reversed(ins[max(0, i - 400):i]):
            m = re.search(r"'((?:dlg|Dlg|wnd|btn|grid|lst)[A-Za-z0-9_]*)'$", t2)
            if m:
                label = m.group(1)
                break
        out.setdefault(vid, []).append((off, label))
    return out


if __name__ == '__main__':
    for listing, target in (('wn3d', r'call 0x100911a0$'),):
        res = scan(listing, target)
        for vid in sorted(res):
            labels = sorted(set(l for _, l in res[vid] if l))
            print('view %2d: %3d uses in %s  %s' % (vid, len(res[vid]), listing, ', '.join(labels[:8])))
