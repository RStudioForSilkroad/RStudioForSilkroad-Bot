"""Export phBot's generated scripts without issuing any game commands."""
import json
import os
import time

import QtBind
from phBot import generate_script, get_position, log


name = 'Rikimaru Grim Route Export'
gui = QtBind.init(__name__, name)
QtBind.createLabel(gui, 'Exports routes from your current position. Stay in the same spot during export.', 12, 12)
QtBind.createButton(gui, 'start_export', 'Export all 18 Grim routes', 12, 40)
QtBind.createButton(gui, 'cancel_export', 'Cancel export', 205, 40)
selected_code = QtBind.createLineEdit(gui, 'D17', 12, 75, 60, 25)
QtBind.createButton(gui, 'export_selected', 'Export one Grim route', 82, 75)

destinations = [
    ('D01', 27006, -1649.8, 2632.9), ('D02', 26496, -1285.0, 2221.0),
    ('D03', 25731, -643.6, 1647.0), ('D04', 25984, -1253.0, 1847.2),
    ('D05', 23167, -1460.0, -292.0), ('D06', 23920, -4407.0, 196.0),
    ('D07', 27237, -6435.0, 2810.0), ('D08', 24931, -6788.0, 1113.0),
    ('D09', 25184, -7471.2, 1340.5), ('D10', 24390, -12389.0, 753.0),
    ('D11', 27462, -12330.0, 3065.5), ('D12', 24199, 72.0, 458.0),
    ('D13', 22919, 83.7, -542.4), ('D14', 26250, 755.6, 1967.6),
    ('D15', 22916, -528.3, -396.3), ('D16', 23444, 2594.2, -49.0),
    ('D17', 26029, 7484.0, 1754.0), ('D18', 23204, 5613.9, -309.2),
]
directory = os.path.join(os.environ['LOCALAPPDATA'], 'Rikimaru', 'MemorySelectV3')
output = os.path.join(directory, 'phbot-routes.json')
source = None
pending = []
results = []
previous_results = []
next_query = 0
retry_count = 0


def export_selected():
    code = QtBind.text(gui, selected_code).strip().upper()
    if code.isdigit():
        code = 'D' + code.zfill(2)
    selected = [destination for destination in destinations if destination[0] == code]
    if not selected:
        log(name + ': enter a Grim code from D01 to D18.')
        return
    begin_export(selected)


def start_export():
    begin_export(destinations)


def begin_export(targets):
    global source, pending, results, previous_results, next_query, retry_count
    position = get_position()
    if not position or 'x' not in position or 'y' not in position:
        log(name + ': log in to the game before exporting.')
        return
    source = {'X': float(position['x']), 'Y': float(position['y'])}
    pending = list(targets)
    results = []
    previous_results = []
    try:
        with open(output, encoding='utf-8') as stream:
            previous_results = json.load(stream).get('Routes', [])
    except (OSError, ValueError, AttributeError):
        pass
    next_query = 0
    retry_count = 0
    log(name + ': exporting ' + str(len(pending)) + ' route(s) from X/Y ' + str(source['X']) + ' / ' + str(source['Y']) + '. Keep the character in place.')


def cancel_export():
    pending.clear()
    log(name + ': export cancelled.')


def save_results():
    os.makedirs(directory, exist_ok=True)
    merged = list(results)
    for old in previous_results:
        if any(new['Code'] == old.get('Code') and abs(new['StartX'] - old.get('StartX', 1e9)) <= 3
               and abs(new['StartY'] - old.get('StartY', 1e9)) <= 3 and new['Steps'] for new in results):
            continue
        merged.append(old)
    temporary = output + '.tmp'
    with open(temporary, 'w', encoding='utf-8') as stream:
        json.dump({'Routes': merged[:108]}, stream, ensure_ascii=False, indent=2)
    os.replace(temporary, output)


def event_loop():
    global next_query, retry_count
    now = time.monotonic()
    if not pending or now < next_query:
        return
    next_query = now + 5.6
    position = get_position()
    if not position or abs(position['x'] - source['X']) > 3 or abs(position['y'] - source['Y']) > 3:
        pending.clear()
        log(name + ': position changed. Start export again from the new position.')
        return
    code, region, x, y = pending[0]
    try:
        script = generate_script(region, x, y, 0)
    except Exception as error:
        pending.clear()
        log(name + ': route API error: ' + str(error))
        return
    if script is False and retry_count < 2:
        retry_count += 1
        return
    retry_count = 0
    pending.pop(0)
    steps = script if isinstance(script, list) else []
    results.append({'Code': code, 'StartX': source['X'], 'StartY': source['Y'],
                    'GoalX': x, 'GoalY': y, 'Steps': steps})
    try:
        save_results()
    except OSError as error:
        pending.clear()
        log(name + ': export file error: ' + str(error))
        return
    log(name + ': ' + code + (' exported' if steps else ' no route'))
    if not pending:
        log(name + ': finished. ' + output)
