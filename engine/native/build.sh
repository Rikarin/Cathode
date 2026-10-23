#!/usr/bin/env bash
# Builds the native libraries into engine/artifacts/native. Re-run after changing native code.
# The first run downloads NeuralAmpModelerCore (pinned, see cth_nam/CMakeLists.txt).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
cmake -S "$here" -B "$here/../artifacts/obj/native" -DCMAKE_BUILD_TYPE=Release
cmake --build "$here/../artifacts/obj/native" --config Release --parallel
