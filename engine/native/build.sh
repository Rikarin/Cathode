#!/usr/bin/env bash
# Builds the native libraries into engine/artifacts/native. Re-run after changing native code.
# NeuralAmpModelerCore (pinned in cth_nam/CMakeLists.txt) and its Eigen submodule are fetched once into
# engine/artifacts/deps, with retries, and reused from there: CI caches that folder, so a GitLab outage
# (Eigen's home) cannot break the build. On Windows, Steinberg's ASIO SDK (pinned by URL and SHA-256 in
# cth_audio/CMakeLists.txt) is fetched the same way.
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

asio_arg=""
case "$(uname -s)" in
  MINGW* | MSYS* | CYGWIN*)
    url="$(sed -n 's/^ *URL \(https:[^ ]*\.zip\)$/\1/p' "$here/cth_audio/CMakeLists.txt")"
    sha="$(sed -n 's/.*URL_HASH SHA256=\([0-9a-f]\{64\}\).*/\1/p' "$here/cth_audio/CMakeLists.txt")"
    asio="$here/../artifacts/deps/$(basename "$url" .zip)"
    if [ ! -f "$asio/.complete" ]; then
      for attempt in 1 2 3 4 5; do
        rm -rf "$asio" "$asio.zip" "$asio.tmp"
        if curl -fsSL -o "$asio.zip" "$url" \
          && [ "$(cmake -E sha256sum "$asio.zip" | cut -d' ' -f1)" = "$sha" ] \
          && mkdir -p "$asio.tmp" && (cd "$asio.tmp" && cmake -E tar xf "$asio.zip") \
          && mv "$asio.tmp/ASIOSDK" "$asio"; then
          touch "$asio/.complete"
          rm -rf "$asio.zip" "$asio.tmp"
          break
        fi
        echo "fetching the ASIO SDK failed (attempt $attempt); retrying in $((attempt * 20)) s" >&2
        sleep $((attempt * 20))
      done
      [ -f "$asio/.complete" ] || { echo "could not fetch the ASIO SDK" >&2; exit 1; }
    fi
    asio_arg="-DFETCHCONTENT_SOURCE_DIR_ASIOSDK=$asio"
    ;;
esac

cmake -S "$here" -B "$here/../artifacts/obj/native" -DCMAKE_BUILD_TYPE=Release \
  -DFETCHCONTENT_SOURCE_DIR_NAM_CORE="$nam" ${asio_arg:+"$asio_arg"}
cmake --build "$here/../artifacts/obj/native" --config Release --parallel
