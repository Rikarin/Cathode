# Cathode editor

The desktop editor (docs/02-architecture.md §7): an Angular 22 renderer styled with [xUI](https://xuijs.org) and Tailwind 4, in an Electron shell that runs the engine (cathode-host and cathode-server) as sidecars.

| Project           | Role                                                                                                                                                                                               |
| ----------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `apps/editor`     | The renderer: grid editor, block pages and panels, preset library, audio setup                                                                                                                     |
| `apps/desktop`    | Electron main (sidecar supervision, `app://` scheme with CSP) and the preload bridge; Playwright E2E in `e2e/`                                                                                     |
| `libs/api-client` | Types generated from `engine/src/Cathode.Server/openapi.json`, plus a typed `fetch` client                                                                                                         |
| `libs/audio-ui`   | `cth-knob`, `cth-meter`, `cth-tuner`, `cth-scope`, `cth-speaker-cone` (DynaCab mic placement) and `cth-block-tile` (with the block icons): the controls xUI does not have, with Storybook stories (`nx storybook audio-ui`) that must pass axe |

## Develop

```bash
pnpm install
```

Run the editor in a browser against a development Server, with an in-process engine and no audio:

```bash
dotnet run -c Release --project ../engine/src/Cathode.Server -- --Cathode:Engine:Mode=inprocess
```

```bash
pnpm exec nx serve editor
```

Or run the whole desktop app. Build the engine first with `dotnet build -c Release` in `engine/`:

```bash
pnpm exec nx serve desktop
```

## Test

```bash
pnpm exec nx run-many -t test
```

```bash
pnpm exec nx e2e desktop
```

```bash
pnpm exec nx test-storybook audio-ui
```

After an API change, regenerate the client. CI fails while it is stale:

```bash
pnpm exec nx run api-client:generate
```

## Package

Build the desktop app for this machine's OS into `dist/release`: a `.dmg` and `.zip` on macOS, an NSIS installer on Windows, an AppImage on Linux. It publishes the engine too (NativeAOT host, self-contained server, native shims), so it needs the .NET SDK, CMake and a C++ compiler:

```bash
pnpm dist
```

NativeAOT does not cross-compile, so each OS builds its own. The `desktop` workflow builds all three on every push to master and attaches them to a GitHub release for `v*` tags. Builds are unsigned (ad hoc on macOS) unless the signing secrets it lists are set.

## Security model

- **Renderer.** It is sandboxed with context isolation and no Node.
- **Preload.** It exposes only `window.cathode` (connection, server target, audio settings, latency test, and the platform and full-screen state for the macOS title bar), and main validates every argument.
- **Serving the editor.** It comes from the `app://` scheme with a CSP that allows connections only to the Server in use. Navigation, new windows and permission requests are refused.
- **Sidecars.** They listen on loopback with a random per-run token. A remote Server needs its own token, and the Server refuses to listen beyond localhost without one.
