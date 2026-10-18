"""Analyse fresh-process, one-setting ablations with bracketing reference runs.

Input is the private feature-audit runs.json and sibling <Id>-frames.csv and logs.
A positive changed/reference percentage means the changed setting was slower.
Controls are interpolated in log space by receipt time. These are conditional,
stationary effects; correlated settings must not be added together as total savings.
"""
import argparse
from datetime import datetime
import hashlib
import json
import math
from pathlib import Path
import re
import statistics

from CompareFrameTraces import frame_stats


def control_estimate(controls, timestamp, metric):
    before = [row for row in controls if row['timestamp'] <= timestamp]
    after = [row for row in controls if row['timestamp'] >= timestamp]
    if not before or not after:
        return None
    left, right = max(before, key=lambda r: r['timestamp']), min(after, key=lambda r: r['timestamp'])
    if left['timestamp'] == right['timestamp']:
        return left[metric]
    fraction = (timestamp - left['timestamp']) / (right['timestamp'] - left['timestamp'])
    return math.exp(math.log(left[metric]) * (1 - fraction) + math.log(right[metric]) * fraction)


def analyse(directory, incomplete=False):
    import csv
    directory = Path(directory)
    receipts = json.loads((directory / 'runs.json').read_text(encoding='utf-8-sig'))
    if not isinstance(receipts, list):
        raise ValueError('Run receipts must be a JSON array')
    runs, flags, pending, systems = [], [], [], set()
    for receipt in receipts:
        run_id = receipt['Id']
        if not re.fullmatch(r'[A-Za-z0-9-]{1,60}', run_id):
            raise ValueError('Unsafe run ID')
        files = [directory / (run_id + suffix) for suffix in ['-frames.csv', '-MelonLoader.log', '-Player.log']]
        if not all(path.exists() for path in files):
            pending.append(run_id)
            continue
        with files[0].open(newline='', encoding='utf-8-sig') as stream:
            stats = frame_stats(list(csv.DictReader(stream)))
        loader, player = [path.read_text(encoding='utf-8-sig') for path in files[1:]]
        reasons = []
        system = re.search(r'CLAIMS system=(.*)', loader)
        if system:
            systems.add(system.group(1).strip())
        else:
            reasons.append('system/graphics receipt unavailable')
        if f'FEATURE measurement={run_id}; feature={receipt["Feature"]};' not in loader:
            reasons.append('feature identity not established')
        configuration = directory / (run_id + '-preferences.cfg')
        if not configuration.exists() or hashlib.sha256(configuration.read_bytes()).hexdigest().upper() != receipt['ConfigSHA256'].upper():
            reasons.append('configuration receipt hash differs/unavailable')
        if stats['focusedFrames'] != stats['frames']:
            reasons.append('unfocused frames')
        if not 29 <= stats['seconds'] <= 32:
            reasons.append('duration outside 30-second protocol')
        if f'CLAIMS TRACE END {run_id};' not in loader or 'saveFileChanged=False;' not in loader:
            reasons.append('completion/save receipt unavailable')
        if 'CLAIMS world ready; ships=20' not in loader:
            reasons.append('20-ship workload not established')
        if '[ERROR]' in loader or '[Error]' in loader or re.search(r'\b\w+Exception:', player):
            reasons.append('runtime error')
        if 'SteamAPI_Init() failed' in player or 'Steam is not initialized' in player:
            reasons.append('Steam host unavailable')
        flags.extend(f'{run_id}: {reason}' for reason in reasons)
        runs.append({**receipt, **stats, 'timestamp': datetime.fromisoformat(receipt['StartedUtc']).timestamp(),
                     'system': system.group(1).strip() if system else None,
                     'traceSha256': hashlib.sha256(files[0].read_bytes()).hexdigest().upper(), 'valid': not reasons})
    if pending and not incomplete:
        flags.append('Incomplete runs: ' + ', '.join(pending))
    if len({run['WorldSHA256'] for run in runs}) > 1:
        flags.append('Starting worlds differ')
    if len({run['ModSHA256'] for run in runs}) > 1:
        flags.append('Shipping binaries differ')
    if len(systems) > 1:
        flags.append('System/graphics receipts differ')
    controls = [row for row in runs if row['Feature'] == 'reference' and row['valid']]
    effects = {}
    for run in runs:
        if run['Feature'] == 'reference' or not run['valid']:
            continue
        baseline = control_estimate(controls, run['timestamp'], 'meanMs')
        if baseline is None:
            if not incomplete:
                flags.append(run['Id'] + ': no bracketing controls')
            continue
        effects.setdefault(run['Feature'], []).append({
            'id': run['Id'], 'changedMeanMs': run['meanMs'], 'referenceMeanMs': baseline,
            'changedVersusReferencePercent': (run['meanMs'] / baseline - 1) * 100,
            'changedFps': run['averageFps'], 'referenceFps': 1000 / baseline,
        })
    summaries = {}
    for feature, rows in effects.items():
        values = [row['changedVersusReferencePercent'] for row in rows]
        summaries[feature] = {'runs': len(rows), 'medianChangePercent': statistics.median(values),
                              'rangePercent': [min(values), max(values)], 'observations': rows,
                              'interpretation': 'screening only' if len(rows) < 3 else 'three-run conditional estimate; no universal or gameplay-equivalence claim'}
    control_ms = [row['meanMs'] for row in controls]
    return {'runs': runs, 'features': summaries, 'flags': flags, 'pending': pending,
            'controlMeanMsRange': [min(control_ms), max(control_ms)] if control_ms else None,
            'notes': ['Positive change means disabling/altering the feature made this scene slower; factory-cadence instead enables divisor 2.',
                      'Every run and invalidity flag is retained. Review all flags and configuration/module receipts.',
                      'References use native factory cadence with other shipping defaults. Ablations are marginal effects in that combination.',
                      'Elapsed start receipts approximate control position; startup duration and workload evolution limit precision.',
                      'One-run screens cannot establish small effects. Low-impact or unexercised features remain unresolved.',
                      'Do not sum feature effects or infer simulation safety from performance.']}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--allow-incomplete', action='store_true')
    args = parser.parse_args()
    report = analyse(args.directory, args.allow_incomplete)
    print(json.dumps(report, indent=2, allow_nan=False))
    raise SystemExit(2 if report['flags'] else 0)
