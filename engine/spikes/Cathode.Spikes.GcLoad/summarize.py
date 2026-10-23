#!/usr/bin/env python3
"""Turns results.jsonl from run.sh into a markdown table. Usage: python3 -I summarize.py results.jsonl"""
import json
import sys

rows = [json.loads(line) for line in open(sys.argv[1]) if line.strip()]
print("| Configuration | Buffer | p50 | p99.9 | p99.99 | Max | ≥ 50 % | Over budget | Driver overloads | GCs 0/1/2 | GC pause (ms) | Alloc (MB/s) | Requests |")
print("|---|---|---|---|---|---|---|---|---|---|---|---|---|")
for r in rows:
    print(f"| {r['Label']} | {r['Buffer']} | {r['P50']:.0f} % | {r['P999']:.0f} % | {r['P9999']:.0f} % | {r['Max']:.1f} % | "
          f"{r['Over50']} | {r['OverBudget']} | {r['DriverOverloads']} | {r['Gen0']}/{r['Gen1']}/{r['Gen2']} | "
          f"{r['GcPauseMs']:.1f} | {r['AllocMBps']:.0f} | {r['RequestsInProcess'] or '—'} |")
