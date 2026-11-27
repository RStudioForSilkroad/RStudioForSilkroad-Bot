#!/usr/bin/env python3
"""Rebuilds the bot's data files that come from the game's own tables, or checks that they still match.

The game keeps these tables in its Media archive (server_dep/silkroad/textdata/*.txt, UTF-16). Extract the ones listed in TABLES with your own
extractor into one folder, then:

    python tools/game-data/build_data.py --tables <folder> --check      # report differences, change nothing (exit code 1 when there are any)
    python tools/game-data/build_data.py --tables <folder> --write      # rewrite data/*.json from the tables

Why this exists: the files used to be made once by hand-run scripts that were not kept. When the game changed (a new Jangan -> Donwhang
teleport link, gate NPCs standing tens of units from the arrival point) the bot kept the old picture and each mistake was found by trying
it in the game. Run --check after every game update; it names what changed.

The rules of each file are in docs/data-model.md.
"""
import argparse
import json
import os
import sys

TABLES = ['teleportdata.txt', 'teleportlink.txt', 'teleportbuilding.txt', 'refoptionalteleport.txt', 'textdata_object.txt', 'skilldata_5000.txt',
          'itemdata_5000.txt']


def load_table(folder, name):
    """The rows of one table as lists of columns; comment lines (first column not a digit) are dropped."""
    raw = open(os.path.join(folder, name), 'rb').read()
    if raw[:2] == b'\xff\xfe':
        raw = raw[2:]
    text = raw.decode('utf-16-le', errors='replace').replace('\r', '')
    return [line.split('\t') for line in text.split('\n') if line.strip() and line[0].isdigit()]


def load_tables(folder, prefix):
    """The rows of every table file that starts with `prefix` and ends with a number or nothing (skilldata.txt, skilldata_5000.txt ...); the
    encrypted copies (…enc.txt) and the virtual table are left out."""
    rows = []
    for name in sorted(os.listdir(folder)):
        if name.startswith(prefix) and name.endswith('.txt') and 'enc' not in name and 'virtual' not in name:
            rows += load_table(folder, name)
    return rows


def world_xy(region, local_x, local_y):
    """World X/Y from a region number (low byte = x sector, high byte = y sector) and the position inside the region (tenths of a unit)."""
    # instance regions are negative (for example -32767); their y sector is the signed high byte, which is how the game's own coordinates run
    sx, sy = region & 0xff, region >> 8
    return round((sx - 135) * 192 + local_x / 10.0, 1), round((sy - 92) * 192 + local_y / 10.0, 1)


def build_transport(folder):
    """Gates (teleportdata) and the links between them (teleportlink). Only links in service count: the first column is 1. The others are
    switched-off entries and the one-gold links of the GM tools; a traveller cannot use them."""
    gates = []
    for r in load_table(folder, 'teleportdata.txt'):
        region = int(r[5])
        x, y = world_xy(region, int(r[6]), int(r[8]))
        gates.append({'Id': int(r[1]), 'Code': r[2].strip(), 'Model': int(r[3]), 'Region': region, 'X': x, 'Y': y,
                      'Height': float(r[7]), 'World': int(r[12])})
    links = []
    for r in load_table(folder, 'teleportlink.txt'):
        if r[0] != '1':
            continue
        links.append({'From': int(r[1]), 'To': int(r[2]), 'Fee': int(r[3]), 'Level': int(r[8]) if r[7] == '1' else 0,
                      'Restrictions': [int(v) for v in r[4:22]]})
    return {'Gates': gates, 'Links': links}


def build_gate_npcs(folder):
    """Where each gate's NPC stands (teleportbuilding): usually tens of units from the point where a traveller arrives (teleportdata)."""
    out = []
    for r in load_table(folder, 'teleportbuilding.txt'):
        if len(r) < 46:
            continue
        try:
            region, lx, h, ly = int(r[41]), float(r[43]), float(r[44]), float(r[45])
        except ValueError:
            continue
        x, y = world_xy(region, lx, ly)
        out.append({'Model': int(r[1]), 'Region': region, 'X': x, 'Y': y, 'Height': h})
    return out


def zone_names(folder):
    names = {}
    for r in load_table(folder, 'textdata_object.txt'):
        if len(r) > 12 and r[1].startswith('SN_ZONE'):
            names[r[1]] = (r[2], r[8], r[12])
    return names


def build_reverse_points(folder):
    """The places the Reverse Return Scroll can take you to (refoptionalteleport), with their names from the object text table."""
    names = zone_names(folder)
    out = []
    for r in load_table(folder, 'refoptionalteleport.txt'):
        if len(r) < 9:
            continue
        korean, english, turkish = names.get(r[3], (r[2], r[2], r[2]))
        region, lx, height, ly = int(r[4]), float(r[5]), float(r[6]), float(r[7])
        x, y = world_xy(region, lx, ly)
        out.append({'Id': int(r[1]), 'Name': english, 'Turkish': turkish, 'Korean': korean, 'Region': region, 'LocalX': lx,
                    'Height': height, 'LocalY': ly, 'X': x, 'Y': y})
    return out


def build_skill_weapons(folder):
    """The weapon type a skill can only be used with (skilldata column 50; 255 = any weapon, left out), grouped by weapon type:
    {"11": [skill ids]}. Types: 7-9 melee, 10 warlock rod, 11 wizard staff, 12 crossbow, 13 dagger, 14 harp, 15 cleric staff (the Cleric's
    buffs and heals need it; the Wizard's spells need the type 11 staff, which is why a Wizard / Cleric must change weapons)."""
    groups = {}
    for r in load_tables(folder, 'skilldata'):
        if len(r) > 50 and r[50].isdigit() and int(r[50]) != 255:
            groups.setdefault(r[50], []).append(int(r[1]))
    return {k: sorted(set(v)) for k, v in sorted(groups.items(), key=lambda kv: int(kv[0]))}


def build_skill_timing(folder):
    """How a skill is used (skilldata): {"id": [prepare ms, cast ms, action ms, reuse ms, needs a target 0/1]} for every active skill (activity 1 or
    2: buffs, imbues, attacks; passives are left out). The bot paces its skill commands with it (no command while the last cast is not over or
    the skill's own reuse time has not passed) and sends no target with a skill that takes none (a self buff or an imbue)."""
    out = {}
    for r in load_tables(folder, 'skilldata'):
        if len(r) > 22 and r[8] in ('1', '2') and r[1].isdigit():
            out[r[1]] = [int(r[11]), int(r[12]), int(r[13]), int(r[14]), int(r[22])]
    return dict(sorted(out.items(), key=lambda kv: int(kv[0])))


def build_weapon_types(folder):
    """The weapon type of every weapon item (itemdata: type ids 3/1/6, the weapon type in column 12), grouped by weapon type like the skills."""
    groups = {}
    for r in load_tables(folder, 'itemdata'):
        if len(r) > 12 and r[9] == '3' and r[10] == '1' and r[11] == '6':
            groups.setdefault(r[12], []).append(int(r[1]))
    return {k: sorted(set(v)) for k, v in sorted(groups.items(), key=lambda kv: int(kv[0]))}


BUILDERS = {
    'transport-network.json': build_transport,
    'gate-npcs.json': build_gate_npcs,
    'reverse-points.json': build_reverse_points,
    'skill-weapons.json': build_skill_weapons,
    'weapon-types.json': build_weapon_types,
    'skill-timing.json': build_skill_timing,
}


def same(a, b):
    """Equal data; numbers within 0.05 (older files carry float noise such as -50.80000000000001)."""
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(same(a[k], b[k]) for k in a)
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(same(x, y) for x, y in zip(a, b))
    if isinstance(a, float) or isinstance(b, float):
        return isinstance(a, (int, float)) and isinstance(b, (int, float)) and abs(a - b) < 0.05
    return a == b


def describe_difference(name, shipped, built):
    """A few lines naming what is different between the shipped data and the one the tables give."""
    lines = []
    if isinstance(built, dict) and name.endswith('skill-timing.json'):
        have, want = shipped or {}, built
        return ['  skill-timing.json: %d new, %d gone, %d differ' % (len(set(want) - set(have)), len(set(have) - set(want)),
                sum(1 for k in set(want) & set(have) if want[k] != have[k]))]
    if isinstance(built, dict) and name.endswith('weapons.json') or name.endswith('weapon-types.json'):
        lines = []
        for part in sorted(set(built) | set(shipped or {})):
            now, then = set(built.get(part, [])), set((shipped or {}).get(part, []))
            if now != then:
                lines.append('  %s[%s]: %d new, %d gone' % (name, part, len(now - then), len(then - now)))
        return lines
    if isinstance(built, dict):
        for part in built:
            lines += describe_difference(name + ':' + part, shipped.get(part), built[part])
        return lines
    key = (lambda e: (e['From'], e['To'])) if name.endswith('Links') else (lambda e: e.get('Id', e.get('Model')))
    have = {key(e): e for e in (shipped or [])}
    want = {key(e): e for e in built}
    missing = sorted(set(want) - set(have))
    extra = sorted(set(have) - set(want))
    changed = sorted(k for k in set(want) & set(have) if not same(want[k], have[k]))
    if missing:
        lines.append('  %s: %d in the game but not in the bot: %s' % (name, len(missing), missing[:12]))
    if extra:
        lines.append('  %s: %d in the bot but not in the game: %s' % (name, len(extra), extra[:12]))
    if changed:
        lines.append('  %s: %d differ: %s' % (name, len(changed), changed[:12]))
    return lines


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.dirname(os.path.dirname(here))
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--tables', default=os.environ.get('NEWERA_TABLES', os.path.join(root, 'build', 'game-tables', 'tables')))
    ap.add_argument('--data', default=os.path.join(root, 'data'))
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument('--check', action='store_true', help='compare only (the default)')
    mode.add_argument('--write', action='store_true', help='rewrite the data files')
    args = ap.parse_args()
    missing = [t for t in TABLES if not os.path.exists(os.path.join(args.tables, t))]
    if missing:
        print('Tables missing in %s: %s' % (args.tables, ', '.join(missing)))
        return 2
    differences = 0
    for name, build in BUILDERS.items():
        built = build(args.tables)
        path = os.path.join(args.data, name)
        shipped = json.load(open(path, encoding='utf-8-sig')) if os.path.exists(path) else None
        if same(shipped, built):
            print('%-24s matches the game tables' % name)
            continue
        differences += 1
        print('%-24s DIFFERS' % name)
        for line in describe_difference(name, shipped, built):
            print(line)
        if args.write:
            with open(path, 'w', encoding='utf-8') as f:
                f.write(json.dumps(built, indent=1, ensure_ascii=False))
            print('%-24s rewritten' % name)
    return 1 if differences and not args.write else 0


if __name__ == '__main__':
    sys.exit(main())
