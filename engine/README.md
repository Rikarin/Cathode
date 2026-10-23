# Cathode engine

The .NET 10 audio engine. The design is described in [docs/02-architecture.md](../docs/02-architecture.md).

| Project | Role |
|---|---|
| `src/Cathode.Dsp` | Pure DSP: block contract, parameter bank and smoothing, biquad and TPT state-variable filters, built-in blocks, block registry |
| `src/Cathode.Engine` | Grid model (JSON), graph compiler (pruning, ordering, buffer reuse, latency compensation), SPSC command ring, real-time loop with RCU graph swap, callback stats |
| `src/Cathode.Audio` | WAV I/O, offline renderer, null (device-less, real-time paced) backend, native device sessions, latency probe |
| `src/Cathode.Dsp.Native` | Blocks backed by native code: `NamAmpBlock` (Neural Amp Modeler, [ADR-0003](../docs/adr/0003-nam-hosting.md)) |
| `native/cth_audio` | C shim: CoreAudio HAL IOProc backend (Windows and Linux are stubs for now), FTZ/DAZ helper |
| `native/cth_nam` | C ABI over NeuralAmpModelerCore v0.6.0 (pinned and fetched by CMake) |
| `src/Cathode.Host` | NativeAOT engine executable: `--serve` for the Server, plus diagnostics (`--self-test`, `--audio-test`, …) |
| `src/Cathode.Contracts` | Preset schema and API DTOs (no engine dependencies) |
| `src/Cathode.Ipc` | Server↔Host protocol: length-prefixed JSON over a Unix domain socket |
| `src/Cathode.EngineHost` | Host-side request handler, socket server, in-process link for tests |
| `src/Cathode.Server` | ASP.NET Core REST + WebSocket API: presets, scenes, channels, undo, assets ([ADR-0004](../docs/adr/0004-state-ownership.md)) |
| `tools/Cathode.Render` | `cathode-render --graph g.json --in di.wav --out out.wav` |
| `tests/Cathode.Testing` | Stimuli and analysers: FFT, DTFT magnitude, THD, null residual, ESR, allocation probe |
| `tests/*.Tests` | xUnit v3 + CsCheck suites, including the per-block conformance suite |

Build the native libraries first (CMake, plus a C and C++20 compiler). The first run downloads
NeuralAmpModelerCore and Eigen. Without them, the device and NAM tests skip and everything else still works:

```bash
native/build.sh
```

```bash
dotnet test --solution Cathode.slnx
```

Device diagnostics (macOS for now; see [ADR-0002](../docs/adr/0002-native-audio-backend.md)):

```bash
dotnet run --project src/Cathode.Host -- --devices
```

```bash
dotnet run --project src/Cathode.Host -- --audio-test --buffer 64 --seconds 10
```

CPU cost of a NAM model, or of a whole rig, through the engine. `examples/` holds two rigs and a synthetic cab IR:

```bash
dotnet run -c Release --project src/Cathode.Host -- --bench-nam --model artifacts/native/nam-models/A2.nam
```

```bash
dotnet run -c Release --project src/Cathode.Host -- --bench-nam --graph examples/high-gain.json
```

```bash
dotnet publish src/Cathode.Host -c Release -r osx-arm64 -o out && ./out/cathode-host --self-test
```

Every block registered in `BlockRegistry.CreateDefault()` automatically runs the conformance suite:
- descriptor consistency
- finite, bounded output under random parameters, stimuli and parameter jumps
- determinism and reset equivalence
- block-size independence
- zero allocation
- decay to silence with no subnormals

## Running the API

Two processes ([ADR-0001](../docs/adr/0001-process-model.md)). Start the engine host on a device, or with `--null-audio`:

```bash
dotnet run -c Release --project src/Cathode.Host -- --serve --socket /tmp/cathode-host.sock --null-audio
```

Then start the Server, which listens on `http://127.0.0.1:5180` with OpenAPI at `/openapi/v1.json`:

```bash
dotnet run -c Release --project src/Cathode.Server -- --Cathode:Engine:SocketPath=/tmp/cathode-host.sock
```

`src/Cathode.Server/cathode.http` walks through building a rig with channels and scenes. If an API change is intended, regenerate the committed OpenAPI document:

```bash
CATHODE_UPDATE_OPENAPI=1 dotnet test --project tests/Cathode.Server.Tests
```

## Soak test

The production layout runs under editor-and-gig load: the NativeAOT host on a device (muted), the Release server, knob drags, scene changes, preset loads, uploads and a WebSocket client. The runner publishes everything first and writes `soak.jsonl` and `summary.txt`. It passes with zero driver overloads and zero API errors, and reports the host's GC count and native callback timing.

```bash
tools/Cathode.Soak/run-soak.sh "<device uid>" 60 ./soak-results
```

