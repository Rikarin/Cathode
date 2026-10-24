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
