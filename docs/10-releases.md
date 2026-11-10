# 10. Releases and installation

Cathode ships two ways:

| Package | For | Contents |
|---|---|---|
| **Desktop app** | A Mac, Windows or Linux computer with a display | The editor, Electron and the engine sidecars (cathode-host and cathode-server on loopback). macOS: `.dmg` and `.zip` (Apple Silicon). Windows: NSIS installer (x64). Linux: AppImage (x64). Extra targets, see [Desktop targets](#desktop-targets): Intel Macs, Windows on ARM, ARM64 Linux, and a `.deb` on Linux. |
| **Headless rig** | A small Linux computer in the rig (Raspberry Pi 5, mini PC) with an audio interface and no display | `cathode-server-<version>-linux-<arch>.tar.gz` (x64, arm64): cathode-host, cathode-server, the editor's web build, two systemd services and an install script. Played from any browser on the network or from the desktop app's remote-server connection. |

## Releasing

Push a version tag: `git tag v1.2.3 && git push origin v1.2.3`. A tag with a suffix (`v1.2.3-rc.1`) is published as a
prerelease. [`release.yml`](../.github/workflows/release.yml) first checks that the tag points at a commit on master,
and builds nothing if it does not. Then it:

1. Builds the desktop app on macOS, Windows and Linux (`pnpm dist`), each on its own OS and CPU since NativeAOT cannot
   cross-compile (see [Desktop targets](#desktop-targets)), and starts the packaged app on its runner (`CATHODE_PACKAGED=1`, `packaged.spec.ts` and
   `sidecars.spec.ts`): it runs the engine in its `resources/engine`, the server reports the engine connected and
   serves the editor, and quitting leaves no engine process behind.
   On Linux the engine is not built by these jobs: the `linux-engine` job links it in a Debian 12 container for x64
   and ARM64, and they package that one (see [The oldest Linux it runs on](#the-oldest-linux-it-runs-on)).
2. Builds the headless package on Linux x64 and Linux ARM64 runners (`packaging/headless/build.sh`), installs it on the
   runner with `install.sh` and checks it: the host passes `--self-test`, the services start, the server reports the
   engine connected, the web editor loads and the API refuses a request without the token. Then it uninstalls it
   with `--purge` and checks that nothing is left.
3. Runs the tagged commit's tests: [`engine.yml`](../.github/workflows/engine.yml) (the engine tests on three OSes and
   the NativeAOT host's self-test) and [`editor.yml`](../.github/workflows/editor.yml) (the generated API client,
   type-check, lint, unit tests, Storybook and the desktop app's Playwright tests), called as reusable workflows.
4. Only when all of that passes, publishes a GitHub release with every file, a `SHA256SUMS` and its attestation bundle
   `SHA256SUMS.sigstore.json`, with notes generated from the commits.

Each build job hashes its installers where it built them and attests their build provenance (SLSA, signed keyless
through Sigstore with the job's GitHub identity, `actions/attest-build-provenance`). The release job checks every
downloaded file against those build-time hashes, merges them into `SHA256SUMS`, refuses a file that has none, and
attests `SHA256SUMS` itself.

### Desktop targets

The desktop app is built for six OS and CPU pairs, each on a runner of its own (NativeAOT cannot cross-compile, and
`publish-engine.mjs` and `package.mjs` build for the machine they run on):

| Target | Runner | Files | A release waits for it |
|---|---|---|---|
| macOS, Apple Silicon | `macos-latest` | `Cathode-<version>-mac-arm64.dmg`, `.zip` | yes |
| Windows x64 | `windows-latest` | `Cathode-<version>-win-x64.exe` | yes |
| Linux x64 | `ubuntu-24.04` | `Cathode-<version>-linux-x86_64.AppImage` | yes |
| macOS, Intel | `macos-15-intel` | `Cathode-<version>-mac-x64.dmg`, `.zip` | no |
| Windows ARM64 | `windows-11-arm` | `Cathode-<version>-win-arm64.exe` | no |
| Linux ARM64 | `ubuntu-24.04-arm` | `Cathode-<version>-linux-arm64.AppImage` | no |

The first three are the supported targets of 1.0: a release is not published unless each builds and passes the
packaged-app tests. The other three are extra: they run the same steps and the same tests (`continue-on-error` on
their matrix legs in `release.yml` and `desktop.yml`), their files are published, hashed and attested like the rest
when the leg passes, and a leg that fails leaves the release without that target instead of holding it back. They
have run nowhere but on those runners, and nobody has played through them on real hardware yet; the README and the
manual say so. To make one a supported target, remove `extra: true` from its leg in both workflows once it has been
passing on master and someone has used it with an interface. GitHub offers the Intel Mac runner until August 2027.

**The `.deb`.** On Linux `package.mjs` also builds a `.deb` (`Cathode-<version>-linux-amd64.deb`, `-arm64.deb`) when
`CATHODE_DEB_MAINTAINER` is set to `Name <name@host>`. A Debian package must name a maintainer with an address, and
the project has none of its own, so the workflows read it from the repository variable of that name (Settings ▸
Secrets and variables ▸ Actions ▸ Variables): set it once and every later build carries the `.deb`; leave it unset
and Linux gets the AppImage only. The package installs to `/opt/Cathode`, adds the menu entry, the icon and the
`.cthpreset` association, and depends on `libc6 (>= 2.36)` and `libasound2` for the engine besides Electron's own
libraries. There is no `.rpm`.

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
publishing. Such a run can be started on any branch, so its builds get no signing secrets and no attestation. [`desktop.yml`](../.github/workflows/desktop.yml) still builds the desktop app on every push to master,
as a snapshot (it sets `CATHODE_VERSION` to the default version plus the commit, `1.2.3+abc1234`), and on a pull request that changes the desktop app's main process, its packaging scripts or the native build.
A pull request that changes the engine's sources runs `editor.yml` too, for its Playwright tests of the desktop app
against the real engine.

### Signing

Both packages carry Cathode's `LICENSE` and a `licenses` folder with
[`THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md) and the licence texts of the third-party code in the binaries
(that file says where each comes from). The desktop app opens the folder from Help ▸ Open Source Licences.

Signing is optional (see the top of `release.yml`): without certificates, macOS builds are signed ad hoc and Windows
builds are unsigned.

The certificate and the Apple credentials are worth protecting from code that nobody reviewed, so the workflows hand
them out narrowly:

- `release.yml` builds nothing for a tag that is not on master, and passes the secrets to the desktop build only on a
  release run (a `v*` tag on master). A manual run gets none.
- `desktop.yml` passes them only when it builds master. A pull request or a manual run on another branch gets none.

Those rules live in the workflow files, and a branch can carry its own copy of a workflow file. What a branch cannot
change is a GitHub environment, so when you add the signing secrets, put them there and not in the repository's
secrets:

1. Settings, Environments, `release` (the first release run creates it; `release.yml`'s desktop jobs run in it on a
   release run).
2. Deployment branches and tags: selected, with the tag rule `v*`. Add required reviewers if a release should wait for
   an approval.
3. Add `CSC_LINK`, `CSC_KEY_PASSWORD`, `APPLE_ID`, `APPLE_APP_SPECIFIC_PASSWORD` and `APPLE_TEAM_ID` as environment
   secrets, and do not keep them as repository secrets.

With the secrets in the environment only, `desktop.yml`'s builds of master are unsigned, which is all a workflow
artifact needs.

### Versions

The tag is the version. `release.yml` passes it on as `CATHODE_VERSION`, and every build takes it from there:

- the desktop installers: `editor/apps/desktop/scripts/package.mjs`;
- the headless package: `packaging/headless/build.sh`;
- the engine's assemblies, which the host's `version`, `/api/v1/system` and the host's hello report:
  `engine/Directory.Build.props`.

Without a tag, builds use the defaults in `editor/package.json` and `engine/Directory.Build.props`. Keep the two equal
(`VersioningTests` checks), and move them to the coming release when you tag it.

Before tagging:

1. Add the release's section to [`CHANGELOG.md`](../CHANGELOG.md).
2. Add the release's folder to the legacy corpus (`engine/tests/assets/legacy/<version>/`,
   [03-testing-strategy.md](03-testing-strategy.md) §9): build the commit you are about to tag, start its Server on an
   empty data folder and run `engine/tests/assets/legacy/make-corpus.mjs` against it, then record the folder's levels.
   The script's header has the commands. `LegacyCorpusTests` compares the folders with the changelog's release
   headings, so the tagged commit's tests fail without it. If the release changes a stored schema, add its line to
   `LegacyCorpusTests.Schemas`.

Also before tagging, move Electron to the newest patch of its line (`pnpm outdated electron` in `editor/`, then
`pnpm update electron`): `package.mjs` packages the version the lockfile resolves, and Electron's patch releases are
how Chromium and Node security fixes reach the app.

### The .NET runtime in a release

CI installs exactly the SDK that [`engine/global.json`](../engine/global.json) names: `dotnet-install --jsonfile`
reads `sdk.version` and ignores `rollForward`, which only chooses among the SDKs on a developer's machine.
cathode-server is published self-contained and cathode-host as NativeAOT, so both carry the .NET runtime and ASP.NET
Core that SDK came with. Microsoft's monthly servicing releases are mostly security fixes, many of them in Kestrel,
and cathode-server listens on the network, so a release is built with the current one:

- Before tagging, set `sdk.version` in `engine/global.json` to the latest SDK of its .NET version
  (<https://dotnet.microsoft.com/download/dotnet/10.0>). Dependabot opens a pull request when one is out
  ([`.github/dependabot.yml`](../.github/dependabot.yml), the `dotnet-sdk` ecosystem).
- `release.yml` checks it on a tag
  ([`sdk-is-current.sh`](../.github/actions/setup-dotnet/sdk-is-current.sh)): a tag whose `global.json` is behind
  the latest SDK is not published, and the message names the version to set.

### The preset format

Presets carry `schemaVersion` (`Preset.CurrentSchemaVersion`). It is 2 since 1.0.0-rc.4; schema 1 is the format of
1.0.0-rc.1 to rc.3, and a preset without the field is schema 1. Every place that reads a stored preset passes its JSON
through `PresetMigrations` first: the library files, snapshots, templates and `.cthpreset` imports. So a preset saved
by an earlier release keeps loading.

Parameter values are stored sparsely, so new parameters, block types and amp models need no new schema. Only a change
that would make an older file read wrongly does. That covers a renamed, re-ranged or re-meant parameter, or a moved
field. For such a change:

1. Raise the version.
2. Append the step that upgrades the previous schema.
3. Add a test with a file of the previous schema.

Released steps are never edited. A preset from a newer release is refused on load and import with a message that
says so.

## Dependencies

[Dependabot](../.github/dependabot.yml) opens the update pull requests every Monday: the editor's and the website's npm
packages, the engine's NuGet packages and the actions the workflows pin by commit. Minor and patch updates of one
ecosystem come in one pull request, a major update in its own, and Angular, Nx and Storybook each as a family. The
test workflows run on each of them.

[`audit.yml`](../.github/workflows/audit.yml) runs every night and on pull requests that change a manifest or
lockfile. It fails on a known vulnerability in a package that ships (`pnpm audit --prod` in `editor/` and `website/`,
`dotnet list package --vulnerable --include-transitive` in `engine/`). Advisories against the development tool chain
(Storybook, Vite, Nx) are listed in the job's summary without failing it.

The native libraries are not covered: NeuralAmpModelerCore and the ASIO SDK are pinned (by commit, by URL and hash) in
`engine/native/*/CMakeLists.txt` and moved by hand ([ADR-0003](adr/0003-nam-hosting.md)).

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

### The oldest Linux it runs on

The NativeAOT host and the native shims bind to the C library of the system that links them, so that system sets the
minimum. A release links them on **Debian 12** ("bookworm"), which makes the minimum **glibc 2.36**: Debian 12 and
Raspberry Pi OS based on it, Ubuntu 24.04, Fedora 37, and everything newer. Debian 12 is what most Raspberry Pi 5s and
many mini PCs still run, and it is the oldest system whose own packages build the engine: its CMake (3.25) and GCC
(12, for the C++20 of NeuralAmpModelerCore) are new enough, where Ubuntu 22.04 (glibc 2.35) would need both from
elsewhere. Ubuntu 22.04 and Debian 11 are not supported.

How `release.yml` does it:

- The `linux-engine` job runs in a `debian:12` container, once on an x64 runner and once on an ARM64 one (NativeAOT
  cannot cross-compile). It runs `publish-engine.mjs`, checks the result, runs the host's self-test there, on Debian
  12's C library, and hands the engine on as a workflow artifact.
- The `headless` job and the Linux leg of the `desktop` job take that engine instead of building their own
  (`CATHODE_ENGINE_PREBUILT=1`, read by `publish-engine.mjs`) and package it on an `ubuntu-24.04` runner, where
  systemd, Electron and Playwright are at hand for the install check and the packaged-app tests.
- [`packaging/glibc-floor.sh`](../packaging/glibc-floor.sh) reads the newest glibc version the binaries ask for and
  fails the release when it is above 2.36. It runs where the engine is built and again on each package.
- `cth_nam` links its C++ runtime statically, so no `libstdc++` version comes on top, and cathode-server uses
  Microsoft's own prebuilt runtime, which asks for less.
- `install.sh` runs `cathode-host --version` and `ldd` on the shims before it installs anything, and says what is
  missing.

Only releases are built this way. A snapshot from `desktop.yml`, or a package built by hand with
`packaging/headless/build.sh`, carries an engine linked on the machine that built it and needs that machine's C
library or newer.

To change the minimum, change the container image in `linux-engine` and the number in the workflow, `install.sh`,
the READMEs and the manual. The image must bring CMake 3.24, a C++20 compiler and Clang (for NativeAOT's link).

Audio on the rig needs the Linux backend of the native shim (`engine/native/cth_audio`). Without a device the engine
runs on the null backend, which is how CI checks the package.
