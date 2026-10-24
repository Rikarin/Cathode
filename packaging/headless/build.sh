#!/usr/bin/env bash
# Builds the headless rig package for this machine (no cross-compiling: NativeAOT cannot), into
# dist/headless/cathode-server-<version>-<os>-<arch>.tar.gz at the repository root: the engine host and the server
# (as the desktop app ships them), the editor's web build, the systemd services and the install scripts.
# Usage: packaging/headless/build.sh [version]   (default: CATHODE_VERSION, else the editor workspace's version)
# Needs: .NET (engine/global.json), Node with pnpm (editor/package.json), CMake and a C compiler for the native shims.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)

version=${1:-${CATHODE_VERSION:-}}
if [ -z "$version" ]; then
  version=$(node -p "require('$root/editor/package.json').version")
fi
version=${version#v}

case "$(uname -s)" in
  Linux) os=linux ;;
  Darwin) os=osx ;; # for trying the package layout locally; the installer itself is Linux and systemd only
  *) echo "Build the headless package on Linux (or macOS for a local try)." >&2; exit 1 ;;
esac
case "$(uname -m)" in
  x86_64 | amd64) arch=x64 ;;
  aarch64 | arm64) arch=arm64 ;;
  *) echo "Unsupported architecture $(uname -m)." >&2; exit 1 ;;
esac

name="cathode-server-$version-$os-$arch"
stage="$root/dist/headless/$name"
rm -rf "$stage" "$stage.tar.gz"
mkdir -p "$stage/bin"

echo "Building the editor's web build"
(cd "$root/editor" && pnpm exec nx build editor --configuration production)
echo "Publishing the engine host and server"
(cd "$root/editor" && node apps/desktop/scripts/publish-engine.mjs)

cp -R "$root/editor/dist/engine/." "$stage/bin/"
cp -R "$root/editor/dist/apps/editor/browser" "$stage/editor"
cp "$here/run-host.sh" "$stage/bin/"
cp -R "$here/systemd" "$here/install.sh" "$here/uninstall.sh" "$here/cathode.env.example" "$here/README.md" "$stage/"
for licence in LICENSE LICENSE.md COPYING; do
  if [ -f "$root/$licence" ]; then cp "$root/$licence" "$stage/"; fi
done
echo "$version" > "$stage/VERSION"
echo "$arch" > "$stage/ARCH"

tar -C "$root/dist/headless" -czf "$stage.tar.gz" "$name"
echo "Packaged $stage.tar.gz"
