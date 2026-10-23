#!/usr/bin/env bash
# Two-process soak in the production layout: NativeAOT cathode-host on a device (muted), cathode-server in Release,
# and cathode-soak driving the API. Publishes everything first.
#   run-soak.sh "<device uid>|null" [minutes=60] [results folder=./soak-results] [max host GCs, -1 = no gate]
# "null" runs the host on the null backend (no audio device), as on CI runners: the API, IPC and host GC gates still
# apply; driver overloads and native timing need a real device.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
engine="$here/../.."
device="${1:?device uid (see: cathode-host --devices)}"
minutes="${2:-60}"
results="${3:-$PWD/soak-results}"
max_gcs="${4:--1}"
build="$(mktemp -d)"
data="$(mktemp -d)"
socket="/tmp/cathode-soak-$$.sock"
mkdir -p "$results"

rid="$(dotnet --info | awk '/RID:/ { print $2; exit }')"
dotnet publish "$engine/src/Cathode.Host" -c Release -r "$rid" -o "$build/host" -v quiet -nologo
dotnet publish "$engine/src/Cathode.Server" -c Release -o "$build/server" -v quiet -nologo
dotnet publish "$engine/tools/Cathode.Soak" -c Release -o "$build/soak" -v quiet -nologo

# Bare hex: the NativeAOT runtime ignores a 0x prefix. 256 MB of gen0 keeps the host collection-free for hours.
if [ "$device" = "null" ]; then
  audio=(--null-audio)
else
  audio=(--device "$device" --mute)
fi
DOTNET_GCgen0size="${DOTNET_GCgen0size:-10000000}" "$build/host/cathode-host" --serve --socket "$socket" --buffer 64 "${audio[@]}" > "$results/host.log" 2>&1 &
host=$!
"$build/server/cathode-server" --Cathode:Engine:SocketPath="$socket" --Cathode:DataDirectory="$data" > "$results/server.log" 2>&1 &
server=$!
trap 'kill $server $host 2>/dev/null || true; wait $server $host 2>/dev/null || true; rm -rf "$build" "$data"' EXIT

status=0
"$build/soak/cathode-soak" --minutes "$minutes" \
  --nam "$engine/artifacts/native/nam-models/A2.nam" --ir "$engine/examples/synthetic-cab.wav" \
  --out "$results/soak.jsonl" --max-host-gcs "$max_gcs" | tee "$results/summary.txt" || status=${PIPESTATUS[0]}
exit $status
