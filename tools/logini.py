#!/usr/bin/env python3
"""Decrypt / encrypt Bounty Bay Online login.ini (and login122231.ini).

The client (Login.dll 0x10036d44..0x10036e0f) opens "<dir>\\login.ini" through the ClientTools ini service with the key
"SnailDes". ClientTools.dll DDecrypt (0x1007b9e0 DES_DecryptToMem, block routine 0x1007b530, key schedule 0x1007aed0,
encrypt-file 0x1007b620) is the well known "bit array" DES with standard tables (IP/E/P/S/PC1/PC2 read from ClientTools.dll
.data 0x100b61a8..0x100b6e88) except that bytes are expanded to bits LSB-first (ByteToBit 0x1007ad90: bit[i]=(b>>i)&1,
BitToByte 0x1007add0). ECB mode, 8-byte blocks.

Padding (encrypt, 0x1007b737): a final partial block of n (1..7) bytes is zero padded and its last byte set to 8-n.
If the plaintext length is a multiple of 8 no padding block is added. Decrypt (0x1007bb96): if the last byte L of the
last block is < 8 and the bytes [8-L .. 6] are all zero, only 8-L bytes are kept; otherwise the full block is kept.

usage:
  python logini.py decrypt <in> [out]            (prints to stdout when out is omitted)
  python logini.py encrypt <out> --plain <txt>   (encrypt an existing plaintext file)
  python logini.py encrypt <out> --ip 127.0.0.1 --port 3000 --name Localhost [--more "name,ip,port" ...]
  python logini.py roundtrip <in>                (decrypt + re-encrypt, compare bytes)
"""
import sys, argparse

KEY = b"SnailDes"

IP = [57,49,41,33,25,17,9,1,59,51,43,35,27,19,11,3,61,53,45,37,29,21,13,5,63,55,47,39,31,23,15,7,
      56,48,40,32,24,16,8,0,58,50,42,34,26,18,10,2,60,52,44,36,28,20,12,4,62,54,46,38,30,22,14,6]
IPR = [39,7,47,15,55,23,63,31,38,6,46,14,54,22,62,30,37,5,45,13,53,21,61,29,36,4,44,12,52,20,60,28,
       35,3,43,11,51,19,59,27,34,2,42,10,50,18,58,26,33,1,41,9,49,17,57,25,32,0,40,8,48,16,56,24]
E = [31,0,1,2,3,4,3,4,5,6,7,8,7,8,9,10,11,12,11,12,13,14,15,16,15,16,17,18,19,20,19,20,
     21,22,23,24,23,24,25,26,27,28,27,28,29,30,31,0]
P = [15,6,19,20,28,11,27,16,0,14,22,25,4,17,30,9,1,7,23,13,31,26,2,8,18,12,29,5,21,10,3,24]
PC1 = [56,48,40,32,24,16,8,0,57,49,41,33,25,17,9,1,58,50,42,34,26,18,10,2,59,51,43,35,
       62,54,46,38,30,22,14,6,61,53,45,37,29,21,13,5,60,52,44,36,28,20,12,4,27,19,11,3]
PC2 = [13,16,10,23,0,4,2,27,14,5,20,9,22,18,11,3,25,7,15,6,26,19,12,1,
       40,51,30,36,46,54,29,39,50,44,32,46,43,48,38,55,33,52,45,41,49,35,28,31]
SH = [1,1,2,2,2,2,2,2,1,2,2,2,2,2,2,1]
S = [
 [[14,4,13,1,2,15,11,8,3,10,6,12,5,9,0,7],[0,15,7,4,14,2,13,1,10,6,12,11,9,5,3,8],
  [4,1,14,8,13,6,2,11,15,12,9,7,3,10,5,0],[15,12,8,2,4,9,1,7,5,11,3,14,10,0,6,13]],
 [[15,1,8,14,6,11,3,4,9,7,2,13,12,0,5,10],[3,13,4,7,15,2,8,14,12,0,1,10,6,9,11,5],
  [0,14,7,11,10,4,13,1,5,8,12,6,9,3,2,15],[13,8,10,1,3,15,4,2,11,6,7,12,0,5,14,9]],
 [[10,0,9,14,6,3,15,5,1,13,12,7,11,4,2,8],[13,7,0,9,3,4,6,10,2,8,5,14,12,11,15,1],
  [13,6,4,9,8,15,3,0,11,1,2,12,5,10,14,7],[1,10,13,0,6,9,8,7,4,15,14,3,11,5,2,12]],
 [[7,13,14,3,0,6,9,10,1,2,8,5,11,12,4,15],[13,8,11,5,6,15,0,3,4,7,2,12,1,10,14,9],
  [10,6,9,0,12,11,7,13,15,1,3,14,5,2,8,4],[3,15,0,6,10,1,13,8,9,4,5,11,12,7,2,14]],
 [[2,12,4,1,7,10,11,6,8,5,3,15,13,0,14,9],[14,11,2,12,4,7,13,1,5,0,15,10,3,9,8,6],
  [4,2,1,11,10,13,7,8,15,9,12,5,6,3,0,14],[11,8,12,7,1,14,2,13,6,15,0,9,10,4,5,3]],
 [[12,1,10,15,9,2,6,8,0,13,3,4,14,7,5,11],[10,15,4,2,7,12,9,5,6,1,13,14,0,11,3,8],
  [9,14,15,5,2,8,12,3,7,0,4,10,1,13,11,6],[4,3,2,12,9,5,15,10,11,14,1,7,6,0,8,13]],
 [[4,11,2,14,15,0,8,13,3,12,9,7,5,10,6,1],[13,0,11,7,4,9,1,10,14,3,5,12,2,15,8,6],
  [1,4,11,13,12,3,7,14,10,15,6,8,0,5,9,2],[6,11,13,8,1,4,10,7,9,5,0,15,14,2,3,12]],
 [[13,2,8,4,6,15,11,1,10,9,3,14,5,0,12,7],[1,15,13,8,10,3,7,4,12,5,6,11,0,14,9,2],
  [7,11,4,1,9,12,14,2,0,6,10,13,15,3,5,8],[2,1,14,7,4,10,8,13,15,12,9,0,3,5,6,11]],
]


def _bits(block):  # ByteToBit, LSB first (ClientTools 0x1007ad90)
    return [(b >> i) & 1 for b in block for i in range(8)]


def _bytes(bits):  # BitToByte (0x1007add0)
    return bytes(sum(bits[j * 8 + i] << i for i in range(8)) for j in range(8))


def subkeys(key):
    k = _bits(key[:8].ljust(8, b"\0"))
    k = [k[i] for i in PC1]
    out = []
    for s in SH:
        c, d = k[:28], k[28:]
        k = c[s:] + c[:s] + d[s:] + d[:s]
        out.append([k[i] for i in PC2])
    return out


def _f(r, sk):
    x = [r[i] ^ sk[j] for j, i in enumerate(E)]
    o = []
    for n in range(8):
        b = x[n * 6:n * 6 + 6]
        v = S[n][(b[0] << 1) | b[5]][(b[1] << 3) | (b[2] << 2) | (b[3] << 1) | b[4]]
        o += [(v >> 3) & 1, (v >> 2) & 1, (v >> 1) & 1, v & 1]
    return [o[i] for i in P]


def _block(block, ks, decrypt):
    b = _bits(block)
    b = [b[i] for i in IP]
    L, R = b[:32], b[32:]
    order = range(15, -1, -1) if decrypt else range(16)
    last = 0 if decrypt else 15
    for n in order:
        f = _f(R, ks[n])
        L = [a ^ c for a, c in zip(L, f)]
        if n != last:
            L, R = R, L
    b = L + R
    return _bytes([b[i] for i in IPR])


def decrypt(data, key=KEY):
    ks = subkeys(key)
    out = bytearray()
    blocks = [data[i:i + 8] for i in range(0, len(data) - len(data) % 8, 8)]
    for i, blk in enumerate(blocks):
        p = _block(blk, ks, True)
        if i == len(blocks) - 1:
            L = p[7]
            if L < 8 and all(x == 0 for x in p[8 - L:7]):
                p = p[:8 - L]
        out += p
    return bytes(out)


def encrypt(data, key=KEY):
    ks = subkeys(key)
    out = bytearray()
    for i in range(0, len(data), 8):
        blk = data[i:i + 8]
        if len(blk) < 8:
            n = len(blk)
            blk = blk + b"\0" * (7 - n) + bytes([8 - n])
        out += _block(blk, ks, False)
    return bytes(out)


def make_ini(entries):
    """entries: list of (name, ip, port). Same key layout as the shipped file / the plaintext copy."""
    secs = []
    for i, (name, ip, port) in enumerate(entries):
        secs.append(f"[{i}]\r\nip={ip}\r\nport={port}\r\ntype=0\r\ndesc={name}\r\nhelp={name}\r\n"
                    f"helpcolor=255,0,255,255\r\nbackcolor=175,25,25,25")
    return "\r\n\r\n".join(secs).encode("latin-1")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=["decrypt", "encrypt", "roundtrip"])
    ap.add_argument("file")
    ap.add_argument("out", nargs="?")
    ap.add_argument("--plain")
    ap.add_argument("--ip", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=3000)
    ap.add_argument("--name", default="Localhost")
    ap.add_argument("--more", action="append", default=[], help="extra server 'name,ip,port'")
    a = ap.parse_args()
    if a.cmd == "decrypt":
        p = decrypt(open(a.file, "rb").read())
        if a.out:
            open(a.out, "wb").write(p)
        else:
            sys.stdout.buffer.write(p)
    elif a.cmd == "encrypt":
        if a.plain:
            p = open(a.plain, "rb").read()
        else:
            ents = [(a.name, a.ip, a.port)] + [tuple(m.split(",")) for m in a.more]
            p = make_ini(ents)
        c = encrypt(p)
        assert decrypt(c) == p
        open(a.file, "wb").write(c)
        print(f"wrote {a.file}: {len(c)} bytes ({len(p)} plaintext)")
    else:
        c = open(a.file, "rb").read()
        p = decrypt(c)
        c2 = encrypt(p)
        print("plaintext bytes:", len(p), "| re-encrypted identical:", c2 == c)
        sys.exit(0 if c2 == c else 1)


if __name__ == "__main__":
    main()
