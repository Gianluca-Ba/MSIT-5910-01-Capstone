"""Summarize Measure-Unit6.ps1 CSV output using only Python's standard library.

PowerShell exports numbers using the current culture. Accept either a decimal
point or decimal comma; CSV quoting preserves the field boundaries.
"""
import sys
import csv
import json
import math
import statistics
from pathlib import Path

evidence = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / 'docs/unit6/evidence'

def read(name):
    with (evidence / name).open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))

def stats(values):
    values = sorted(float(value.replace(',', '.')) for value in values if value)
    if not values:
        return {'n': 0}
    return {'n': len(values), 'mean': statistics.mean(values),
            'median': statistics.median(values),
            'p95': values[math.ceil(.95 * len(values)) - 1],
            'min': values[0], 'max': values[-1]}

rows = read('requests.csv')
result = {scenario: stats(row['HttpMs'] for row in rows
                         if row['Scenario'] == scenario)
          for scenario in ['valid', 'duplicate', 'conflict', 'invalid']}
result['end_to_end'] = stats(row['ObservedEndToEndMs'] for row in rows
                             if row['Scenario'] == 'valid')
result['trials'] = read('trials.csv')
result['custom'] = read('custom-validation.csv')
result['matched'] = sum(row['Matched'] == 'True' for row in rows)
result['total'] = len(rows)
result['unobserved_valid'] = sum(row['Scenario'] == 'valid' and
                               not row['ObservedEndToEndMs'] for row in rows)
(evidence / 'summary.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
