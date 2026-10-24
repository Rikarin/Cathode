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

echo "Installing Cathode $(cat "$here/VERSION") for linux-$expected"

if ! id cathode >/dev/null 2>&1; then
  useradd --system --no-create-home --home-dir /var/lib/cathode --shell /usr/sbin/nologin cathode
fi
usermod -a -G audio cathode

# Stop a running copy before its files are replaced.
systemctl stop cathode-server cathode-host 2>/dev/null || true

rm -rf /opt/cathode/bin /opt/cathode/editor
mkdir -p /opt/cathode
cp -R "$here/bin" "$here/editor" /opt/cathode/
cp "$here/VERSION" /opt/cathode/
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
