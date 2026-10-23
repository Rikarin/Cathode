# Cathode

An open-source software guitar processor in the Axe-Fx III class. Licensed GPL-3.0-or-later.

Planning package for a hardware-free, Axe-Fx III–class guitar processor:
- **C# (.NET 10) engine** configured through a **REST API**
- **Electron + Angular 22 + Tailwind 4 + xUI** editor

| Doc | What's inside |
|---|---|
| [01 — Axe-Fx III feature audit](docs/01-axe-fx-iii-audit.md) | Hardware, grid, all ~44 block types with counts and limits, amp parameter surface, scenes/channels/modifiers, utilities, editor/FC, firmware history through 33.00β; each mapped to a software requirement; MVP cut |
| [02 — Architecture](docs/02-architecture.md) | Process model, repo layout, preset domain model, engine internals (graph compiler, threads, params, switching, I/O, NAM), REST/WS API, editor design with xUI component map |
| [03 — Testing strategy](docs/03-testing-strategy.md) | Test tiers, analysis toolkit, DSP unit/property tests, golden renders, model accuracy (ESR/MRSTFT/ABX), real-time safety, API contracts, presets, frontend, HIL |
| [04 — Implementation plan](docs/04-implementation-plan.md) | Phases 0–9 with exit criteria, Phase 0 feasibility spikes, risk register, open questions, first two weeks |
| [05 — DSP and runtime research](docs/05-dsp-and-runtime-research.md) | Modelling approaches, NAM ecosystem, anti-aliasing, convolution, real-time .NET, audio I/O, latency, sources |

## Key decisions at a glance
1. **Two processes:**
   - an allocation-free **NativeAOT engine host** that owns the audio thread
   - a separate **ASP.NET Core gateway** for REST and WebSocket, so control-plane GCs can't cause dropouts (confirmed in spike S2)
2. **Native audio shim** (ASIO, CoreAudio, JACK) with a single C ABI. Target: **64 samples at 48 kHz, < 6 ms round trip**.
3. **Three-tier amp strategy:**
   - **Capture** (hosted NeuralAmpModelerCore, so Tone3000 NAM A1/A2 models work out of the box). Fractal itself added a NAM block in fw 33β.
   - **Circuit** (our own WDF/DK component models, a multi-month research track)
   - **Hybrid** (neural preamp with parametric power amp)
4. **Grid → compiled DAG** with buffer colouring and latency compensation. Knob, scene and channel changes never recompile. Presets swap RCU-style with spillover.
5. **Metadata-driven blocks:** one descriptor feeds the DSP, REST validation, the generated editor UI and the tests.
6. **Testing foundation:** an offline renderer plus an analysis toolkit (THD, aliasing, null, ESR/MRSTFT). Every block gets a generated suite, including a zero-allocation check.
7. **Editor** built from xUI (dock-manager, omnibar, data-table, echarts, konva, node-graph) plus new `cth-knob` and `cth-meter` components to upstream into xUI.

## Getting started

You need the .NET 10 SDK, Node with pnpm, and CMake with a C and C++20 compiler. Build once:

```bash
cd engine && native/build.sh && dotnet build -c Release
```

```bash
cd editor && pnpm install
```

The native libraries provide audio devices, MIDI and NAM. Without them, Cathode still runs on the null audio backend.

### Desktop app

The Electron app starts the engine host and the Server itself, with a token per run. It starts on the null audio backend. Pick your interface in its audio settings, which also run the loopback latency test.

```bash
cd editor && pnpm exec nx serve desktop
```

### Browser, no audio

This is the quickest loop for UI work. The Server runs the engine in-process:

```bash
cd engine && dotnet run -c Release --project src/Cathode.Server -- --Cathode:Engine:Mode=inprocess
```

```bash
cd editor && pnpm exec nx serve editor
```

### Two processes on a real device

List the audio devices, then start the host on one of them and the Server against its socket:

```bash
cd engine && dotnet run -c Release --project src/Cathode.Host -- --devices
```

```bash
cd engine && dotnet run -c Release --project src/Cathode.Host -- --serve --socket /tmp/cathode-host.sock --device "<uid>"
```

```bash
cd engine && dotnet run -c Release --project src/Cathode.Server -- --Cathode:Engine:SocketPath=/tmp/cathode-host.sock
```

Then run `pnpm exec nx serve editor` as above. The Server listens on `http://127.0.0.1:5180`, with OpenAPI at `/openapi/v1.json`. [`cathode.http`](engine/src/Cathode.Server/cathode.http) walks through the API without the editor.

More detail is in [engine/README.md](engine/README.md) (tests, soak, tokens) and [editor/README.md](editor/README.md) (tests, Storybook, client generation).
