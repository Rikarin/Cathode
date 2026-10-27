# Cathode engine

The .NET 10 audio engine. The design is described in [docs/02-architecture.md](../docs/02-architecture.md).

| Project | Role |
|---|---|
| `src/Cathode.Dsp` | Pure DSP: block contract, parameter bank and smoothing, biquad and TPT state-variable filters, built-in blocks, AIDA-X capture models (managed LSTM/GRU), block registry |
| `src/Cathode.Engine` | Grid model (JSON), graph compiler (pruning, ordering, buffer reuse, latency compensation), SPSC command ring, real-time loop with RCU graph swap, callback stats, shared-memory telemetry region |
| `src/Cathode.Audio` | WAV I/O, offline renderer, null (device-less, real-time paced) backend, native device sessions, latency probe |
| `src/Cathode.Dsp.Native` | Blocks backed by native code: `NamAmpBlock`, the capture amp with Neural Amp Modeler models ([ADR-0003](../docs/adr/0003-nam-hosting.md)) as well as AIDA-X ones |
| `native/cth_audio` | C shim: CoreAudio HAL IOProc (macOS), WASAPI and ASIO (Windows; ASIO through Steinberg's SDK, fetched by the build), ALSA `hw:` and JACK (Linux; needs `libasound2-dev` to build), with CoreMIDI, WinMM and ALSA sequencer MIDI, and an FTZ/DAZ helper |
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

Unit tests of the shim's device-independent parts (ASIO sample conversion and driver-list parsing):

```bash
ctest --test-dir artifacts/obj/native -C Release --output-on-failure
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

Measure an impulse response: a sine sweep plays on outputs 1 and 2, input 1 records, and the trimmed IR is written as a WAV. The desktop app's IRs page runs the same command:

```bash
dotnet run -c Release --project src/Cathode.Host -- --ir-capture --device "<uid>" --level -18 --out cab.wav
```

The FX Loop block sends to and returns from spare interface channels. Name them when serving:

```bash
dotnet run -c Release --project src/Cathode.Host -- --serve --socket /tmp/cathode-host.sock --device "<uid>" --loop-out 3,4 --loop-in 3,4
```

Every block registered in `BlockRegistry.CreateDefault()` automatically runs the conformance suite:
- descriptor consistency
- finite, bounded output under random parameters, stimuli and parameter jumps
- determinism and reset equivalence
- block-size independence
- zero allocation
- decay to silence with no subnormals
- a recorded CPU cost

Each block also has a golden render in `tests/assets/golden`. After an intended change to a block's sound, regenerate them and review the diff:

```bash
CATHODE_UPDATE_GOLDEN=1 dotnet test --project tests/Cathode.Dsp.Tests
```

After adding or changing a block, measure the costs again. The catalog serves them to the editor's CPU estimate:

```bash
dotnet run -c Release --project tools/Cathode.Bench -- --write
```

## Running the API

Two processes ([ADR-0001](../docs/adr/0001-process-model.md)). Start the engine host on a device, or with `--null-audio`:

```bash
dotnet run -c Release --project src/Cathode.Host -- --serve --socket /tmp/cathode-host.sock --null-audio
```

Then start the Server, which listens on `http://127.0.0.1:5180` with OpenAPI at `/openapi/v1.json`:

```bash
dotnet run -c Release --project src/Cathode.Server -- --Cathode:Engine:SocketPath=/tmp/cathode-host.sock
```

The host writes meters, the CPU histogram and xrun counts to a memory-mapped file next to its socket (`<socket>.<pid>.telemetry`, or `--telemetry <path>`) and names it in its `hello` reply. The Server maps it read-only and sends meters to WebSocket clients at about 30 Hz. IPC telemetry (tuner, GC counts, device timing) is sent at 10 Hz.

Set `Cathode:Token` (or the `CATHODE__TOKEN` environment variable) to require `Authorization: Bearer <token>` on every request, and `?access_token=` on `/ws`. The Server will not listen beyond localhost without a token. Without one, only loopback origins pass CORS. The desktop app (`editor/`) starts both processes itself, with a token per run.

A launcher that wants its children to go when it goes starts them with standard input on a pipe and `--watch-stdin` (host) or `--Cathode:WatchStdin=true` (Server): each then stops cleanly when the pipe closes, which the OS does however the launcher ends, a crash or a force quit included. The desktop app does this.

`src/Cathode.Server/cathode.http` walks through building a rig with channels and scenes. If an API change is intended, regenerate the committed OpenAPI document:

```bash
CATHODE_UPDATE_OPENAPI=1 dotnet test --project tests/Cathode.Server.Tests
```

## Soak test

The production layout runs under editor-and-gig load: the NativeAOT host on a device (muted), the Release server, knob drags, scene changes, preset loads, uploads and a WebSocket client. The runner publishes everything first and writes `soak.jsonl` and `summary.txt`. It passes with zero driver overloads and zero API errors, and reports the host's GC count and native callback timing.

```bash
tools/Cathode.Soak/run-soak.sh "<device uid>" 60 ./soak-results 0
```

The last argument is the most host GCs allowed (`-1` disables that gate). Use `null` as the device to run without an audio interface. On the null backend, callback timing means little: the backend thread sleeps while it is ahead of real time and catches up in bursts, so it doesn't burn a core. Use a device for timing.

`.github/workflows/soak.yml` runs it nightly on GitHub's hosted Linux runners with the null backend. It can also be started manually with a chosen length. Real-hardware soaks run on a self-hosted macOS runner labelled `audio` when the repository variable `SOAK_DEVICE` holds the device uid.

