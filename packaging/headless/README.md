# Cathode headless rig

The engine and server for a small Linux computer in your rig (a Raspberry Pi 5, a mini PC, a NUC) with an audio
interface and no display. You play and edit it remotely: from any browser on the network (a phone, a tablet, a laptop)
or from the Cathode desktop app's remote-server connection.

## Install

On the rig, as root:

```sh
tar xzf cathode-server-<version>-linux-<arch>.tar.gz
cd cathode-server-<version>-linux-<arch>
sudo ./install.sh
```

It installs to `/opt/cathode`, creates a `cathode` system user in the `audio` group, writes the settings to
`/etc/cathode/cathode.env` with a new access token, keeps presets and assets in `/var/lib/cathode`, and starts two
systemd services now and at every boot: `cathode-host` (the real-time engine) and `cathode-server` (the API and the web
editor). At the end it prints the address to open and the access token.

Running `install.sh` from a newer package updates the program and keeps your settings, presets and token.

## Choose the audio interface

The engine starts without audio until you pick an interface:

```sh
sudo -u cathode /opt/cathode/bin/cathode-host --devices
sudo nano /etc/cathode/cathode.env      # set CATHODE_DEVICE, and the rate, buffer and channel options
sudo systemctl restart cathode-host cathode-server
```

## Connect

- **Browser:** open `http://<rig address>:5180/` and enter the access token once; the browser remembers it.
- **Desktop app:** Settings, Audio setup, Server: Remote, with the same address and token.

The token is in `/etc/cathode/cathode.env` (`Cathode__Token`). Anyone with it can change your rig, so keep the rig on a
network you trust.

## Look after it

```sh
systemctl status cathode-host cathode-server
journalctl -u cathode-host -u cathode-server -f       # logs, including audio dropouts
sudo ./uninstall.sh                                    # remove the program (settings and presets stay)
sudo ./uninstall.sh --purge                            # remove everything
```

For the lowest latency, use a kernel with `PREEMPT_RT` or the distribution's low-latency kernel, set the CPU governor
to `performance`, and raise `CATHODE_BUFFER` (64, 128, 256) until the log shows no dropouts.

## Sandboxing

Both services run as the unprivileged `cathode` user inside a systemd sandbox, so a bug in the server's upload parsers
(WAV, NAM models, preset packages) or in the engine's IPC cannot reach the rest of the machine. Check the result with
`systemd-analyze security cathode-server cathode-host`.

| Directive                                                   | cathode-server                                      | cathode-host                                         |
| ----------------------------------------------------------- | --------------------------------------------------- | ---------------------------------------------------- |
| `NoNewPrivileges`, `CapabilityBoundingSet=` (empty)         | yes, no capabilities                                | yes, no capabilities (systemd applies `Nice` and the RT limits before it starts) |
| `ProtectSystem=strict`                                      | writes `/var/lib/cathode` (data), `/var/cache/cathode` (the unpacked single-file bundle), `/run/cathode` | writes `/run/cathode` (socket, telemetry) |
| `ProtectHome`                                               | yes                                                 | yes                                                  |
| `PrivateTmp`                                                | yes, shared with the host (`JoinsNamespaceOf`)      | yes; the looper export is a file the host writes there and the server sends |
| `PrivateDevices`                                            | yes (no devices)                                    | no: `DevicePolicy=closed`, `DeviceAllow=char-alsa rw` (`/dev/snd`, audio and MIDI) |
| `RestrictAddressFamilies`                                   | `AF_UNIX AF_INET AF_INET6`                          | `AF_UNIX` (the server's socket, JACK)                |
| `RestrictRealtime`                                          | yes                                                 | no: the audio thread runs `SCHED_FIFO`               |
| `MemoryDenyWriteExecute`                                    | no: the .NET JIT writes the code it runs            | no: NativeAOT should allow it, but it is not yet verified on a rig |
| `SystemCallFilter=@system-service ~@privileged`             | yes (no `ptrace`: the server cannot trace the host) | yes (`@system-service` includes the real-time and `mlock` calls) |
| `ProtectKernelTunables/Modules/Logs`, `ProtectControlGroups`, `ProtectClock`, `ProtectHostname`, `ProtectProc=invisible`, `RestrictNamespaces`, `RestrictSUIDSGID`, `LockPersonality`, `SystemCallArchitectures=native`, `UMask=0027` | yes | yes |

To loosen one for your rig, add a drop-in rather than editing the unit (it is replaced on update), for example
`sudo systemctl edit cathode-host`:

```ini
[Service]
# jack1 keeps its sockets in /tmp: let the host see the real one
PrivateTmp=no
```

(and the same drop-in for `cathode-server`, so the two still share one `/tmp` for the looper export).

Serving the editor on a port below 1024 (such as 80) needs `AmbientCapabilities=CAP_NET_BIND_SERVICE` and
`CapabilityBoundingSet=CAP_NET_BIND_SERVICE` on `cathode-server`. A JACK or PipeWire server running as another user
also needs its socket reachable from the `cathode` user.
