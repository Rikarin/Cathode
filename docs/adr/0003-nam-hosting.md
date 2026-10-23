# ADR-0003: Host NeuralAmpModelerCore natively behind a C ABI (spike S3)

- **Status:** accepted
- **Date:** 2026-10-23
- **Spike:** S3, from [04-implementation-plan.md](../04-implementation-plan.md#phase-0-feasibility-spikes-34-weeks--answer-the--items-before-committing)

## Decision

The capture tier of the Amp block hosts the **reference C++ NeuralAmpModelerCore** rather than a C# port:
- **Native side:** `engine/native/cth_nam` wraps NeuralAmpModelerCore **v0.6.0**, pinned by commit through CMake FetchContent and built as an OBJECT library. A STATIC library would let the linker drop the architectures' self-registering static initialisers; this happened, and every model failed to load with "No config parser registered".
- **C ABI:** `load`, `info`, `reset`, `set_size`, `process`, `free`, `set_fast_tanh`. Mono float in and out; the model runs in NAM's default double precision, converted at the boundary.
- **Managed side:** `Cathode.Dsp.Native` contains `NamModel` (the handle) and `NamAmpBlock`, which has input trim, loudness normalisation to −18 dB as in the NAM plugin, and output level.
- **Graphs:** graph nodes reference model files through a new **assets** map (`"assets": { "model": "x.nam" }`). The compiler loads assets on the control thread before `Prepare`, and reports load failures as graph validation errors.
- **Fast tanh** is on by default in the host benchmark, as in the NAM plugin. It is a process-global switch upstream.

## Correctness

Golden renders come from **upstream's own `render` tool** on a committed, deterministic, guitar-like DI. Cathode reproduces them:

| Reference | Residual vs upstream (relative to signal) |
|---|---|
| A1-Standard WaveNet | −285 dB (bit-identical) |
| A2 Full (SlimmableContainer) | −130 dB |
| A2 Lite (`--slim 0`) | −135 dB |
| LSTM | −109 dB |

The non-identical cases are float rounding between upstream's `-Ofast` tool build and our `-O3` library build. A sensitivity test proves the comparison catches a **0.1 dB** input change.

> **Lesson:** the first version of these goldens used the opening of upstream's `example_audio/input.wav`, which is one second of **digital silence**. Every test passed and proved nothing, and it also produced two false findings: "fast tanh changes the tone by 1.6 % ESR" and "A2-Lite has nothing to do with Full". Golden stimuli must now be checked for level, and a sensitivity test guards the comparison.

On real signal, fast tanh gives ESR 7.7e-6 (A1) and 2.3e-5 (LSTM) against exact tanh, which is inaudible. A2 uses no tanh, so its ESR is 0. A2-Lite against Full gives ESR 0.042.

## Performance (M1 Max, NativeAOT host, 48 kHz / 64 frames, full engine path Input → NAM → Output)

**CPU, % of one core** (median of 3; "upstream" is NAM's own `benchmodel`, which feeds zeros):

| Model | Cathode, DI-level noise | Cathode, silence | Upstream `benchmodel` |
|---|---|---|---|
| A1-Standard, fast tanh | **4.2 %** | 4.1 % | 4.2 % |
| A1-Standard, exact tanh | **10.2 %** | 6.2 % | 6.2 % |
| A2 Full | **4.0 %** | 4.0 % | 3.7 % |
| A2 Lite | **0.57 %** | — | 0.50 % |
| LSTM (small) | **0.24 %** | 0.24 % | 0.21 % |

- **Allocations:** 0 bytes per callback, verified in the benchmark and by test.
- **Live on a device** (Mackie ProFX, CoreAudio, muted, 10 s, no driver overloads at any setting):

  | Model | Buffer | Engine worst callback |
  |---|---|---|
  | A1 | 64 | 9.2 % of budget |
  | A1 | 32 | 11.8 % of budget |
  | A2 | 64 | 8.0 % of budget |
  | A2 | 32 | 8.4 % of budget |

## Findings

1. **The wrapper costs nothing on A1.** It matches upstream within noise. A2 and LSTM run 10–14 % slower than upstream's benchmark, a gap of about 0.35 % of a core for A2. The gap doesn't depend on input or denormals, and LTO didn't close it (tried, no gain, reverted). It remains open, at low priority.
2. **Exact tanh is input-dependent** and costs 65 % more on real signal than upstream's zero-input benchmark suggests. Since fast tanh is inaudible (ESR ≤ 2.3e-5), **keep fast tanh on**.
3. **A2 is not the 30–40 % saving its announcement claimed**, at least for this example model on Apple Silicon: A2 Full is 4 % faster than A1-Standard. **A2 Lite is 7× cheaper than Full** at ESR 0.042, which makes it the right default for a second amp, matching what Fractal's NAM block does on non-Turbo units.
4. **Budget:**

   | Configuration | Cost |
   |---|---|
   | One A1 or A2 Full amp | about 4 % of one core |
   | Two amps plus spillover during a preset change (four models alive) | about 16 % |
   | 32-sample buffers | worst callback stays around 12 % of budget |

   NAM is **not** the bottleneck. Convolution, reverbs and the Circuit tier will be.
5. **No ONNX Runtime, no C# port:** the reference core is cheap enough and gives exact `.nam` compatibility, A2 included, for free.

## Consequences and open items

- **Build:** the native build now downloads NeuralAmpModelerCore and Eigen on first configure. CI needs network access; cache `artifacts/obj` later.
- **Version bumps:** bumping NAM is a deliberate act. Regenerate the goldens with that version's `render` (see `engine/tests/assets/nam/README.md`) and review the residuals.
- **Model sample rate:** models declare a training rate (48 kHz for these). The engine runs at 48 kHz internally, so no resampling is needed. A mismatch isn't handled yet: warn in the UI, or resample.
- **Resizing:** `SetSlimmableSize` is not real-time safe, so Lite/Full switching must build a new block or graph off-thread, which the RCU swap already supports.
- **Still to do:**
  - investigate the 10–14 % A2/LSTM gap with a profiler
  - Windows and Linux numbers once CI runs
  - an AIDA-X/RTNeural wrapper (deferred: NAM covers the Tone3000 library)
