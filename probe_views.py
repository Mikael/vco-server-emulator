import os
import subprocess
import sys
import time

from PIL import Image, ImageChops

HERE = os.path.dirname(os.path.abspath(__file__))
DUMPS = os.path.join(HERE, 'dumps')


def shot(name):
    path = os.path.join(DUMPS, name)
    subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
                    os.path.join(HERE, 'shot.ps1'), '-Out', path], capture_output=True)
    return Image.open(path).convert('RGB')


def inject(expr):
    with open(os.path.join(HERE, 'inject.py'), 'w', encoding='ascii') as f:
        f.write(expr)
    for _ in range(20):
        time.sleep(0.4)
        if not os.path.exists(os.path.join(HERE, 'inject.py')):
            return


def changed(a, b):
    diff = ImageChops.difference(a, b).convert('L').point(lambda v: 255 if v > 60 else 0)
    box = diff.getbbox()
    if not box:
        return None, 0
    return box, sum(1 for v in diff.crop(box).getdata() if v)


if __name__ == '__main__':
    first = int(sys.argv[1]) if len(sys.argv) > 1 else 0
    last = int(sys.argv[2]) if len(sys.argv) > 2 else 32
    for v in range(first, last + 1):
        base = shot('view_base.png')
        inject("[view_create(%d, 30), view_add(%d, 1, {'Name': 'NEW_004', 'Index': 1, 'Amount': %d, "
               "'Photo': 190150, 'Kind': 1, 'ItemType': 1})]" % (v, v, v + 1))
        time.sleep(2.5)
        if subprocess.run(['tasklist', '/FI', 'IMAGENAME eq Core.exe'], capture_output=True, text=True
                          ).stdout.count('Core.exe') == 0:
            print('view %d: GAME EXITED' % v)
            break
        img = shot('view_%d.png' % v)
        box, count = changed(base, img)
        print('view %2d: %s' % (v, 'changed %s (%d px)' % (box, count) if count > 1500 else 'no visible change'), flush=True)
        if count <= 1500:
            os.remove(os.path.join(DUMPS, 'view_%d.png' % v))
        if v != 3:
            inject('[view_delete(%d)]' % v)
            time.sleep(1.5)
