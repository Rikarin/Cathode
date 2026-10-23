#!/usr/bin/env bash
# Spike S2 matrix: callback timing under REST/GC load, in-process vs out-of-process (docs/adr/0001-process-model.md).
# Usage: run.sh "<device uid>" [results.jsonl]
# Plays nothing audible (output is muted), but holds the device for about eight minutes.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
engine="$here/../.."
device="${1:?device uid (see: cathode-host --devices)}"
results="${2:-$here/results.jsonl}"
nam="$engine/artifacts/native/nam-models/A2.nam"
bin="$here/bin/Release/net10.0/Cathode.Spikes.GcLoad"
warmup=5
seconds=20
load_seconds=$((warmup + seconds + 3))

dotnet build "$here" -c Release -v quiet -nologo >/dev/null
: > "$results"

audio() { "$bin" audio --device "$device" --nam "$nam" --warmup "$warmup" --seconds "$seconds" --results "$results" "$@"; }
load() { sleep 1; "$bin" load --seconds "$load_seconds" "$@"; }
realistic=(--concurrency 2 --rate 100)   # an editor dragging knobs plus polling, with occasional uploads
stress=(--concurrency 8)                 # unthrottled: as much as Kestrel will take

for buffer in 64 32; do
  audio --buffer "$buffer" --label "baseline"
  if [ "$buffer" = 64 ]; then
    audio --buffer "$buffer" --alloc-storm 50 --label "alloc storm 50 MB/s"
    audio --buffer "$buffer" --alloc-storm 500 --label "alloc storm 500 MB/s"
  fi

  load "${realistic[@]}" & audio --buffer "$buffer" --server --label "in-proc realistic"; wait
  load "${stress[@]}" & audio --buffer "$buffer" --server --label "in-proc stress"; wait

  if [ "$buffer" = 64 ]; then
    load "${stress[@]}" & DOTNET_gcConcurrent=0 audio --buffer "$buffer" --server --label "in-proc stress, non-concurrent"; wait
    load "${stress[@]}" & DOTNET_gcServer=1 audio --buffer "$buffer" --server --label "in-proc stress, server GC"; wait
    load "${stress[@]}" & DOTNET_GCgen0size=0x10000000 audio --buffer "$buffer" --server --label "in-proc stress, gen0 256 MB"; wait
    load "${stress[@]}" & audio --buffer "$buffer" --server --sustained-low-latency --label "in-proc stress, SustainedLowLatency"; wait
  fi

  # Out of process: the API runs in its own process (ASP.NET's default server GC); the audio process only renders.
  DOTNET_gcServer=1 "$bin" server --seconds $((load_seconds + 2)) >/dev/null & load "${realistic[@]}" & audio --buffer "$buffer" --label "out-of-proc realistic"; wait
  DOTNET_gcServer=1 "$bin" server --seconds $((load_seconds + 2)) >/dev/null & load "${stress[@]}" & audio --buffer "$buffer" --label "out-of-proc stress"; wait
done

echo "results: $results"
