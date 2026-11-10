#!/bin/sh
# Prints the newest glibc version the given Linux binaries ask for (their floor: they do not load on an older C
# library), and fails when it is newer than the promised one. The release workflow runs it on the engine it has just
# built, so the minimum distribution the README and the manual name cannot move without someone noticing.
# Usage: packaging/glibc-floor.sh <promised version, e.g. 2.39> <binary>...
set -eu
promised=$1
shift

versions=$(objdump -T "$@" | grep -o 'GLIBC_[0-9][0-9.]*' | sed 's/^GLIBC_//' | sort -Vu)
if [ -z "$versions" ]; then
  echo "No glibc symbol versions in: $*" >&2
  exit 1
fi
floor=$(echo "$versions" | tail -1)
echo "Needs glibc $floor or newer (promised: $promised)"

if [ "$(printf '%s\n' "$promised" "$floor" | sort -V | tail -1)" != "$promised" ]; then
  echo "These need glibc $floor, newer than the promised $promised:" >&2
  for file in "$@"; do
    if objdump -T "$file" | grep -Eq "GLIBC_$floor([^0-9.]|\$)"; then echo "  $file" >&2; fi
  done
  echo "Build on an older distribution, or raise the minimum in README.md, packaging/headless/README.md," >&2
  echo "docs/10-releases.md, the manual and install.sh." >&2
  exit 1
fi
