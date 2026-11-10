#!/usr/bin/env bash
# Fails when engine/global.json pins a .NET SDK older than the latest one of its .NET version. CI installs exactly the
# pinned SDK, and cathode-server (self-contained) and cathode-host (NativeAOT) carry the runtime and ASP.NET Core that
# SDK came with, so a release built with last month's SDK ships last month's security fixes. release.yml runs this
# on a tag. When Microsoft's release list cannot be read it warns and passes: an outage there must not stop a release.
# Usage: .github/actions/setup-dotnet/sdk-is-current.sh   (needs curl and jq; DOTNET_RELEASES_FILE reads a local copy)
set -euo pipefail
root=$(cd "$(dirname "$0")/../../.." && pwd)

pinned=$(jq -er '.sdk.version' "$root/engine/global.json")
channel=${pinned%.*}
metadata="https://builds.dotnet.microsoft.com/dotnet/release-metadata/$channel/releases.json"

if [ -n "${DOTNET_RELEASES_FILE:-}" ]; then
  latest=$(jq -er '."latest-sdk"' "$DOTNET_RELEASES_FILE")
elif ! latest=$(curl -fsSL --retry 3 "$metadata" | jq -er '."latest-sdk"'); then
  echo "::warning::could not read the latest .NET $channel SDK from $metadata; engine/global.json ($pinned) was not checked"
  exit 0
fi

echo "engine/global.json pins SDK $pinned; the latest .NET $channel SDK is $latest"
if [ "$pinned" != "$latest" ] && [ "$(printf '%s\n' "$pinned" "$latest" | sort -V | tail -1)" = "$latest" ]; then
  echo "::error::engine/global.json pins SDK $pinned, but $latest is out: set sdk.version to $latest on master and tag that commit (docs/10-releases.md, \"The .NET runtime in a release\")"
  exit 1
fi
