#!/usr/bin/env bash
# Builds the native shim into engine/artifacts/native. Re-run after changing native code.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
cmake -S "$here/cth_audio" -B "$here/../artifacts/obj/cth_audio" -DCMAKE_BUILD_TYPE=Release
cmake --build "$here/../artifacts/obj/cth_audio" --config Release
