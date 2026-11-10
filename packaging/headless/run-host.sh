#!/bin/sh
# Starts cathode-host for the cathode-host service from /etc/cathode/cathode.env (systemd passes its variables).
set -eu
here=$(dirname "$0")

# The extra options, split at spaces and at nothing else: the value is never read as shell, so an input map's ";"
# stays inside its option and a "*" is not a file pattern. No option here takes a value with a space in it, and
# systemd has already dropped any quotes from the value (EnvironmentFile=), so there is nothing to unquote.
set -f
# shellcheck disable=SC2086 # split on purpose
set -- ${CATHODE_HOST_ARGS:-}
set +f
if [ -n "${CATHODE_DEVICE:-}" ]; then
  set -- --device "$CATHODE_DEVICE" "$@"
else
  set -- --null-audio "$@"
fi

exec "$here/cathode-host" --serve --socket /run/cathode/host.sock \
  --rate "${CATHODE_RATE:-48000}" --buffer "${CATHODE_BUFFER:-64}" "$@"
