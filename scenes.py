import glob
import os
import struct
import sys

RESOURCE = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'voyage', 'resource')
MATERIAL, LIGHT, LABEL, OBJECT = 56, 60, 108, 104


def scene_models(scene):
    ini = os.path.join(RESOURCE, 'ini', 'scenes', '%s.ini' % scene)
    try:
        lines = open(ini, encoding='gbk', errors='replace').read().splitlines()
    except OSError:
        return {}
    out, path = {}, ''
    for ln in lines:
        key, _, val = ln.partition('=')
        key, val = key.strip().upper(), val.split(';')[0].strip()
        if key == 'MODELPATH':
            path = val
        elif key == 'FILENAME' and val:
            out[val.lower()] = os.path.join(RESOURCE, 'MODELS', path, val + '.GM')
    return out


def read_gm(path):
    try:
        d = open(path, 'rb').read()
        (name_size, names, ntex, nmat, nlights, nlabels, nobj, ntri, nvb) = struct.unpack_from('<9i', d, 8)
        o = 0x48
        block = d[o:o + name_size]
        o += name_size + 4 * names + 4 * ntex + MATERIAL * nmat + LIGHT * nlights

        def name(off):
            return block[off:block.index(b'\0', off)].decode('latin1') if 0 <= off < len(block) else ''

        labels = []
        for i in range(nlabels):
            g, n, _ = struct.unpack_from('<3i', d, o)
            m = struct.unpack_from('<16f', d, o + 12)
            labels.append((name(g), name(n), (m[12], m[13], m[14])))
            o += LABEL
        counts = []
        for i in range(nobj):
            vb, _, _, nverts, _ = struct.unpack_from('<5i', d, o + 28)
            counts.append((vb, nverts))
            o += OBJECT
        o += 6 * ntri
        heads = [struct.unpack_from('<2i', d, o + 8 * i) for i in range(nvb)]
        o += 8 * nvb
        verts = []
        for i, (vtype, size) in enumerate(heads):
            n = sum(c for vb, c in counts if vb == i)
            stride = size // n if n else 36
            if stride >= 12:
                for k in range(size // stride):
                    verts.append(struct.unpack_from('<3f', d, o + k * stride))
            o += size
        return labels, verts
    except (OSError, struct.error, ValueError):
        return [], []


def spawn_point(scene):
    models = scene_models(scene)
    for name in ('walk_patch', 'walkpatch', 'walk'):
        _, vs = read_gm(models.get(name, ''))
        if len(vs) >= 3:
            xs = sorted(v[0] for v in vs)
            zs = sorted(v[2] for v in vs)
            mx, mz = xs[len(xs) // 2], zs[len(zs) // 2]
            return min(vs, key=lambda v: (v[0] - mx) ** 2 + (v[2] - mz) ** 2)
    return None


def locators(scene):
    return read_gm(scene_models(scene).get('locator', ''))[0]


if __name__ == '__main__':
    args = sys.argv[1:]
    brief = args == ['all']
    if brief:
        args = sorted(os.path.basename(p)[:-4] for p in glob.glob(os.path.join(RESOURCE, 'ini', 'scenes', '*.ini')))
    for s in args:
        sp = spawn_point(s)
        locs = locators(s)
        print('%s  spawn=%s  locators=%s' % (
            s, '(%.1f, %.1f, %.1f)' % sp if sp else None,
            len(locs) if brief else ['%s/%s (%.1f, %.1f, %.1f)' % ((g, n) + p) for g, n, p in locs]))
