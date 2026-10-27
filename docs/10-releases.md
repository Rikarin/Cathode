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
3. Runs the tagged commit's tests: [`engine.yml`](../.github/workflows/engine.yml) (the engine tests on three OSes and
   the NativeAOT host's self-test) and [`editor.yml`](../.github/workflows/editor.yml) (the generated API client,
   type-check, lint, unit tests, Storybook and the desktop app's Playwright tests), called as reusable workflows, and
   checks that the tag points at a commit on master.
4. Only when all of that passes, publishes a GitHub release with every file, a `SHA256SUMS` and its attestation bundle
   `SHA256SUMS.sigstore.json`, with notes generated from the commits.

Each build job hashes its installers where it built them and attests their build provenance (SLSA, signed keyless
through Sigstore with the job's GitHub identity, `actions/attest-build-provenance`). The release job checks every
downloaded file against those build-time hashes, merges them into `SHA256SUMS`, refuses a file that has none, and
attests `SHA256SUMS` itself.

### Verifying a download

With the GitHub CLI, either the installer itself or `SHA256SUMS` and then the installer against it:

```sh
gh attestation verify Cathode-1.2.3-mac-arm64.dmg --repo Rikarin/cathode
gh attestation verify SHA256SUMS --repo Rikarin/cathode --bundle SHA256SUMS.sigstore.json
sha256sum --check --ignore-missing SHA256SUMS
```

Either proves the file was built by this repository's `release.yml` from the tagged commit, not uploaded afterwards.
(`--bundle` checks against the published bundle; without it `gh` fetches the attestation from GitHub.)

Running the workflow by hand (Actions, release, Run workflow) builds everything as workflow artifacts without
publishing. [`desktop.yml`](../.github/workflows/desktop.yml) still builds the desktop app on every push to master.

Signing is optional (see the top of `release.yml`): without certificates, macOS builds are signed ad hoc and Windows
builds are unsigned.

### Versions

The tag is the version. `release.yml` passes it on as `CATHODE_VERSION`, and every build takes it from there:

- the desktop installers: `editor/apps/desktop/scripts/package.mjs`;
- the headless package: `packaging/headless/build.sh`;
- the engine's assemblies, which the host's `version`, `/api/v1/system` and the host's hello report:
  `engine/Directory.Build.props`.

Without a tag, builds use the defaults in `editor/package.json` and `engine/Directory.Build.props`. Keep the two equal
(`VersioningTests` checks), and move them to the coming release when you tag it.

Before tagging, add the release's section to [`CHANGELOG.md`](../CHANGELOG.md).

### The preset format

Presets carry `schemaVersion` (`Preset.CurrentSchemaVersion`, 1 since 1.0). Every place that reads a stored preset
passes its JSON through `PresetMigrations` first: the library files, snapshots, templates and `.cthpreset` imports.
So a preset saved by an earlier release keeps loading.

Parameter values are stored sparsely, so new parameters, block types and amp models need no new schema. Only a change
that would make an older file read wrongly does. That covers a renamed, re-ranged or re-meant parameter, or a moved
field. For such a change:

1. Raise the version.
2. Append the step that upgrades the previous schema.
3. Add a test with a file of the previous schema.

Released steps are never edited. A preset from a newer release is refused on load and import with a message that
says so.

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
