#!/bin/sh
# Starts cathode-host for the cathode-host service from /etc/cathode/cathode.env (systemd passes its variables).
set -eu
here=$(dirname "$0")

# The extra options as the shell would read them, quotes included (the file is root's, like the unit).
eval "set -- ${CATHODE_HOST_ARGS:-}"
if [ -n "${CATHODE_DEVICE:-}" ]; then
  set -- --device "$CATHODE_DEVICE" "$@"
else
  set -- --null-audio "$@"
fi

exec "$here/cathode-host" --serve --socket /run/cathode/host.sock \
  --rate "${CATHODE_RATE:-48000}" --buffer "${CATHODE_BUFFER:-64}" "$@"
