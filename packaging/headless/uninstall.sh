#!/bin/sh
# Removes the Cathode headless rig: the services, /opt/cathode, the server's unpacked bundle (/var/cache/cathode) and
# the socket folder (/run/cathode). Settings (/etc/cathode) and presets (/var/lib/cathode) stay unless --purge is
# given. Run as root: sudo ./uninstall.sh [--purge]
set -eu

if [ "$(id -u)" -ne 0 ]; then
  echo "Run as root: sudo $0 $*" >&2
  exit 1
fi

systemctl disable --now cathode-server cathode-host 2>/dev/null || true
rm -f /etc/systemd/system/cathode-host.service /etc/systemd/system/cathode-server.service
systemctl daemon-reload
# The cache and the socket folder hold nothing of the user's, and systemd makes them again for a later install
# (/run/cathode outlives a stopped host: RuntimeDirectoryPreserve).
rm -rf /opt/cathode /var/cache/cathode /run/cathode

if [ "${1:-}" = "--purge" ]; then
  rm -rf /etc/cathode /var/lib/cathode
  userdel cathode 2>/dev/null || true
  echo "Cathode removed, with its settings, presets and assets."
else
  echo "Cathode removed. Settings stay in /etc/cathode and presets in /var/lib/cathode (--purge removes them)."
fi
