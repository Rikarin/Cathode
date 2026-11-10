#!/bin/sh
# Checks run-host.sh: the command line it builds from the settings in cathode.env, as systemd hands them over (one
# environment variable each, quotes already removed). A stand-in cathode-host prints its arguments one per line.
# Usage: packaging/headless/tests/run-host.test.sh   (any POSIX shell; set SH to try another, e.g. SH=dash)
set -eu
here=$(cd "$(dirname "$0")" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

cp "$here/../run-host.sh" "$work/run-host.sh"
cat > "$work/cathode-host" <<'HOST'
#!/bin/sh
printf '%s\n' "$@"
HOST
chmod +x "$work/cathode-host"
# A file an unquoted "*" would match, were the options read as file patterns.
touch "$work/bait"

failed=0
# check <name> <expected arguments, one per line> [VARIABLE=value ...]
check() {
  name=$1
  expected=$2
  shift 2
  if actual=$(cd "$work" && env -i PATH="$PATH" "$@" "${SH:-sh}" ./run-host.sh 2>&1) && [ "$actual" = "$expected" ]; then
    echo "ok: $name"
  else
    echo "FAILED: $name"
    echo "  expected: $(echo "$expected" | tr '\n' ' ')"
    echo "  actual:   $(echo "$actual" | tr '\n' ' ')"
    failed=1
  fi
}

base='--serve
--socket
/run/cathode/host.sock'

check "no settings: the null backend at 48 kHz and 64 frames" "$base
--rate
48000
--buffer
64
--null-audio"

check "a device, a rate and a buffer" "$base
--rate
44100
--buffer
128
--device
alsa:USB Audio" \
  "CATHODE_DEVICE=alsa:USB Audio" CATHODE_RATE=44100 CATHODE_BUFFER=128

check "the example in cathode.env.example: the input map's ';' stays in its option" "$base
--rate
48000
--buffer
64
--device
alsa:USB
--input-map
1;2
--outputs
1,3,0,0
--loop-in
3,4
--loop-out
3,4
--di-out
5,6" \
  CATHODE_DEVICE=alsa:USB "CATHODE_HOST_ARGS=--input-map 1;2 --outputs 1,3,0,0 --loop-in 3,4 --loop-out 3,4 --di-out 5,6"

check "an input map with empty places" "$base
--rate
48000
--buffer
64
--null-audio
--input-map
1;3,4;;;5,6" \
  "CATHODE_HOST_ARGS=--input-map 1;3,4;;;5,6"

# shellcheck disable=SC2016 # the settings are literal text, which is the point
check "nothing in the options is run or expanded" "$base
--rate
48000
--buffer
64
--null-audio
--mute
*
\$(touch
ran)
\`touch
ran\`
|
touch
ran" \
  'CATHODE_HOST_ARGS=--mute * $(touch ran) `touch ran` | touch ran'
if [ -e "$work/ran" ]; then
  echo "FAILED: the options were run as shell"
  failed=1
fi

exit "$failed"
