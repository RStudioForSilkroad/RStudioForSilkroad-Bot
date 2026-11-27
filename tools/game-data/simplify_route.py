#!/usr/bin/env python3
"""Turns a recorded walk (build/route-*.csv from the route recorder: t_ms,region,x,y,height,selected,hp) into a list of way points for a caravan road.

    python tools/game-data/simplify_route.py <recording.csv> <out.json> [tolerance] [maxStep]

The points keep the walked shape: a point is dropped only when the walked line stays within `tolerance` units of the straight line (default 2.5)
and no leg is longer than `maxStep` units (default 25), so stairs and corners are not cut. Height is kept (the roads climb and the walker needs it).
"""
import csv
import json
import math
import sys


def distance_to_segment(p, a, b):
    ax, ay, bx, by = a[0], a[1], b[0], b[1]
    dx, dy = bx - ax, by - ay
    length2 = dx * dx + dy * dy
    if length2 == 0:
        return math.hypot(p[0] - ax, p[1] - ay)
    t = max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - ay) * dy) / length2))
    return math.hypot(p[0] - (ax + t * dx), p[1] - (ay + t * dy))


def simplify(points, tolerance, max_step):
    keep = [False] * len(points)
    keep[0] = keep[-1] = True
    stack = [(0, len(points) - 1)]
    while stack:
        first, last = stack.pop()
        if last <= first + 1:
            continue
        far, index = 0.0, -1
        for i in range(first + 1, last):
            d = distance_to_segment(points[i], points[first], points[last])
            if d > far:
                far, index = d, i
        long_leg = math.hypot(points[last][0] - points[first][0], points[last][1] - points[first][1]) > max_step
        if far > tolerance or long_leg:
            if index < 0:
                index = (first + last) // 2
            keep[index] = True
            stack.append((first, index))
            stack.append((index, last))
    return [p for p, k in zip(points, keep) if k]


def main():
    source, target = sys.argv[1], sys.argv[2]
    tolerance = float(sys.argv[3]) if len(sys.argv) > 3 else 2.5
    max_step = float(sys.argv[4]) if len(sys.argv) > 4 else 25.0
    rows = list(csv.DictReader(open(source, encoding='utf-8')))
    points = [(float(r['x']), float(r['y']), float(r['height'])) for r in rows]
    # drop repeated points
    unique = [points[0]]
    for p in points[1:]:
        if math.hypot(p[0] - unique[-1][0], p[1] - unique[-1][1]) >= 0.5:
            unique.append(p)
    result = simplify(unique, tolerance, max_step)
    out = [[round(x, 1), round(y, 1), round(h, 1)] for x, y, h in result]
    with open(target, 'w', encoding='utf-8') as f:
        f.write(json.dumps(out, separators=(',', ':')))
    walked = sum(math.hypot(a[0] - b[0], a[1] - b[1]) for a, b in zip(unique, unique[1:]))
    print('%d points recorded -> %d way points, walked length %d' % (len(points), len(out), round(walked)))


main()
