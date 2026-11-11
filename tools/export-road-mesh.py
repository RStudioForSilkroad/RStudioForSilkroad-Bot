import os
import gzip
import json
import math
import mmap
import struct
from pathlib import Path

root = Path('tools/nav-research')
phbot = Path(os.environ.get('PHBOT_DIR', 'C:/Program Files/phBot Stable'))  # phBot klasörünün yolu; PHBOT_DIR ortam değişkeniyle ayarla
all_regions = json.loads((root / 'regions.json').read_text(encoding='utf-8'))
regions = {int(k): v for k, v in all_regions.items()
           if 60 <= int(k) % 256 <= 180 and 69 <= int(k) // 256 <= 113}
del all_regions
starts = {}
count = 0
for region, mesh in regions.items():
    starts[region] = count
    count += len(mesh['cells'])

with open('D:/Games/New Era Online/Data.ne', 'rb') as source:
    data = mmap.mmap(source.fileno(), 0, access=mmap.ACCESS_READ)
    for region, mesh in regions.items():
        offset = mesh['tileOffset']
        mesh['blocked'] = {i for i, flags, _ in struct.iter_unpack(
            '<IHH', data[offset:offset + 73728]) if flags & 1}
        mesh['heights'] = struct.unpack_from('<9409f', data, mesh['heightOffset'])
    data.close()

town_points = []
for town in ('Hotan', 'Donwhang', 'Jangan', 'Samarkand', 'Constantinople'):
    for suffix in ('', '_r'):
        for line in (phbot / 'Town' / (town + suffix + '.txt')).read_text(encoding='utf-8-sig').splitlines():
            parts = line.split(',')
            if parts[0] != 'walk':
                continue
            if len(parts) == 4:
                x, y = map(float, parts[1:3])
            elif len(parts) == 6:
                rx, ry, lx, ly = map(float, parts[1:5])
                x, y = (rx - 135) * 192 + lx / 10, (ry - 92) * 192 + ly / 10
            else:
                continue
            town_points.append((x, y))

town_cells = set()
for x, y in town_points:
    sx, sy = math.floor(x / 192), math.floor(y / 192)
    region = sx + 135 + (sy + 92) * 256
    if region not in regions:
        continue
    mesh = regions[region]
    lx, ly = (x - sx * 192) * 10, (y - sy * 192) * 10
    for i, (left, top, right, bottom, _) in enumerate(mesh['cells']):
        if i not in mesh['blocked'] and left <= lx <= right and top <= ly <= bottom:
            town_cells.add(starts[region] + i)
            break

def traversable(region, cell):
    return region in regions and 0 <= cell < len(regions[region]['cells']) and cell not in regions[region]['blocked']

edge_count = 0
with gzip.open('data/road-mesh.bin.gz', 'wb', compresslevel=5) as target:
    target.write(struct.pack('<4sII', b'RD01', len(regions), count))
    for region, mesh in regions.items():
        links = [set() for _ in mesh['cells']]
        for e in mesh['internal']:
            a, b = e[-2:]
            if traversable(region, a) and traversable(region, b):
                if not e[4] & 2:
                    links[a].add(starts[region] + b)
                if not e[4] & 1:
                    links[b].add(starts[region] + a)
        for e in mesh['global']:
            a, b, src, dst = e[-4:]
            if src == region and not e[4] & 2 and traversable(src, a) and traversable(dst, b):
                links[a].add(starts[dst] + b)
        target.write(struct.pack('<HI', region, len(links)))
        for i, (x0, y0, x1, y1, _) in enumerate(mesh['cells']):
            ix = max(0, min(96, round((x0 + x1) / 40)))
            iy = max(0, min(96, round((y0 + y1) / 40)))
            height = mesh['heights'][iy * 97 + ix]
            if not math.isfinite(height):
                height = 0
            flags = (1 if i in mesh['blocked'] else 0) | (2 if starts[region] + i in town_cells else 0)
            target.write(struct.pack('<fffffBH', x0, y0, x1, y1, height, flags, len(links[i])))
            for destination in sorted(links[i]):
                target.write(struct.pack('<I', destination))
            edge_count += len(links[i])
print(f'{len(regions)} regions, {count} cells, {edge_count} directed edges, '
      f'{len(town_points)} phBot walk points, {len(town_cells)} matched cells')
