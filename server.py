import datetime
import json
import math
import random
import os
import socket
import struct
import sys
import threading
import time

import scenes

LOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'capture.log')
lock = threading.Lock()


def log(msg):
    line = '%s %s' % (datetime.datetime.now().strftime('%H:%M:%S.%f')[:-3], msg)
    with lock:
        print(line, flush=True)
        with open(LOG, 'a', encoding='utf-8') as f:
            f.write(line + '\n')


def hexdump(data):
    rows = []
    for i in range(0, len(data), 16):
        chunk = data[i:i + 16]
        hexs = ' '.join('%02x' % b for b in chunk)
        text = ''.join(chr(b) if 32 <= b < 127 else '.' for b in chunk)
        rows.append('    %04x  %-47s  %s' % (i, hexs, text))
    return '\n'.join(rows)


def frame(body):
    return body.replace(b'\xee', b'\xee\x00') + b'\xee\xee'


class Deframer:

    def __init__(self):
        self.buf = bytearray()
        self.prev = 0

    def feed(self, data):
        out = []
        for c in data:
            if c == 0xEE and self.prev == 0xEE:
                body = bytes(self.buf[:-1])
                if body:
                    out.append(body)
                self.buf.clear()
                self.prev = 0
                continue
            if not (c == 0 and self.prev == 0xEE):
                self.buf.append(c)
            self.prev = c
        return out


def hello():
    return struct.pack('<HIII', 0x32, 0x11111111, 0x400, 0x22222222) + bytes(range(32)) + bytes(20)


def verify_picture(w=64, h=20):
    px = []
    for y in range(h):
        for x in range(w):
            on = (x // 4 + y // 4) % 2
            px.append(0x00FFD060 if on else 0x00203040)
    return struct.pack('<HII', 0x31, w, h) + struct.pack('<%dI' % len(px), *px)


LIST_PORT = 3000
LOGIN_PORT = 3001


def server_list():
    name = b'Localhost'.ljust(32, b'\0')
    entry = name + struct.pack('<I', 1) + b'127.0.0.1'.ljust(16, b'\0') + struct.pack('<I', LOGIN_PORT)
    return struct.pack('<HI', 0x28, 1) + entry


REPLY = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'reply.hex')


def scripted_replies():
    out = []
    try:
        for line in open(REPLY, encoding='utf-8'):
            line = line.split('#')[0].strip()
            if line == 'picture':
                out.append(verify_picture())
            elif line:
                out.append(bytes.fromhex(line))
    except (OSError, ValueError) as e:
        log('reply.hex: %s' % e)
    return out


SKILLS = tuple((i, 0) for i in range(7))


def key(a, b):
    return a | (b << 24)


MAN, WOMAN = 0, 1
ROLES = ((3001001, key(MAN, 0)), (3001002, key(WOMAN, 0)))
FACES = tuple((f, key(MAN, 0)) for f in range(3101001, 3101010)) + tuple((f, key(WOMAN, 0)) for f in range(3102001, 3102007))
BODIES = tuple((b, key(MAN, 0)) for b in (3401001, 3401002, 3401003)) + tuple((b, key(WOMAN, 0)) for b in (3402001, 3402002, 3402003))
CITIES = tuple((c, key(0, 0)) for c in (3, 5, 7, 9, 11, 13, 15, 17, 21, 23))


def create_info(points=len(SKILLS), tables=(ROLES, FACES, BODIES, SKILLS, CITIES)):
    body = struct.pack('<HI5I', 0x1a, points, *(len(t) for t in tables))
    for t in tables:
        for a, b in t:
            body += struct.pack('<II', a, b)
    return body


CHAR_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'characters.json')


_ctx = threading.local()
_file_lock = threading.Lock()


def _all_accounts():
    try:
        with open(CHAR_FILE, encoding='utf-8') as f:
            data = json.load(f)
    except (OSError, ValueError):
        return {}
    return {'_unclaimed': data} if isinstance(data, list) else data


def load_chars():
    with _file_lock:
        data = _all_accounts()
        acct = getattr(_ctx, 'account', '') or '_unclaimed'
        if acct not in data and '_unclaimed' in data and acct != '_unclaimed':
            return data['_unclaimed']
        return data.get(acct, [])


def save_chars(chars):
    with _file_lock:
        data = _all_accounts()
        acct = getattr(_ctx, 'account', '') or '_unclaimed'
        if acct != '_unclaimed':
            data.pop('_unclaimed', None)
        data[acct] = chars
        with open(CHAR_FILE, 'w', encoding='utf-8') as f:
            json.dump(data, f, indent=1)


def parse_create(body):
    name = body[2:0x22].split(b'\0')[0].decode('latin1')
    n = body[0x28]
    skills = [[body[0x29 + 2 * i], body[0x2a + 2 * i]] for i in range(n)]
    return {'name': name, 'choices': list(body[0x22:0x28]), 'skills': skills,
            'role': ROLES[0][0], 'face': FACES[0][0], 'body': BODIES[0][0]}


def char_list(chars=()):
    body = bytearray(0x43 + 40 + 92)
    struct.pack_into('<H', body, 0, 1)
    for i, ch in enumerate(list(chars)[:2]):
        o = 92 * i
        body[o + 0x0f:o + 0x0f + 32] = ch['name'].encode('latin1')[:31].ljust(32, b'\0')
        struct.pack_into('<10I', body, o + 0x43, ch['role'], ch['face'], ch['body'], 0, 0, 0, 0, 0, 0, 0)
    return bytes(body)

T_U8, T_U16, T_U32, T_FLOAT, T_STR, T_LSTR = range(6)

PROPERTIES = (
    ('Name', T_STR), ('NameShow', T_STR), ('ConsName', T_STR),('Money', T_U32), ('Flag', T_U32), ('TotalIntegral', T_U32),
    ('Load', T_FLOAT), ('LoadMax', T_FLOAT), ('VisualRange', T_FLOAT), ('WindOrient', T_FLOAT),
    ('SelectState', T_U8), ('StallBuyMax', T_U8), ('StallSellMax', T_U8), ('TeamOceanMode', T_U8),
    ('Model', T_U32), ('ActionSet', T_U32), ('CollideRadius', T_FLOAT), ('IsRun', T_U32),
    ('GameMaster', T_U32), ('WeaponEffect', T_U32), ('Face', T_U32), ('BodyType', T_U32),
    ('Hair', T_U32), ('HairColor', T_U32), ('TexColor', T_U32), ('Hat', T_U32), ('CapColor', T_U32),
    ('LeftHand', T_U32), ('RightHand', T_U32), ('ArmToxicLevel', T_U32),
    ('HP', T_U32), ('MaxHP', T_U32), ('SP', T_U32), ('MaxSP', T_U32), ('Level', T_U32), ('Dead', T_U8),
    ('PosX', T_FLOAT), ('PosY', T_FLOAT), ('PosZ', T_FLOAT), ('Orient', T_FLOAT), ('Country', T_U32),
    ('Title', T_STR), ('Notoriety', T_U32), ('Credit', T_U32),
    ('LandModel', T_U32), ('ShipModel', T_U32), ('BankMoney', T_U32),
    ('S/ModelCannonL', T_U32), ('S/ModelCannonR', T_U32), ('S/NumCannonL', T_U32), ('S/NumCannonR', T_U32),
    ('S/SailTex', T_U32), ('S/Flag', T_U32), ('S/ArmArmor', T_U32), ('AutoDrive', T_U32),
    ('RatioSailHP', T_U32), ('S/RandSeed', T_U32), ('MarineStrategy', T_U32), ('S/FishNet', T_U32),
    ('S/ShipDamage', T_U32), ('SailState', T_U32), ('Bumping', T_U32),
    ('S/Level', T_U32), ('S/MastFront', T_U32), ('S/MastMiddle', T_U32), ('S/MastBack', T_U32),
    ('HaveConsTask', T_U32), ('SDamageReduce', T_U32), ('SDamageAdd', T_U32), ('FireAnchor', T_U32),
    ('RotSpeedAdd', T_U32),
    ('S/Name', T_STR),
    ('Index', T_U32), ('Kind', T_U32), ('ItemType', T_U32), ('Amount', T_U32), ('Photo', T_U32),
    ('Count', T_U32), ('Cost', T_U32), ('Quality', T_U32), ('Type', T_U32),
    ('Hour', T_U8), ('WeatherType', T_U8), ('WindLevel', T_FLOAT), ('StreamOrient', T_FLOAT),
)
PROP_INDEX = {name: i for i, (name, _) in enumerate(PROPERTIES)}


def encode_props(values):
    out = b''
    for name, v in values.items():
        i = PROP_INDEX[name]
        t = PROPERTIES[i][1]
        out += struct.pack('<H', i)
        if t == T_U8:
            out += struct.pack('<B', v)
        elif t == T_U16:
            out += struct.pack('<H', v)
        elif t == T_U32:
            out += struct.pack('<I', v & 0xffffffff)
        elif t == T_FLOAT:
            out += struct.pack('<f', v)
        elif t == T_STR:
            s = v.encode('latin1')[:255]
            out += struct.pack('<B', len(s)) + s
        else:
            s = v.encode('latin1')
            out += struct.pack('<H', len(s)) + s
    return len(values), out


def player_props(values):
    n, data = encode_props(values)
    return struct.pack('<HH', 0x0b, n) + data


def object_props(obj_id, values):
    n, data = encode_props(values)
    return struct.pack('<HIH', 0x0d, obj_id, n) + data


def property_table(props=PROPERTIES):
    body = struct.pack('<HH', 9, len(props))
    for name, typ in props:
        body += name.encode('ascii').ljust(16, b'\0')[:16] + bytes([typ])
    return body


START_SCENE = 1001001


def enter_world(player_id=1, scene=START_SCENE):
    return struct.pack('<HIIII', 3, player_id, 0, 0, scene)


OBJ_LAND_ROLE, OBJ_OCEAN_ROLE = 0x01, 0x68


def create_object(obj_id, serial, kind, model):
    return struct.pack('<HIIBI', 7, obj_id, serial, kind, model)


START_POS = (-87.65, 13.81, 116.74)

ACT_STAND, ACT_WALK, ACT_JOG, ACT_RUN = 7, 12, 13, 14


def _action_codes():
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'voyage', 'resource', 'ini', 'actgroup.ini')
    codes, inside = {}, False
    try:
        for ln in open(path, encoding='gbk', errors='replace'):
            ln = ln.strip()
            if ln.startswith('['):
                inside = ln.lower() == '[prior]'
            elif inside and '=' in ln:
                k, v = ln.split('=', 1)
                if k.strip().isdigit():
                    codes[int(k)] = v.strip()
    except OSError:
        pass
    return [codes.get(i, '') for i in range(max(codes) + 1)] if codes else []


ACTION_CODES = _action_codes()
RUN_SPEED = 4.0


class Mover:

    def __init__(self, x, z):
        self.sx, self.sz, self.dx, self.dz = x, z, x, z
        self.t0, self.speed = time.time(), RUN_SPEED

    def now(self):
        dist = math.hypot(self.dx - self.sx, self.dz - self.sz)
        if dist < 1e-6:
            return self.dx, self.dz
        f = min(1.0, (time.time() - self.t0) * self.speed / dist)
        return self.sx + (self.dx - self.sx) * f, self.sz + (self.dz - self.sz) * f

    def walk_to(self, x, z):
        self.sx, self.sz = self.now()
        self.dx, self.dz, self.t0 = x, z, time.time()
        return self.sx, self.sz


def place_object(obj_id, x, y, z, orient=0, dest=None, speed=0, action=(0, 0, 0)):
    dx, dz = dest if dest else (x, z)
    return struct.pack('<HI3fH2fHHhH', 0x2a, obj_id, x, y, z, orient, dx, dz, speed, *action)


COMMAND_NAMES = frozenset('''AddBlacklist AddFriend AddShortcut AdjShipSpeed ApplyConsDept Buy BuySailor BuyShip CancelStallItem ChangeTeamOceanMode CheckStallBuy CloseConsDept ConsFireOut ConsRelationCmd Contraband CreateCons CreateConsDept DepositMoney display DoAction EncashMoney ERROR FireSailor GetPlayerInfo GetRecvItem HandToShortcut ItemPwdLogin ItemPwdSet JoinCons KickOut MakingItem nt QMI QueryRebuildShip QuerySTObj RebuildShip RebuildStatuary ReferConsTaskItem RemoveBlacklist RemoveFriend RemoveItem RemoveShortcut RemoveSM Repair rm SailDye SaveItem SaveSM SeeMemory SeeMemoryNum SeeRw_List SeeRw_ListNum Select Sell SellCard SendItem SendMoney SendSM SetConsDept SetConsDuty SetConsTenet SetExchange SetTitle ShipDye ShortcutPage Skill StallBuy StallSell StartStall State StatueSetMessage TakeUp TeamInvite TeamObj TransferConsProffer Unlay UseTo UseToBoxItem UseUniqueSkill WarCitySetting'''.split())


def chat(obj_id, text):
    return struct.pack('<HI', 0x13, obj_id) + text.encode('latin1', 'replace') + b'\0'


def table_create(table_id, name, columns):
    body = struct.pack('<H', 0x22) + name.encode('ascii').ljust(16, b'\0')[:16] + bytes([table_id, len(columns)])
    for cname, ctype in columns:
        body += cname.encode('ascii').ljust(16, b'\0')[:16] + bytes([ctype])
    return body


def table_rows(table_id, columns, rows):
    body = struct.pack('<HBH', 0x23, table_id, len(rows))
    for row in rows:
        for (cname, ctype), v in zip(columns, row):
            if ctype == T_U8:
                body += struct.pack('<B', v)
            elif ctype == T_U16:
                body += struct.pack('<H', v)
            elif ctype == T_U32:
                body += struct.pack('<I', v)
            elif ctype == T_FLOAT:
                body += struct.pack('<f', v)
            else:
                s = str(v).encode('latin1')[:255]
                body += struct.pack('<B' if ctype == T_STR else '<H', len(s)) + s
    return body


def npc_talk(obj_id, entries, kind=3, serial=0):
    body = struct.pack('<HIIB', 0x16, obj_id, serial, kind)
    for etype, number, text in entries:
        t = text.encode('latin1', 'replace')
        body += struct.pack('<Bhh', etype, number, len(t)) + t
    return body + struct.pack('<Bhh', 0, 0, 0) + b'\0'


def view_create(view, capacity):
    return struct.pack('<HBBH', 0x1c, view, 0, capacity)


def view_delete(view):
    return struct.pack('<HB', 0x1d, view)


def view_add(view, index, values, slot=0):
    n, data = encode_props(values)
    return struct.pack('<HBIIH', 0x1f, view, slot, index, n) + data


def view_update(view, index, values):
    n, data = encode_props(values)
    return struct.pack('<HBIH', 0x21, view, index, n) + data


def view_remove(view, index):
    return struct.pack('<HBI', 0x20, view, index)


VIEW_BAG_ITEMS = 3
BAG_SLOTS = 30

STARTER_ITEMS = [['NEW_004', 190150, 5], ['NEW_005', 190151, 5], ['NEW_001', 190182, 1]]

VIEW_BAG_EQUIP, VIEW_BAG_ARTICLE = 1, 2
STARTER_BAGS = {1: {1: ['IDName_XZB000001', 110101, 1], 2: ['IDName_ZB003292', 110201, 1]}, 2: {}, 4: {}}

VIEW_EQUIPPED = 4
VIEW_SHORTCUTS = 5
VIEW_TRADE_MINE, VIEW_TRADE_THEIRS = 12, 13
VIEW_SHOP_EQUIP, VIEW_SHOP_ITEM, VIEW_SHOP_MATERIAL = 15, 17, 23
VIEW_BOOTH, VIEW_SHIPYARD, VIEW_TRADE_GOODS, VIEW_PROVISION, VIEW_BANK = 16, 18, 21, 24, 30

GOODS = {
    'NEW_004': (190150, 40), 'NEW_005': (190151, 40), 'NEW_006': (190152, 40),
    'NEW_007': (190153, 120), 'NEW_008': (190154, 120), 'NEW_001': (190182, 200),
    'NEW_009': (190155, 30), 'NEW_010': (190157, 30),
}
PRICES = {name: price for name, (photo, price) in GOODS.items()}


def shelf(*names):
    return [(n, GOODS[n][0], GOODS[n][1]) for n in names]


SHOPS = {
    101: {VIEW_SHOP_ITEM: shelf('NEW_004', 'NEW_005', 'NEW_006', 'NEW_007', 'NEW_008', 'NEW_001'),
          VIEW_SHOP_MATERIAL: shelf('NEW_009', 'NEW_010'),
          VIEW_SHOP_EQUIP: []},
}


def scene_state(hour=12, weather=0, wind_level=1.0, wind_orient=0.0, stream_orient=0.0):
    n, data = encode_props({'Hour': hour, 'WeatherType': weather, 'WindLevel': wind_level,
                            'WindOrient': wind_orient, 'StreamOrient': stream_orient})
    return struct.pack('<HH', 0x0c, n) + data


def show_object(obj_id, visible=True):
    return struct.pack('<HIB', 0x27, obj_id, 1 if visible else 0)


KIND_SELF, KIND_PLAYER, KIND_NPC = 1, 2, 3
KIND_DOOR, KIND_GOODS, KIND_CASTLE = 7, 8, 0x0d
KIND_SEA_SELF, KIND_SEA_PLAYER, KIND_SEA_NPC = 4, 5, 6
KIND_OCEAN_SELF, KIND_OCEAN_PLAYER, KIND_OCEAN_NPC = 0x68, 0x69, 0x6a

WORLD_NPCS = (
    (100, KIND_NPC, 3201001, 'Townsman', 1.0, 2.0),
    (101, KIND_NPC, 3201002, 'Merchant', -3.0, 4.0),
)


RESOURCE = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'voyage', 'resource')


def scene_spawn(scene):
    return scenes.spawn_point(scene) or (0.0, 0.0, 0.0)


OPT_TRADE, OPT_BANK, OPT_BYE, OPT_SAIL, OPT_DEPOSIT, OPT_WITHDRAW = 1, 2, 3, 4, 5, 6
T_TEXT, T_OPTION = 0, 2
DEFAULT_DIALOGUE = [(T_TEXT, 0, 'Good day.'), (T_OPTION, OPT_BYE, 'Goodbye')]
DIALOGUES = {
    100: [(T_TEXT, 0, 'Welcome to Athens. The docks are to the east.'),
          (T_OPTION, OPT_SAIL, 'Sail off'), (T_OPTION, OPT_BYE, 'Goodbye')],
    101: [(T_TEXT, 0, 'Have a look at my wares.'),
          (T_OPTION, OPT_TRADE, 'Talk about trading'), (T_OPTION, OPT_BANK, 'Bank'), (T_OPTION, OPT_BYE, 'Goodbye')],
}

NPC_LINES = {
    100: 'Welcome to Athens, %s. The docks are to the east.',
    101: 'Have a look at my wares, %s.',
}


ACT_ATTACK = ACTION_CODES.index('103') if '103' in ACTION_CODES else 6
RESPAWN_SECONDS = 30
MONSTERS = {
    200: {'name': 'Dock Thug', 'model': 3201003, 'hp': 60, 'hit': (4, 9), 'gold': 25, 'at': (8.0, 6.0)},
    201: {'name': 'Smuggler', 'model': 3201004, 'hp': 90, 'hit': (6, 12), 'gold': 45, 'at': (11.0, 9.0)},
}
monster_state = {}
HEALING = {'NEW_004': 30, 'NEW_005': 30, 'NEW_006': 30, 'NEW_007': 60, 'NEW_008': 100}


def monster_messages(oid):
    info = MONSTERS[oid]
    hp = monster_state.get(oid, {}).get('hp', info['hp'])
    x, z = START_POS[0] + info['at'][0], START_POS[2] + info['at'][1]
    return [create_object(oid, 0, KIND_NPC, info['model']),
            object_props(oid, {'Name': info['name'], 'Model': info['model'], 'ActionSet': 0, 'CollideRadius': 0.5,
                               'HP': hp, 'MaxHP': info['hp'], 'Level': 1}),
            place_object(oid, x, START_POS[1], z), show_object(oid)]


def respawn_monster(oid):
    with world_lock:
        monster_state[oid] = {'hp': MONSTERS[oid]['hp'], 'dead': False}
        players = [c for c in in_world if c.scene == START_SCENE]
    for c in players:
        try:
            for body in monster_messages(oid):
                c.send(body, '(monster returns)')
        except OSError:
            pass


def _sea_scenes():
    out = set()
    folder = os.path.join(RESOURCE, 'ini', 'scenes')
    try:
        names = os.listdir(folder)
    except OSError:
        return out
    for n in names:
        if n.lower().endswith('.ini') and n[:-4].isdigit():
            head = open(os.path.join(folder, n), encoding='gbk', errors='replace').read().upper().split('[')[0]
            words = {ln.strip() for ln in head.splitlines()}
            if 'SEA' in words and 'CITY' not in words:
                out.add(int(n[:-4]))
    return out


SEA_SCENES = _sea_scenes()
SHIP_MODEL = 2011001

world_lock = threading.Lock()
in_world = []


class Conn:
    def __init__(self, sock, addr, port):
        self.sock, self.port = sock, port
        self.pos = None
        self.slot = 0
        self.name = ''
        self.mine = {}
        self.items = None
        self.bags = {}
        self.talking_to = 0
        self.at_sea = False
        self.account = ''
        self.hp = self.max_hp = 100
        self.force_land = True
        self.sent_inventory = False
        self.shop = {}
        self.money = 1000
        self.scene = START_SCENE
        self.height = START_POS[1]
        self.tag = '[%d %s:%d]' % (port, addr[0], addr[1])
        self.wlock = threading.Lock()
        with world_lock:
            Conn.next_id += 1
            self.pid = Conn.next_id

    next_id = 1000

    def send(self, body, note=''):
        log('%s SEND id=0x%04x len=%d %s\n%s' % (self.tag, struct.unpack_from('<H', body)[0], len(body), note,
                                                 hexdump(body)))
        with self.wlock:
            self.sock.sendall(frame(body))

    def others(self):
        with world_lock:
            return [c for c in in_world if c is not self and c.scene == self.scene and c.pos]

    def to_others(self, body, note):
        for c in self.others():
            try:
                c.send(body, note)
            except OSError:
                pass

    def public_props(self):
        keep = ('Name', 'Model', 'ActionSet', 'CollideRadius', 'Face', 'BodyType', 'Hair', 'HairColor',
                'TexColor', 'Hat', 'CapColor', 'LeftHand', 'RightHand', 'HP', 'MaxHP', 'Level', 'Title')
        return {k: v for k, v in self.mine.items() if k in keep}

    def show_to(self, other):
        x, z = self.pos.now()
        other.send(create_object(self.pid, 0, KIND_PLAYER, 3001001), '(player %s appears)' % self.name)
        other.send(object_props(self.pid, self.public_props()), '(player %s props)' % self.name)
        other.send(place_object(self.pid, x, self.height, z), '(player %s position)' % self.name)
        other.send(show_object(self.pid), '(player %s visible)' % self.name)

    def leave_scene(self):
        self.to_others(struct.pack('<HII', 8, self.pid, 0), '(player %s leaves)' % self.name)
        with world_lock:
            if self in in_world:
                in_world.remove(self)

    def spawn(self, npc):
        oid, kind, model, name, dx, dz = npc
        self.send(create_object(oid, 0, kind, model), '(spawn %s)' % name)
        self.send(object_props(oid, {'Name': name, 'Model': model, 'ActionSet': 0, 'CollideRadius': 0.5,
                                     'HP': 100, 'MaxHP': 100, 'Level': 1}), '(props %s)' % name)
        self.send(place_object(oid, START_POS[0] + dx, START_POS[1], START_POS[2] + dz), '(place %s)' % name)
        self.send(show_object(oid), '(show %s)' % name)

    def enter_scene(self, scene, pos=None):
        if pos is None:
            pos = START_POS if scene == START_SCENE else scene_spawn(scene)
        self.shop = {}
        if self.pos:
            self.leave_scene()
        self.scene = scene
        self.send(enter_world(scene=scene), '(enter scene %d)' % scene)
        self.at_sea = scene in SEA_SCENES and not self.force_land
        if self.at_sea:
            pos = (pos[0], 0.0, pos[2])
            self.send(create_object(1, 0, KIND_SEA_SELF, SHIP_MODEL), '(spawn ship)')
            ship = dict(self.mine, Model=SHIP_MODEL, ShipModel=SHIP_MODEL, Dead=0, AutoDrive=0, RatioSailHP=100,
                        Title=self.mine.get('Title', ''), **{'S/Name': "%s's ship" % self.name},
                        SailState=0, Bumping=0, MarineStrategy=0)
            ship.update({'S/ModelCannonL': 0, 'S/ModelCannonR': 0, 'S/NumCannonL': 0, 'S/NumCannonR': 0,
                         'S/SailTex': 0, 'S/Flag': 0, 'S/RandSeed': 1, 'S/FishNet': 0, 'S/ShipDamage': 0,
                         'S/Level': 0, 'S/MastFront': 2211101, 'S/MastMiddle': 2211401, 'S/MastBack': 2211101,
                         'HaveConsTask': 0, 'SDamageReduce': 0, 'SDamageAdd': 0, 'FireAnchor': 0, 'RotSpeedAdd': 0})
            self.send(object_props(1, ship), '(ship properties)')
        else:
            self.send(create_object(1, 0, KIND_SELF, 3001001), '(spawn player)')
            self.send(object_props(1, self.mine), '(player object properties)')
        self.send(place_object(1, *pos), '(place player)')
        self.send(show_object(1), '(show player)')
        self.send(scene_state(), '(time, weather, wind)')
        self.pos = Mover(pos[0], pos[2])
        self.height = pos[1]
        if scene == START_SCENE:
            for npc in WORLD_NPCS:
                self.spawn(npc)
            for oid in MONSTERS:
                if not monster_state.get(oid, {}).get('dead'):
                    for body in monster_messages(oid):
                        self.send(body, '(monster)')
        if not self.sent_inventory:
            self.sent_inventory = True
            self.send_inventory()
        with world_lock:
            in_world.append(self)
        for c in self.others():
            try:
                c.show_to(self)
                self.show_to(c)
            except OSError:
                pass

    def send_inventory(self):
        if self.items is None:
            chars = load_chars()
            saved = chars[self.slot].get('items') if self.slot < len(chars) else None
            if saved is None:
                saved = {str(i): list(it) for i, it in enumerate(STARTER_ITEMS, 1)}
            self.items = {int(k): v for k, v in saved.items()}
            other = (chars[self.slot].get('bags') if self.slot < len(chars) else None) or STARTER_BAGS
            self.bags = {int(v): {int(k): list(it) for k, it in slots.items()} for v, slots in other.items()}
            for v in (VIEW_BAG_EQUIP, VIEW_BAG_ARTICLE, VIEW_EQUIPPED):
                self.bags.setdefault(v, {})
        for view, slots in sorted(self.bags.items()):
            self.send(view_create(view, BAG_SLOTS), '(view %d)' % view)
            for slot, (name, photo, amount) in sorted(slots.items()):
                self.send(view_add(view, slot, {'Name': name, 'Index': slot, 'Amount': amount, 'Photo': photo,
                                                'Kind': 1, 'ItemType': 1}), '(view %d item %s)' % (view, name))
        self.send(view_create(VIEW_BAG_ITEMS, BAG_SLOTS), '(bag view)')
        for slot, (name, photo, amount) in sorted(self.items.items()):
            self.send(view_add(VIEW_BAG_ITEMS, slot, {'Name': name, 'Index': slot, 'Amount': amount,
                                                      'Photo': photo, 'Kind': 1, 'ItemType': 1}),
                      '(bag item %s)' % name)

    def save_items(self):
        chars = load_chars()
        if self.slot < len(chars):
            chars[self.slot]['items'] = {str(k): v for k, v in self.items.items()}
            chars[self.slot]['bags'] = {str(v): {str(k): it for k, it in s.items()} for v, s in self.bags.items()}
            chars[self.slot]['money'] = self.money
            save_chars(chars)

    def set_money(self, amount):
        self.money = amount
        self.mine['Money'] = amount
        self.send(object_props(1, {'Money': amount}), '(money)')

    def open_shop(self, stock):
        self.close_shop()
        for view, goods in stock.items():
            self.shop[view] = {}
            self.send(view_create(view, 18), '(shop shelf)')
            for i, (name, photo, cost) in enumerate(goods, 1):
                self.shop[view][i] = (name, photo, cost)
                self.send(view_add(view, i, {'Name': name, 'Index': i, 'Amount': 1, 'Photo': photo, 'Kind': 1,
                                             'ItemType': 1, 'Cost': cost}), '(shop item)')

    def close_shop(self):
        for view in self.shop:
            self.send(view_delete(view), '(close shop shelf)')
        self.shop = {}

    def command(self, words):
        if words[0] == 'Select' and len(words) == 4 and all(w.isdigit() for w in words[1:]):
            view, how, index = (int(w) for w in words[1:])
            if how == 2 and view in (VIEW_BAG_EQUIP, VIEW_EQUIPPED) and index in self.bags.get(view, {}):
                dst = VIEW_EQUIPPED if view == VIEW_BAG_EQUIP else VIEW_BAG_EQUIP
                free = next((s for s in range(1, (8 if dst == VIEW_EQUIPPED else BAG_SLOTS) + 1)
                             if s not in self.bags[dst]), None)
                if free is None:
                    return True
                item = self.bags[view].pop(index)
                self.bags[dst][free] = item
                self.send(view_remove(view, index), '(move gear)')
                self.send(view_add(dst, free, {'Name': item[0], 'Index': free, 'Amount': item[2], 'Photo': item[1],
                                               'Kind': 1, 'ItemType': 1}), '(move gear)')
                self.save_items()
                log('%s %s %s' % (self.tag, 'WEAR' if dst == VIEW_EQUIPPED else 'TAKE OFF', item[0]))
                return True
            if view == VIEW_BAG_ITEMS and how == 2 and index in (self.items or {}):
                item = self.items[index]
                heal = HEALING.get(item[0], 0)
                if heal and self.hp >= self.max_hp:
                    self.send(chat(1, 'You are already at full health.'), '(item)')
                    return True
                item[2] -= 1
                if heal:
                    self.set_hp(self.hp + heal)
                log('%s USE ITEM %s (slot %d), %d left' % (self.tag, item[0], index, item[2]))
                if item[2] > 0:
                    self.send(view_update(VIEW_BAG_ITEMS, index, {'Amount': item[2]}), '(item amount)')
                else:
                    self.items.pop(index)
                    self.send(view_remove(VIEW_BAG_ITEMS, index), '(item used up)')
                self.save_items()
                return True
            if how == 0 and view in self.shop and index in self.shop[view]:
                name, photo, cost = self.shop[view][index]
                if self.money < cost:
                    self.send(chat(1, 'Not enough money.'), '(shop)')
                    return True
                slot = next((s for s, it in self.items.items() if it[0] == name), None)
                if slot is None:
                    slot = next((s for s in range(1, BAG_SLOTS + 1) if s not in self.items), None)
                    if slot is None:
                        self.send(chat(1, 'Your bag is full.'), '(shop)')
                        return True
                    self.items[slot] = [name, photo, 1]
                    self.send(view_add(VIEW_BAG_ITEMS, slot, {'Name': name, 'Index': slot, 'Amount': 1, 'Photo': photo,
                                                              'Kind': 1, 'ItemType': 1}), '(bought item)')
                else:
                    self.items[slot][2] += 1
                    self.send(view_update(VIEW_BAG_ITEMS, slot, {'Amount': self.items[slot][2]}), '(bought item)')
                self.set_money(self.money - cost)
                self.save_items()
                log('%s BUY %s for %d' % (self.tag, name, cost))
                return True
            if how == 0 and view == VIEW_BAG_ITEMS and self.shop and index in (self.items or {}):
                item = self.items[index]
                price = max(1, PRICES.get(item[0], 10) // 2)
                item[2] -= 1
                if item[2] > 0:
                    self.send(view_update(VIEW_BAG_ITEMS, index, {'Amount': item[2]}), '(sold item)')
                else:
                    self.items.pop(index)
                    self.send(view_remove(VIEW_BAG_ITEMS, index), '(sold item)')
                self.set_money(self.money + price)
                self.save_items()
                log('%s SELL %s for %d' % (self.tag, item[0], price))
                return True
            if how == 0:
                return True
        if words[0] == 'Sell' and len(words) == 5 and all(w.isdigit() for w in words[1:]):
            shop_view, view, index, qty = (int(w) for w in words[1:])
            if view == VIEW_BAG_ITEMS and index in (self.items or {}) and qty > 0:
                item = self.items[index]
                qty = min(qty, item[2])
                price = max(1, PRICES.get(item[0], 10) // 2) * qty
                item[2] -= qty
                if item[2] > 0:
                    self.send(view_update(VIEW_BAG_ITEMS, index, {'Amount': item[2]}), '(sold item)')
                else:
                    self.items.pop(index)
                    self.send(view_remove(VIEW_BAG_ITEMS, index), '(sold item)')
                self.set_money(self.money + price)
                self.save_items()
                log('%s SELL %d x %s for %d' % (self.tag, qty, item[0], price))
            return True
        if words[0] == 'Buy' and len(words) == 4 and all(w.isdigit() for w in words[1:]):
            view, index, qty = (int(w) for w in words[1:])
            for _ in range(min(qty, 99)):
                before = self.money
                self.command(['Select', str(view), '0', str(index)])
                if self.money == before:
                    break
            return True
        if words[0] == 'DoAction' and len(words) == 2:
            code = words[1]
            if code in ACTION_CODES:
                x, z = self.pos.now()
                self.send(place_object(1, x, self.height, z, action=(0, 0, ACTION_CODES.index(code))), '(emote)')
            log('%s EMOTE %s' % (self.tag, code))
            return True
        if words[0] == 'SetTitle':
            title = ' '.join(words[1:])[:30]
            self.mine['Title'] = title
            self.send(object_props(1, {'Title': title}), '(title)')
            return True
        if words[0] in ('DepositMoney', 'EncashMoney') and len(words) == 2 and words[1].isdigit():
            amount = int(words[1])
            chars = load_chars()
            bank = chars[self.slot].get('bank', 0) if self.slot < len(chars) else 0
            if words[0] == 'DepositMoney' and amount <= self.money:
                bank += amount
                self.set_money(self.money - amount)
            elif words[0] == 'EncashMoney' and amount <= bank:
                bank -= amount
                self.set_money(self.money + amount)
            if self.slot < len(chars):
                chars = load_chars()
                chars[self.slot]['bank'] = bank
                chars[self.slot]['money'] = self.money
                save_chars(chars)
            self.send(object_props(1, {'BankMoney': bank}), '(bank balance)')
            return True
        if words[0] == 'RemoveItem' and len(words) == 3 and words[1].isdigit() and words[2].isdigit():
            view, index = int(words[1]), int(words[2])
            if view == VIEW_BAG_ITEMS and index in (self.items or {}):
                self.items.pop(index)
                self.send(view_remove(VIEW_BAG_ITEMS, index), '(item thrown away)')
                self.save_items()
            return True
        if words[0] == 'GetPlayerInfo':
            self.send(chat(1, '%s: level %d, %d gold' % (self.name, self.mine.get('Level', 1), self.money)), '(info)')
            return True
        return False

    def set_hp(self, hp):
        self.hp = max(0, min(self.max_hp, hp))
        self.mine['HP'] = self.hp
        self.send(object_props(1, {'HP': self.hp}), '(hp)')
        self.to_others(object_props(self.pid, {'HP': self.hp}), '(%s hp)' % self.name)

    def attack(self, target):
        with world_lock:
            mon = monster_state.setdefault(target, {'hp': MONSTERS[target]['hp'], 'dead': False})
            if mon['dead']:
                return
            dealt = random.randint(12, 22)
            mon['hp'] -= dealt
            died = mon['hp'] <= 0
            if died:
                mon['dead'] = True
        info = MONSTERS[target]
        x, z = self.pos.now()
        swing = place_object(1, x, self.height, z, action=(0, 0, ACT_ATTACK))
        self.send(swing, '(attack swing)')
        self.to_others(place_object(self.pid, x, self.height, z, action=(0, 0, ACT_ATTACK)), '(%s attacks)' % self.name)
        everyone = [self] + self.others()
        if died:
            for c in everyone:
                c.send(struct.pack('<HII', 8, target, 0), '(monster dies)')
            self.set_money(self.money + info['gold'])
            self.save_items()
            self.send(chat(1, 'You defeated the %s and found %d gold.' % (info['name'], info['gold'])), '(fight)')
            log('%s KILLED %s' % (self.tag, info['name']))
            threading.Timer(RESPAWN_SECONDS, respawn_monster, [target]).start()
            return
        for c in everyone:
            c.send(object_props(target, {'HP': mon['hp']}), '(monster hp)')
        taken = random.randint(*info['hit'])
        self.send(chat(target, '%s hits you for %d. (%d left on it)' % (info['name'], taken, mon['hp'])), '(fight)')
        if self.hp - taken <= 0:
            self.set_hp(self.max_hp)
            self.send(chat(1, 'You were knocked out and wake up where you started.'), '(fight)')
            self.pos = Mover(START_POS[0], START_POS[2])
            self.send(place_object(1, *START_POS), '(back to start)')
        else:
            self.set_hp(self.hp - taken)

    def sail_off(self):
        base = self.scene // 1000 * 1000
        target = base + (1 if self.scene % 1000 != 1 else 2)
        if not os.path.exists(os.path.join(RESOURCE, 'ini', 'scenes', '%d.ini' % target)):
            self.send(chat(1, 'There is nowhere to sail from here.'), '(sail)')
            return
        self.send(struct.pack('<H', 4), '(leave scene)')
        self.enter_scene(target)

    def slash(self, text):
        parts = text.split()
        if parts[0] in ('/go', '/ship') and len(parts) == 2 and parts[1].isdigit():
            scene = int(parts[1])
            self.force_land = parts[0] != '/ship'
            if not os.path.exists(os.path.join(RESOURCE, 'ini', 'scenes', '%d.ini' % scene)):
                self.send(chat(1, 'no such scene %d' % scene), '(slash reply)')
                return
            self.send(struct.pack('<H', 4), '(leave scene)')
            self.enter_scene(scene)
        elif parts[0] in ('/sail', '/land'):
            self.sail_off()
        elif parts[0] == '/where':
            x, z = self.pos.now()
            self.send(chat(1, 'scene %d at %.1f, %.1f' % (self.scene, x, z)), '(slash reply)')
        else:
            self.send(chat(1, 'commands: /go <scene id>, /where'), '(slash reply)')

    def on_message(self, body):
        mid = struct.unpack_from('<H', body)[0] if len(body) >= 2 else -1
        log('%s RECV id=0x%04x len=%d\n%s' % (self.tag, mid, len(body), hexdump(body)))
        _ctx.account = self.account
        if mid == 7 and self.port == LIST_PORT:
            self.send(server_list(), '(server list)')
        elif mid == 7:
            replies = scripted_replies() or [verify_picture()]
            for body in replies:
                self.send(body, '(login server reply to 7)')
        elif mid == 5:
            self.send(create_info(), '(character-creation tables - experimental)')
        elif mid == 0 and self.port == LOGIN_PORT:
            self.account = _ctx.account = body[2:0x12].hex()
            chars = load_chars()
            save_chars(chars)
            log('%s LOGIN account key %s, %d character(s)' % (self.tag, self.account, len(chars)))
            self.send(char_list(chars), '(login ok, character list)')
        elif mid == 2 and self.port == LOGIN_PORT:
            account = body[0x12:0x32].split(b'\0')[0].decode('latin1')
            log('%s ENTER WORLD account=%r slot=%d' % (self.tag, account, body[0x32]))
            chars = load_chars()
            ch = chars[body[0x32]] if body[0x32] < len(chars) else {'name': 'Player', 'face': FACES[0][0], 'body': BODIES[0][0]}
            if account.startswith('bot'):
                ch = {'name': account, 'face': FACES[1][0], 'body': BODIES[1][0]}
                body = body[:0x32] + bytes([99])
            mine = {
                'Name': ch['name'], 'Model': 3001001, 'LandModel': 3001001, 'ActionSet': 0, 'CollideRadius': 0.5, 'IsRun': 1,
                'Face': ch['face'], 'BodyType': ch['body'], 'Hair': 0, 'HairColor': 0, 'TexColor': 0,
                'Hat': 0, 'CapColor': 0, 'LeftHand': 0, 'RightHand': 0, 'ArmToxicLevel': 0, 'WeaponEffect': 0,
                'Money': ch.get('money', 1000), 'Load': 0.0, 'LoadMax': 100.0, 'VisualRange': 100.0,
                'HP': 100, 'MaxHP': 100, 'SP': 100, 'MaxSP': 100, 'Level': 1, 'Dead': 0,
                'PosX': START_POS[0], 'PosY': START_POS[1], 'PosZ': START_POS[2], 'Orient': 0.0,
            }
            self.slot = body[0x32]
            self.name = ch['name']
            self.mine = mine
            self.money = mine['Money']
            self.send(property_table(), '(property table)')
            self.send(player_props(mine), '(own properties)')
            pos = ch.get('pos')
            self.enter_scene(ch.get('scene', START_SCENE), (pos[0], START_POS[1], pos[1]) if pos else None)
        elif mid in (0x0b, 0x10) and self.pos:
            text = body[0x1e if mid == 0x10 else 2:].split(b'\0')[0].decode('latin1')
            word = text.split(' ', 1)[0]
            if word in COMMAND_NAMES:
                if not self.command(text.split()):
                    log('%s COMMAND %r (not implemented)' % (self.tag, text))
            elif text.startswith('/'):
                log('%s SLASH %r' % (self.tag, text))
                self.slash(text)
            elif text:
                log('%s CHAT %r' % (self.tag, text))
                self.send(chat(1, text), '(chat echo)')
                self.to_others(chat(self.pid, text), '(chat from %s)' % self.name)
        elif mid == 0x08 and self.pos:
            target = struct.unpack_from('<I', body, 0x16)[0]
            npc = next((n for n in WORLD_NPCS if n[0] == target), None)
            log('%s INTERACT object %d%s' % (self.tag, target, ' (%s)' % npc[3] if npc else ''))
            if target in MONSTERS and self.scene == START_SCENE:
                self.attack(target)
            elif npc:
                self.talking_to = target
                self.send(npc_talk(target, DIALOGUES.get(target, DEFAULT_DIALOGUE)), '(npc dialogue)')
        elif mid == 0x09 and self.pos and len(body) >= 12:
            target, _, option = struct.unpack_from('<IIH', body, 2)
            log('%s DIALOGUE object %d option %d' % (self.tag, target, option))
            if option == OPT_TRADE and target in SHOPS:
                self.open_shop(SHOPS[target])
                self.send(npc_talk(target, [(T_TEXT, 0, 'Click an item to buy it. Press Sell, then click one of yours.'),
                                            (T_OPTION, OPT_BYE, 'Goodbye')]), '(npc dialogue)')
            elif option == OPT_BANK:
                chars = load_chars()
                bank = chars[self.slot].get('bank', 0) if self.slot < len(chars) else 0
                self.send(npc_talk(target, [(T_TEXT, 0, 'You have %d gold in the bank and carry %d.' % (bank, self.money)),
                                            (T_OPTION, OPT_DEPOSIT, 'Deposit 100'),
                                            (T_OPTION, OPT_WITHDRAW, 'Withdraw 100'),
                                            (T_OPTION, OPT_BYE, 'Goodbye')]), '(npc dialogue)')
            elif option in (OPT_DEPOSIT, OPT_WITHDRAW):
                self.command(['DepositMoney' if option == OPT_DEPOSIT else 'EncashMoney', '100'])
                body2 = struct.pack('<HIIH', 9, target, 0, OPT_BANK)
                self.on_message(body2)
            elif option == OPT_SAIL:
                self.sail_off()
            else:
                self.close_shop()
                self.send(npc_talk(target, [(T_TEXT, 0, 'Fair winds, %s.' % self.name)]), '(npc dialogue)')
        elif mid == 0x12 and self.pos:
            x, z = struct.unpack_from('<2f', body, 2)
            cx, cz = self.pos.walk_to(x, z)
            log('%s MOVE (%.1f, %.1f) -> (%.1f, %.1f)' % (self.tag, cx, cz, x, z))
            self.send(place_object(1, cx, self.height, cz, dest=(x, z), speed=int(RUN_SPEED * 100),
                                   action=(0, 0, ACT_RUN)), '(move)')
            self.to_others(place_object(self.pid, cx, self.height, cz, dest=(x, z), speed=int(RUN_SPEED * 100),
                                        action=(0, 0, ACT_RUN)), '(%s moves)' % self.name)
        elif mid == 4 and self.port == LOGIN_PORT and not self.pos:
            name = body[2:0x22].split(b'\0')[0].decode('latin1')
            chars = [c for c in load_chars() if c['name'] != name]
            save_chars(chars)
            log('%s DELETE CHARACTER %r' % (self.tag, name))
            self.send(char_list(chars), '(character list after delete)')
        elif mid == 1 and self.port == LOGIN_PORT:
            log('%s LEFT CHARACTER SCREEN' % self.tag)
        elif mid == 3 and self.port == LOGIN_PORT:
            chars = load_chars()
            ch = parse_create(body)
            log('%s CREATE %r' % (self.tag, ch))
            if len(chars) < 2:
                chars.append(ch)
                save_chars(chars)
            self.send(char_list(chars), '(character list after create - experimental)')

    def inject_loop(self):
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'inject.py')
        while self.alive:
            time.sleep(0.5)
            if self.port != LOGIN_PORT or not os.path.exists(path):
                continue
            try:
                with open(path, encoding='utf-8') as f:
                    src = f.read()
                os.remove(path)
                for body in eval(src, dict(globals(), conn=self)):
                    self.send(body, '(injected)')
            except Exception as e:
                log('inject: %r' % e)

    def run(self):
        self.alive = True
        threading.Thread(target=self.inject_loop, daemon=True).start()
        log('%s CONNECTED' % self.tag)
        de = Deframer()
        try:
            self.send(hello(), '(handshake)')
            while True:
                data = self.sock.recv(65536)
                if not data:
                    break
                for body in de.feed(data):
                    self.on_message(body)
        except OSError as e:
            log('%s ERROR %s' % (self.tag, e))
        self.alive = False
        _ctx.account = self.account
        if self.pos:
            self.leave_scene()
        if self.pos:
            chars = load_chars()
            if self.slot < len(chars):
                chars[self.slot]['pos'] = [round(v, 2) for v in self.pos.now()]
                chars[self.slot]['scene'] = self.scene
                save_chars(chars)
        log('%s CLOSED' % self.tag)
        self.sock.close()


def serve(port):
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, getattr(socket, 'SO_EXCLUSIVEADDRUSE', socket.SO_REUSEADDR), 1)
    srv.bind(('127.0.0.1', port))
    srv.listen(8)
    log('listening on 127.0.0.1:%d' % port)
    while True:
        sock, addr = srv.accept()
        threading.Thread(target=Conn(sock, addr, port).run, daemon=True).start()


if __name__ == '__main__':
    ports = [int(a) for a in sys.argv[1:]] or [LIST_PORT, LOGIN_PORT]
    for p in ports[1:]:
        threading.Thread(target=serve, args=(p,), daemon=True).start()
    serve(ports[0])
