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
