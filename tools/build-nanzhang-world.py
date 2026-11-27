#!/usr/bin/env python
# Builds data/nanzhang-world.json (every NPC on the Nanzhang map and the quests of those NPCs with their rewards) from the game's own
# text tables (extracted from Media.pk2 with the Pk2 tools: server_dep/silkroad/textdata/*.txt).
# usage: python tools/build-nanzhang-world.py <textdata folder> <output json>
import collections, html, io, json, re, sys

src, out = sys.argv[1].rstrip('/\\'), sys.argv[2]


def rows(name):
    with io.open(src + '/' + name, 'r', encoding='utf-16') as f:
        for line in f:
            line = line.rstrip('\r\n')
            if line.startswith('﻿'):
                line = line[1:]
            if line:
                yield [c.strip() for c in line.split('\t')]


# Text keys -> English text (the third column of the text tables).
texts = {}
for name in ('textdata_object.txt', 'textquest_otherstring.txt', 'textquest_queststring.txt', 'textquest_speech&name.txt', 'textzonename.txt'):
    for r in rows(name):
        if len(r) >= 3 and r[1].startswith('SN_') and r[2] and r[2] != 'xxx':
            texts.setdefault(r[1], r[2])

# NPC objects: id -> (code, name key).
chars = {}
for i in ('', '_5000', '_10000', '_15000', '_20000', '_25000', '_30000', '_35000', '_40000', '_45000'):
    try:
        for r in rows('characterdata%s.txt' % i):
            if len(r) > 5 and r[2].startswith('NPC_'):
                chars[int(r[1])] = (r[2], r[5])
    except IOError:
        pass


def world(region, lx, lz):
    rx, ry = region & 255, region >> 8
    return (rx - 135) * 192 + lx / 10.0, (ry - 92) * 192 + lz / 10.0


# The Nanzhang map (the game's world map of the Nanzhang land): its zones by name, which give the regions. The regions of the Tiger Rim climb
# (Ropemaster camp, Warden top, Hermit foot, Ledge) lie just north of the Blightwood zone.
ZONES = {'Old Mining Camp', 'Hollow Hive Ridge', 'Kuhe Village', 'Chiyan Volcano', 'Southern-Tarim Ferry', 'Forest of Whispers', 'Dusk Forest',
         'Ashen Fields', 'Mossgate', 'Southern Valley', 'Old Nanzhang Road', 'Nanzhang Outskirts', 'Silent Well', 'Blightwood', 'Dugu Manor',
         'Gloom Swamps', 'Mirk Woods', 'Nanzhang', 'Venom Fen', 'Lone Hearth', 'Eastern Crossing', 'Hollow of the Crucified'}
zone_of = {}
for r in rows('textzonename.txt'):
    if len(r) >= 3 and r[1].isdigit():
        zone_of[int(r[1])] = r[2]
regions = sorted(g for g, z in zone_of.items() if z in ZONES) + [22699, 22693, 22443, 22437]
region_set = set(regions)

npcs, seen = [], set()
for r in rows('npcpos.txt'):
    nid, region = int(r[0]), int(r[1])
    if nid not in chars or region not in region_set:
        continue
    x, y = world(region, float(r[2]), float(r[4]))
    code, key = chars[nid]
    name = texts.get(key, code)
    sig = (nid, round(x), round(y))
    if sig in seen:
        continue
    seen.add(sig)
    npcs.append({'id': nid, 'code': code, 'name': name, 'x': round(x, 1), 'y': round(y, 1), 'region': region, 'height': float(r[3])})

# Nanzhang NPCs the tables name but give no position (the server places them): listed without a position until the bot sees them.
survey = set()
try:
    with io.open('data/Nanzhang/Nanzhang-Harita-Verisi.json', encoding='utf-8-sig') as f:
        survey = {n['id'] for n in json.load(f)['NPCs']}
except IOError:
    pass
placed = {n['id'] for n in npcs}
pattern = re.compile(r'^NPC_(CH_NZ|CH_NZT|NE_JOB_OMC|CH_HHR)_|^NPC_CH_[A-Z_]*_NZ$')
for nid, (code, key) in sorted(chars.items()):
    if pattern.search(code) and nid not in placed and nid not in survey and 'OFFSTALL' not in code:
        npcs.append({'id': nid, 'code': code, 'name': texts.get(key, code), 'x': 0, 'y': 0, 'region': 0, 'height': 0})

# Quests: questdata row = service, id, code, level, ?, name key, ..., contents key (PAYCON), NPC name key (NN), type key (NC).
names_in_map = collections.defaultdict(list)
for n in npcs:
    names_in_map[n['name']].append(n)

# Conversation lines by quest code: SN_TALK_<code>_<number>.
talk = collections.defaultdict(list)
for key, value in texts.items():
    m = re.match(r'^SN_TALK_(.+)_(\d+)$', key)
    if m:
        talk[m.group(1)].append((int(m.group(2)), value))

quests = []
for r in rows('questdata.txt'):
    if len(r) < 11:
        continue
    code = r[2]
    giver = texts.get(r[9], '')
    title = texts.get(r[5], '')
    body = texts.get(r[8], '')
    nz = '_NZ' in code or '_HHR_' in code or '_OMC_' in code
    if giver not in names_in_map and not nz:
        continue
    if not body:
        continue
    plain = re.sub(r'<br\s*/?>', '\n', body)
    plain = html.unescape(re.sub(r'<[^>]+>', '', plain))
    rewards = ''
    m = re.search(r'Reward\s*\n(.*?)(?:\n\s*\n|\nForm|$)', plain, re.S)
    if m:
        rewards = ' / '.join(l.strip() for l in m.group(1).split('\n') if l.strip())
    mission = ''
    m = re.search(r'Mission\s*\n(.*?)(?:\n\s*\n|\nReward|$)', plain, re.S)
    if m:
        mission = '; '.join(l.strip() for l in m.group(1).split('\n') if l.strip())
    where = names_in_map.get(giver) or []
    # The conversation of the quest: SN_TALK_<code>_01, _02, ... in order.
    dialog = [t.replace('\\n', ' ') for _, t in sorted(talk.get(code, []))]
    quests.append({'code': code, 'level': int(r[3]) if r[3].isdigit() else 0, 'title': title or giver, 'npc': giver,
                   'npcId': where[0]['id'] if where else 0, 'mission': mission, 'rewards': rewards, 'dialog': dialog})

quests.sort(key=lambda q: (q['npc'], q['level'], q['code']))
with io.open(out, 'w', encoding='utf-8') as f:
    json.dump({'regions': regions, 'npcs': sorted(npcs, key=lambda n: (n['name'], n['id'])), 'quests': quests}, f, ensure_ascii=False, indent=1)
print('NPC', len(npcs), 'Quest', len(quests))
