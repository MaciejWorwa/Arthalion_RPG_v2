"""Summarize CSV outcomes; training reward is not a substitute for evaluation wins."""
import argparse
import csv
from collections import defaultdict
import json
import math
from pathlib import Path


def wilson(wins, total):
    if not total:
        return 0.0, 1.0
    z = 1.959963984540054
    p = wins / total
    center = (p + z*z/(2*total)) / (1+z*z/total)
    radius = z * math.sqrt(p*(1-p)/total + z*z/(4*total*total)) / (1+z*z/total)
    return max(0, center-radius), min(1, center+radius)


def summarize(rows):
    wins = losses = draws = aborts = 0
    for row in rows:
        if row['result'] == 'TechnicalAbort':
            aborts += 1
        elif row['result'] == 'Draw':
            draws += 1
        elif (row['result'] == 'PlayerWin') == (row['learned_side'] == 'Player'):
            wins += 1
        else:
            losses += 1
    total = wins + losses + draws
    return {'games': total, 'wins': wins, 'losses': losses, 'draws': draws, 'aborts': aborts,
            'win_rate': wins/total if total else 0, 'ci95': wilson(wins, total),
            'seconds': sum(float(row['seconds']) for row in rows),
            'decisions': sum(int(row['decisions']) for row in rows)}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('path', type=Path)
    p.add_argument('--window', type=int, default=100)
    p.add_argument('--json', type=Path)
    args = p.parse_args()
    files = [args.path] if args.path.is_file() else sorted(args.path.rglob('episodes.csv'))
    groups = defaultdict(list)
    for file in files:
        with file.open(encoding='utf-8-sig', newline='') as stream:
            for row in csv.DictReader(stream, delimiter=';'):
                # Do not pool different checkpoint evaluations into a misleading mean.
                key = (str(file.parent.parent), row['mode'], row['model'], row['scenario'])
                groups[key].append(row)
    result = []
    for (run, mode, model, scenario), rows in groups.items():
        values = summarize(rows)
        recent = summarize(rows[-args.window:])
        record = {'run': run, 'mode': mode, 'model': model, 'scenario': scenario, **values,
                  'recent_window': args.window, 'recent': recent,
                  'by_side': {side: summarize([r for r in rows if r['learned_side'] == side]) for side in ('Player', 'Enemy')}}
        result.append(record)
        low, high = values['ci95']
        speed = values['decisions'] / max(0.001, values['seconds'])
        print(f"{mode} | {model} | {scenario} | {values['games']} games | W/L/D={values['wins']}/{values['losses']}/{values['draws']} "
              f"| wins={values['win_rate']:.1%} CI95=[{low:.1%}, {high:.1%}] | aborts={values['aborts']} | {speed:.1f} decisions/s\n  {run}")
    if not result:
        raise SystemExit('No episode results found.')
    if args.json:
        args.json.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')


if __name__ == '__main__':
    main()
