The headless server runs Cathode's engine on a small Linux computer that lives in your rig, with your audio interface plugged into it and no screen, keyboard or mouse. You play through it like a hardware unit, and edit it from any browser on the same network (a phone, a tablet, a laptop) or from the Cathode desktop app.

> **Note:** Linux audio in Cathode is new. It has been tested in containers but not yet on a real interface, so treat the headless server as early. Try it at home before you rely on it at a gig.

## What you need

- **A small Linux computer** with a 64-bit system: a Raspberry Pi 5 (ARM64), or a mini PC or NUC (x64). The installer needs **systemd**, which most distributions use, including Raspberry Pi OS and Ubuntu.
- **A USB audio interface** that works on Linux without special drivers (most class-compliant ones do).
- **A network** shared with the device you will edit from: Ethernet, or the same Wi-Fi.
- Optionally a USB MIDI foot controller. It plugs into the rig, not into the device you edit from.

## Install

1. Download `cathode-server-<version>-linux-arm64.tar.gz` (Raspberry Pi and other ARM64 boards) or `cathode-server-<version>-linux-x64.tar.gz` (Intel and AMD) from the [Releases page](https://github.com/Rikarin/cathode/releases), and copy it to the rig.
2. On the rig, in a terminal (or over SSH):

   ```sh
   tar xzf cathode-server-<version>-linux-arm64.tar.gz
   cd cathode-server-<version>-linux-arm64
   sudo ./install.sh
   ```

The installer checks that the package matches the computer, then:

- installs the program in `/opt/cathode`;
- creates a system user `cathode` in the `audio` group;
- writes the settings to `/etc/cathode/cathode.env`, with a new, random **access token**;
- keeps presets, IRs and everything else in `/var/lib/cathode`;
- starts two services, now and at every boot: **cathode-host** (the real-time audio engine) and **cathode-server** (the editor and its API).

At the end it prints the address to open, such as `http://192.168.1.20:5180/`, and the access token. Note both.

## Choose the audio interface

The engine starts without audio until you tell it which interface to use.

1. List the interfaces the engine can see:

   ```sh
   sudo -u cathode /opt/cathode/bin/cathode-host --devices
   ```

   ALSA devices are listed as `alsa:<card name>` (for example `alsa:USB`), JACK clients as `jack:<name>`. Card names stay the same across reboots and replugging, unlike card numbers.

2. Edit the settings file:

   ```sh
   sudo nano /etc/cathode/cathode.env
   ```

   Set `CATHODE_DEVICE` to the device from the list, for example `CATHODE_DEVICE=alsa:USB`.

3. Restart the services:

   ```sh
   sudo systemctl restart cathode-host cathode-server
   ```

Then open the editor and check **Settings ▸ Audio setup**: **Backend** should name the audio system, not "null".

### The settings file

`/etc/cathode/cathode.env` holds everything the rig needs. Restart both services after editing it.

| Setting             | Default               | What it does                                                                                                              |
| ------------------- | --------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| `CATHODE_DEVICE`    | empty                 | The audio interface, as `--devices` lists it. Empty runs without audio (the null backend), useful to try the setup first. |
| `CATHODE_RATE`      | `48000`               | Sample rate.                                                                                                              |
| `CATHODE_BUFFER`    | `64`                  | Buffer size in samples. Raise it to 128 or 256 if the log reports dropouts.                                               |
| `CATHODE_HOST_ARGS` | empty                 | The channel routing, as options (below).                                                                                  |
| `ASPNETCORE_URLS`   | `http://0.0.0.0:5180` | Where the editor listens: every network interface, port 5180. Change the number to use another port.                      |
| `Cathode__Token`    | generated             | The access token. Treat it like a password.                                                                               |

`CATHODE_HOST_ARGS` takes the same routing that the desktop app's Audio setup page offers, written as options:

| Option                    | Example   | Meaning                                                                                                                                               |
| ------------------------- | --------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| `--input-map`             | `"1;2"`   | Device channels for Input 1, Input 2, Input 3, Input 4, USB 5/6 and USB 7/8, separated by `;`. `3,4` is a stereo pair. Default: Input 1 on channel 1. |
| `--outputs`               | `1,3,0,0` | The first device channel of Outputs 1 to 4; 0 leaves one unused. Default: Output 1 on channels 1 and 2.                                               |
| `--loop-in`, `--loop-out` | `3,4`     | Device channels for the FX Loop block's return and send.                                                                                              |
| `--di-out`                | `5,6`     | Device channels for the dry DI out.                                                                                                                   |

For example: `CATHODE_HOST_ARGS=--input-map "1;2" --outputs 1,3,0,0`. See [Routing inputs and outputs](/manual/audio-setup#routing-inputs-and-outputs) for what each does.

## Connect from a browser

1. On a phone, tablet or computer on the same network, open the address the installer printed: `http://<rig address>:5180/`.
2. The first time, the page asks for the **access token**. Paste it and click **Connect**.

The browser remembers the token for that rig, so next time the editor opens straight away. It is the full editor, the same as in the desktop app, except that **Audio setup** shows the engine figures and the input calibration but cannot choose the interface (that is the settings file's job).

<!-- screenshot wanted: the editor in a phone browser, connected to a headless rig (remote.png, already listed in screenshots.ts) -->

If you lose the token, it is the `Cathode__Token` line in `/etc/cathode/cathode.env`:

```sh
sudo grep Cathode__Token /etc/cathode/cathode.env
```

> **Tip:** The editor also reads a `server` and a `token` from its address, for example `?server=http://192.168.1.20:5180`, which lets an editor served from one place talk to a server somewhere else. Avoid putting the token in the address: it ends up in your browser's history.

## Connect from the desktop app

The desktop app can edit the rig instead of running its own engine:

1. Open **Settings ▸ Audio setup**.
2. Under **Server**, choose **Remote**.
3. Enter the **URL**, for example `http://192.168.1.20:5180`, and the **Access token**.
4. Click **Connect**. The editor reloads, connected to the rig.

To go back to the computer's own engine, choose **This computer** and click **Use this computer**. If the rig cannot be reached when the app starts, the startup screen offers **Use this computer** and the same **Server** settings.

> **Note:** While connected to a rig, the **Audio interface** section of that page does not edit anything: it says that the interface belongs to the rig, which sets it in its settings file (see [Choose the audio interface](#choose-the-audio-interface)).

## MIDI on the rig

A MIDI controller plugged into the rig is found automatically, as on a desktop: the engine listens to every MIDI input through the ALSA sequencer. Set up mappings and footswitches on the **Global** page from any editor connected to the rig. See [MIDI and footswitches](/manual/midi-and-footswitches).

## Looking after the rig

| Task                                          | Command                                                                                              |
| --------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| Is it running?                                | `systemctl status cathode-host cathode-server`                                                       |
| Follow the logs (including audio dropouts)    | `journalctl -u cathode-host -u cathode-server -f`                                                    |
| Restart after changing settings               | `sudo systemctl restart cathode-host cathode-server`                                                 |
| Update                                        | Unpack the newer package and run its `sudo ./install.sh`. Your settings, presets and token are kept. |
| Remove the program, keep settings and presets | `sudo ./uninstall.sh` (from the package folder)                                                      |
| Remove everything                             | `sudo ./uninstall.sh --purge`                                                                        |

The engine runs with real-time priority and memory that is never swapped out, which the installed services set up for you.

### Back up

Your presets, banks, IRs, captures and global settings are in `/var/lib/cathode`. Copy that folder to keep them, for example with `scp` or `rsync` to another computer. You can also export single presets from the Library in the browser.

## Lower latency on Linux

- Use a kernel built for low latency: a `PREEMPT_RT` kernel, or your distribution's low-latency kernel.
- Set the CPU frequency governor to `performance`, so the processor does not slow down between notes.
- Start at `CATHODE_BUFFER=64`, play for a while, and watch the log for dropouts. Raise it to 128, then 256, until there are none.
- Keep other work off the rig: no desktop environment, no other audio programs.

## Security

Anyone who can reach the rig on the network and has the token can change your presets and settings. The connection is plain HTTP, not encrypted.

- Keep the rig on a network you trust: your own router, not venue or public Wi-Fi.
- Keep the token private, and change it if it leaks: edit `Cathode__Token` in the settings file (a long random string) and restart both services. Every editor then asks for the new one.
