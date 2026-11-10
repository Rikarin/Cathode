#!/bin/sh
# Installs (or updates) the Cathode headless rig from this folder: the engine and server in /opt/cathode, settings in
# /etc/cathode/cathode.env (kept on update), presets and assets in /var/lib/cathode, two systemd services started now
# and at every boot. Run as root: sudo ./install.sh
set -eu
here=$(cd "$(dirname "$0")" && pwd)

if [ "$(id -u)" -ne 0 ]; then
  echo "Run as root: sudo $0" >&2
  exit 1
fi

if ! command -v systemctl >/dev/null 2>&1; then
  echo "This installer needs systemd." >&2
  exit 1
fi

arch=$(uname -m)
expected=$(cat "$here/ARCH")
case "$arch:$expected" in
  x86_64:x64 | aarch64:arm64 | arm64:arm64) ;;
  *)
    echo "This package is for linux-$expected, but this machine is $arch." >&2
    exit 1
    ;;
esac

# The engine is built on Ubuntu 24.04 and needs that system's C library or a newer one (README.md, "What it needs").
# Find out here, before anything is installed, and not from a service that restarts for ever with a loader error.
needs="This package needs glibc 2.39 or newer (Ubuntu 24.04, Debian 13, Raspberry Pi OS based on Debian 13)"
if ! output=$("$here/bin/cathode-host" --version 2>&1); then
  echo "The engine does not start on this system:" >&2
  echo "$output" | sed 's/^/  /' >&2
  echo "$needs; this system has $(getconf GNU_LIBC_VERSION 2>/dev/null || echo "another C library")." >&2
  exit 1
fi
if command -v ldd >/dev/null 2>&1; then
  missing=$(ldd "$here/bin/libcth_audio.so" "$here/bin/libcth_nam.so" 2>&1 | grep 'not found' || true)
  if [ -n "$missing" ]; then
    echo "The engine's audio and NAM libraries do not load on this system:" >&2
    echo "$missing" >&2
    echo "$needs, and the ALSA library (libasound2)." >&2
    exit 1
  fi
fi

echo "Installing Cathode $(cat "$here/VERSION") for linux-$expected"

if ! id cathode >/dev/null 2>&1; then
  useradd --system --no-create-home --home-dir /var/lib/cathode --shell /usr/sbin/nologin cathode
fi
usermod -a -G audio cathode

# Stop a running copy before its files are replaced.
systemctl stop cathode-server cathode-host 2>/dev/null || true

# The server unpacks each version's native libraries into a folder of their own in its cache: drop the last
# version's (systemd makes the folder again, CacheDirectory=).
rm -rf /opt/cathode/bin /opt/cathode/editor /opt/cathode/licenses /var/cache/cathode
mkdir -p /opt/cathode
cp -R "$here/bin" "$here/editor" "$here/licenses" /opt/cathode/
cp "$here/VERSION" "$here/LICENSE" /opt/cathode/
chmod 755 /opt/cathode/bin/cathode-host /opt/cathode/bin/cathode-server /opt/cathode/bin/run-host.sh

mkdir -p /etc/cathode
if [ ! -f /etc/cathode/cathode.env ]; then
  token=$(head -c 24 /dev/urandom | od -An -tx1 | tr -d ' \n')
  sed "s/^Cathode__Token=.*/Cathode__Token=$token/" "$here/cathode.env.example" > /etc/cathode/cathode.env
  created=1
else
  created=0
fi
chown root:cathode /etc/cathode/cathode.env
chmod 640 /etc/cathode/cathode.env

cp "$here/systemd/cathode-host.service" "$here/systemd/cathode-server.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now cathode-host cathode-server

token=$(sed -n 's/^Cathode__Token=//p' /etc/cathode/cathode.env)
port=$(sed -n 's/^ASPNETCORE_URLS=.*:\([0-9]*\).*/\1/p' /etc/cathode/cathode.env)
echo
echo "Cathode is running."
for address in $(hostname -I 2>/dev/null || true); do
  case "$address" in *:*) continue ;; esac
  echo "  Open http://$address:${port:-5180}/ in a browser, or add it as a remote server in the desktop app."
done
echo "  Access token: $token"
if [ "$created" -eq 1 ]; then
  echo
  echo "Next: choose the audio interface. List them with"
  echo "  sudo -u cathode /opt/cathode/bin/cathode-host --devices"
  echo "then set CATHODE_DEVICE in /etc/cathode/cathode.env and run"
  echo "  sudo systemctl restart cathode-host cathode-server"
fi
