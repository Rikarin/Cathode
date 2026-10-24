# 10. Releases and installation

Cathode ships two ways:

| Package | For | Contents |
|---|---|---|
| **Desktop app** | A Mac, Windows or Linux computer with a display | The editor, Electron and the engine sidecars (cathode-host and cathode-server on loopback). macOS: `.dmg` and `.zip` (Apple Silicon). Windows: NSIS installer (x64). Linux: AppImage (x64). |
| **Headless rig** | A small Linux computer in the rig (Raspberry Pi 5, mini PC) with an audio interface and no display | `cathode-server-<version>-linux-<arch>.tar.gz` (x64, arm64): cathode-host, cathode-server, the editor's web build, two systemd services and an install script. Played from any browser on the network or from the desktop app's remote-server connection. |

## Releasing

Push a version tag: `git tag v1.2.3 && git push origin v1.2.3`. A tag with a suffix (`v1.2.3-rc.1`) is published as a
prerelease. [`release.yml`](../.github/workflows/release.yml) then:

1. Builds the desktop app on macOS, Windows and Linux (`pnpm dist`), each on its own OS since NativeAOT cannot
   cross-compile.
2. Builds the headless package on Linux x64 and Linux ARM64 runners (`packaging/headless/build.sh`), installs it on the
   runner with `install.sh` and checks it: the host passes `--self-test`, the services start, the server reports the
   engine connected, the web editor loads and the API refuses a request without the token. Then it uninstalls it.
3. Publishes a GitHub release with every file and a `SHA256SUMS`, with notes generated from the commits.

Running the workflow by hand (Actions, release, Run workflow) builds everything as workflow artifacts without
publishing. [`desktop.yml`](../.github/workflows/desktop.yml) still builds the desktop app on every push to master.

Signing is optional (see the top of `release.yml`): without certificates, macOS builds are signed ad hoc and Windows
builds are unsigned.

## The headless rig

[`packaging/headless`](../packaging/headless) holds the package: [`README.md`](../packaging/headless/README.md) is the
user's guide (install, choose the interface, connect, logs). In short:

- `install.sh` puts the program in `/opt/cathode`, the settings in `/etc/cathode/cathode.env` (kept on update, with a
  generated access token), presets and assets in `/var/lib/cathode`, and enables `cathode-host` and `cathode-server`.
- `cathode-host` runs as the `cathode` user in the `audio` group with real-time priority (`LimitRTPRIO`) and locked
  memory. `run-host.sh` builds its command line from the settings file (device, rate, buffer, channel maps).
- `cathode-server` listens on every interface (`ASPNETCORE_URLS`), so it needs the token, and serves the editor's web
  build at the root (`Cathode:EditorDirectory`, `Api/EditorHosting.cs`). The page is marked same-origin, so the editor
  talks to the server that served it; it asks for the token on a 401 and remembers it in that browser.

Audio on the rig needs the Linux backend of the native shim (`engine/native/cth_audio`). Without a device the engine
runs on the null backend, which is how CI checks the package.
