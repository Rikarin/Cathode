#!/usr/bin/env bash
# Builds the native libraries into engine/artifacts/native. Re-run after changing native code.
# NeuralAmpModelerCore (pinned in cth_nam/CMakeLists.txt) and its Eigen submodule are fetched once into
# engine/artifacts/deps, with retries, and reused from there: CI caches that folder, so a GitLab outage
# (Eigen's home) cannot break the build.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
tag="$(sed -n 's/.*GIT_TAG \([0-9a-f]\{40\}\).*/\1/p' "$here/cth_nam/CMakeLists.txt")"
nam="$here/../artifacts/deps/nam_core-$tag"

if [ ! -f "$nam/.complete" ]; then
  for attempt in 1 2 3 4 5; do
    rm -rf "$nam"
    if git clone --quiet https://github.com/sdatkinson/NeuralAmpModelerCore.git "$nam" \
      && git -C "$nam" checkout --quiet "$tag" \
      && git -C "$nam" submodule update --quiet --init Dependencies/eigen; then
      touch "$nam/.complete"
      break
    fi
    echo "fetching NeuralAmpModelerCore failed (attempt $attempt); retrying in $((attempt * 20)) s" >&2
    sleep $((attempt * 20))
  done
  [ -f "$nam/.complete" ] || { echo "could not fetch NeuralAmpModelerCore" >&2; exit 1; }
fi

cmake -S "$here" -B "$here/../artifacts/obj/native" -DCMAKE_BUILD_TYPE=Release \
  -DFETCHCONTENT_SOURCE_DIR_NAM_CORE="$nam"
cmake --build "$here/../artifacts/obj/native" --config Release --parallel
