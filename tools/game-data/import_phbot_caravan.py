#!/usr/bin/env python3
"""Builds data/caravan-scripts.json from phBot caravan scripts (walk / teleport / wait lines), for the routes the bot has.

    python tools/game-data/import_phbot_caravan.py <script folder> <tables folder> [out.json]

Only `walk`, `wait` and `teleport` lines are kept. Everything else a script may hold (`chat`, `stop`, `mount`, `oldtrade`, `use` ...) is dropped: the bot
never sends chat messages or trade commands from a file. Each `teleport,<NPC name>,<NPC name>` must be a ferry / tunnel the game's own text table names
(SN_NPC_x -> GATE_NPC_x in transport-network.json) and a directed link of the transport network, otherwise the script is refused.
The result lists the NPC names it needs under "Names" so the program can map them without the game's text tables.
"""
import json
import math
import os
import sys

# (from, to, file) - the newest script of each route the bot has; the older duplicates and the other routes of the folder are not used.
USED = [
    ('Hotan', 'Jangan', '8a_Hotan_to_Jangan_PHBOT.txt'),
    ('Jangan', 'Hotan', '8b_Jangan_to_Hotan_PHBOT.txt'),
    ('Hotan', 'Donwhang', '1b_Hotan_to_Downhang_PHBOT.txt'),
    ('Donwhang', 'Hotan', '1a_Downhang_to_Hotan_PHBOT.txt'),
    ('Jangan', 'Donwhang', '2b_Jangan_to_Downhang_PHBOT.txt'),
    ('Donwhang', 'Jangan', '2a_Downhang_to_Jangan_PHBOT.txt'),
    ('Constantinople', 'Samarkand', 'Cons_to_Samar.txt'),
]
KEEP = ('walk', 'wait', 'teleport')


def read_text(path):
    raw = open(path, 'rb').read()
    if raw[:2] in (b'\xff\xfe', b'\xfe\xff'):
        return raw.decode('utf-16')
    return raw.decode('utf-8-sig')


def gate_names(tables):
    text = read_text(os.path.join(tables, 'textdata_object.txt')).replace('\r\n', '\n').split('\n')
    names = {}
    for line in text:
        cols = line.split('\t')
        if len(cols) > 8 and cols[1].startswith('SN_NPC_'):
            names[cols[8].strip()] = 'GATE_NPC_' + cols[1][len('SN_NPC_'):]
    return names


def main():
    folder, tables = sys.argv[1], sys.argv[2]
    out = sys.argv[3] if len(sys.argv) > 3 else 'data/caravan-scripts.json'
    network = json.load(open('data/transport-network.json', encoding='utf-8-sig'))
    gate_by_code = {g['Code']: g for g in network['Gates']}
    links = {(l['From'], l['To']) for l in network['Links']}
    by_name = gate_names(tables)
    needed = {}
    scripts = []
    for start, goal, file in USED:
        steps = []
        last = None
        for line in read_text(os.path.join(folder, file)).replace('\r\n', '\n').split('\n'):
            line = line.strip()
            if not line:
                continue
            parts = line.split(',')
            kind = parts[0].lower()
            if kind not in KEEP:
                continue
            if kind == 'walk':
                x, y, z = float(parts[1]), float(parts[2]), float(parts[3])
                if not (-25920 <= x < 23232 and -17664 <= y < 6912):
                    raise SystemExit('%s: walk point out of the world: %s' % (file, line))
                steps.append('walk,%g,%g,%g' % (x, y, z))
                last = (x, y)
            elif kind == 'wait':
                steps.append('wait,%d' % int(float(parts[1])))
            else:
                a, b = parts[1], parts[2]
                ca, cb = by_name.get(a), by_name.get(b)
                if not ca or not cb or ca not in gate_by_code or cb not in gate_by_code:
                    raise SystemExit('%s: teleport NPC not in the game tables: %s' % (file, line))
                if (gate_by_code[ca]['Id'], gate_by_code[cb]['Id']) not in links:
                    raise SystemExit('%s: no link %s -> %s' % (file, ca, cb))
                needed[a], needed[b] = ca, cb
                steps.append('teleport,%s,%s' % (a, b))
        # waits before / after a teleport are kept; a leading or trailing wait carries no meaning
        while steps and steps[0].startswith('wait'):
            steps.pop(0)
        while steps and steps[-1].startswith('wait'):
            steps.pop()
        scripts.append({'From': start, 'To': goal, 'Source': file, 'Steps': steps})
        walks = [s for s in steps if s.startswith('walk')]
        print('%-15s -> %-15s %-36s %3d walks, %d teleports' % (start, goal, file, len(walks), sum(s.startswith('teleport') for s in steps)))
    data = {'Names': dict(sorted(needed.items())), 'Scripts': scripts}
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
        f.write('\n')
    print('written', out, os.path.getsize(out), 'bytes;', len(needed), 'NPC names')


if __name__ == '__main__':
    main()
