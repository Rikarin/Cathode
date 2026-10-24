#!/bin/sh
# Removes the Cathode headless rig: the services and /opt/cathode. Settings (/etc/cathode) and presets
# (/var/lib/cathode) stay unless --purge is given. Run as root: sudo ./uninstall.sh [--purge]
set -eu

if [ "$(id -u)" -ne 0 ]; then
  echo "Run as root: sudo $0 $*" >&2
  exit 1
fi

systemctl disable --now cathode-server cathode-host 2>/dev/null || true
rm -f /etc/systemd/system/cathode-host.service /etc/systemd/system/cathode-server.service
systemctl daemon-reload
rm -rf /opt/cathode

if [ "${1:-}" = "--purge" ]; then
  rm -rf /etc/cathode /var/lib/cathode
  userdel cathode 2>/dev/null || true
  echo "Cathode removed, with its settings, presets and assets."
else
  echo "Cathode removed. Settings stay in /etc/cathode and presets in /var/lib/cathode (--purge removes them)."
fi
