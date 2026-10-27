# 03 — Testing strategy

An audio product fails in ways most apps don't. It can **sound wrong** while every test is green. It can **click once an hour** because of a GC pause. And it can **silently change tone** after a refactor. This strategy has a layer for each of those failure modes.

Confidence notes: anything marked *(verify)* came from research that could not be confirmed against a primary source. Check it before you adopt it.

---

## 1. Test pyramid

| Tier | Runs | Contents | Budget |
|---|---|---|---|
| **T1 Fast** | every commit/PR | DSP unit and property tests, golden renders (short), preset schema and migration, API integration (null backend), Vitest units | < 5 min |
| **T2 Medium** | every PR | Playwright Electron E2E, Storybook interaction and visual tests (Chromatic), zero-allocation assertions, OpenAPI drift check, relative perf (PR vs base) | < 15 min |
| **T3 Nightly** | dedicated runner | parameter fuzzing, long property runs, full golden corpus, model-accuracy (ESR/MRSTFT) on all captures, BenchmarkDotNet + callback p99.9, 1 h soak under REST/WS load | hours |
| **T4 Release** | HIL rig + humans | loopback round-trip latency, 30–60 min xrun soak at the smallest buffer per OS × interface, hot-plug and sample-rate change, ABX/MUSHRA listening | per release |

The foundation for everything else is the **OfflineBackend**. It renders a preset or block over a WAV file as fast as possible with a fixed sample rate and block size. It is deterministic and needs no sound card, so it works on any CI runner.

---

## 2. The analysis toolkit (build this first)

`engine/tests/Cathode.Testing` is a shared library, also exposed as the `Cathode.Analyze` CLI:

| Stimuli | Analysers |
|---|---|
| unit impulse, step, DC | FFT magnitude and phase at log-spaced frequencies |
| coherent sine (integer cycles per window) | THD, THD+N, harmonic table H2–H9 |
| Farina exponential sine sweep + inverse filter | linear IR plus separated harmonic IRs |
| log chirp to Nyquist (for aliasing) | **signal-to-aliasing ratio** (energy at folded, non-harmonic bins) |
| pink/white noise (seeded), full-scale square | null residual (dBFS) after cross-correlation alignment |
| **DI corpus**: ~20 guitar and bass DI loops (single-coil, humbucker, 7-string, palm-mute, chords, clean arpeggio) | **ESR** with 1 − 0.95 z⁻¹ pre-emphasis; **MRSTFT** (512/1024/2048); LUFS; latency (impulse delay) |

Everything goes through **one** implementation, so tests, benchmarks and the model-quality pipeline measure the same way.

---

## 3. DSP unit testing (per block)

Every `IDspBlock` gets a parametrised suite generated from its descriptor. Run it at **44.1k, 48k and 96k**, and with block sizes **1, 7, 64, 4096 and random-variable** to catch block-boundary bugs.

1. **Analytic response:** linear blocks (EQ, filters, crossover, tone stack) compare FFT(impulse response) against closed-form H(e^jω).
   - Tolerance: ±0.05 dB in the passband, looser near Nyquist.
   - The passive tone stacks are compared against SPICE/ACME.jl reference curves for the same component values.
2. **Step and DC:** DC gain, overshoot and settling (compressors, envelope follower), and that DC blocking works where the block should block DC.
3. **Harmonic profile snapshot:** nonlinear blocks (drives, amp stages) store a table of the harmonic profile at three drive levels. A change beyond tolerance fails the test, and the snapshot is updated deliberately.
4. **Aliasing floor:** the 1, 2.5, 5 and 8 kHz tones at maximum drive must give a signal-to-aliasing ratio of at least the target, for example **≥ 60 dB with 4× oversampling + ADAA**. A spectrogram PNG is attached as a CI artifact.
5. **Null tests:**
   - bypass and dry must be bit-identical
   - mix 0 % against dry, including latency compensation
   - SIMD against a scalar reference within 1e‑6
   - channel A with the same parameters as channel B must null
6. **Latency contract:** the measured impulse delay must equal `LatencySamples`.
7. **Determinism:**
   - the same render twice, and in two processes, must be bit-equal
   - `Reset()` then a render must equal a fresh instance then a render
8. **Denormals:** a loud burst followed by 10 s of silence must leave no subnormal value in the output or the state, and per-block time must not jump.

### Property-based (CsCheck)

For **every** block, with any in-range parameter vector and any bounded input (noise, DC, full-scale square, silence):
- the output has no NaN or ∞ and stays under a +160 dBFS blow-up ceiling. Legitimate gain can be large (five PEQ bands stacked at +24 dB reach +140 dB), while a truly unstable filter grows exponentially past any fixed level within a render; marginal instabilities are caught by the decay test
- after the input stops, the output decays below −90 dBFS within T. Self-oscillating effects are exempt but must stay bounded
- linear blocks satisfy homogeneity and superposition
- **zipper check:** an instant parameter jump never produces a sample-to-sample step above X
- descriptor round-trip: `Deserialize(Serialize(params)) == params`

### Parameter fuzzing (nightly)

Random automation every block, including extreme values, scene, channel and bypass storms, and preset swaps every N ms. This is the same idea as pluginval's high-strictness levels. It must produce no exceptions, no NaN and no unbounded output.

---

## 4. Golden renders and tone stability

- `assets/test-corpus/` holds the DI files. `tests/golden/` holds short reference renders as float32 WAV, plus JSON summaries.
- Comparison is **tolerance-based**, never byte-equal across operating systems and CPUs, because FMA and JIT codegen differ. The test fails if the **null residual** is above −90 dBFS for linear and time-based effects, or above −60 dBFS for nonlinear ones.
- [Verify](https://github.com/VerifyTests/Verify) handles approval of the JSON summaries and preset files. A custom comparer applies the tolerance to `.wav`.
- Every **factory preset** gets a golden render. Any tone change in a PR is visible and must be acknowledged on purpose. This is how you avoid "the update changed my sound" reports.

---

## 5. Model-accuracy testing (amps, drives, cabs)

| Metric | Use |
|---|---|
| **ESR** (pre-emphasised) | the main regression gate per model. Each model must stay within baseline + ε; no universal threshold, because published results vary by device from ~0.01 to > 0.5 |
| **MRSTFT** | complements ESR; tolerant to tiny phase and alignment differences |
| Harmonic profile and frequency response vs reference | for white-box models against measurements of the real amp |
| **ViSQOL / PEAQ** | trend indicators only, never gates. Their correlation with high-gain guitar perception is unproven |
| **ABX / MUSHRA** (webMUSHRA or an in-app ABX page), LUFS-matched | release gate for new or changed amp models; small listener panel |

Pipeline for our own "Circuit" amps:
1. Re-amp the real amp with the NAM capture signal plus our DI corpus, using a reactive load or real cab plus a fixed mic. Record at several knob settings.
2. Render the same inputs through our model at matching settings.
3. Align by cross-correlation, then compute ESR and MRSTFT per setting. The results feed a dashboard and the nightly regression.

Reference measurements must be **ours**, made on amps we have access to.

---

## 6. Real-time safety

.NET has no RealtimeSanitizer, so we build our own equivalents:

1. **Zero-allocation assertion:** warm up for 1 000 blocks, read `GC.GetAllocatedBytesForCurrentThread()`, run 10 000 blocks, and assert the delta is 0. Run this for every block and for the full graph (T2).
2. **Debug guard:** a `[ThreadStatic] IsAudioThread` flag. In Debug builds the engine checks the allocation counter per callback and logs a stack hint when it moves.
3. **Static analysis:**
   - a Roslyn analyzer such as ClrHeapAllocationAnalyzer *(verify maintenance)*, or our own
   - it applies to `Cathode.Dsp` and to methods marked `[RealtimeSafe]`
   - it bans `lock`, `Monitor`, `async`/`Task`, LINQ, closures, boxing, `params`, string formatting and logging
4. **Native code:** build `cth_audio` and `cth_nam` with Clang **RealtimeSanitizer** (`-fsanitize=realtime`, `[[clang::nonblocking]]`) in a CI job.
5. **GC soak (T3):**
   - run the engine for 1 h with an **aggressive REST and WebSocket load generator** hammering the server: knob storms, preset loads and IR uploads
   - assert **zero gen0 or gen2 collections in the Host process during steady state**, zero xruns on the NullBackend, and a callback p99.9 below 50 % of budget
   - this is also how Phase 0 checks the process-split decision
6. **Callback timing:** a preallocated histogram of `Stopwatch.GetTimestamp()` deltas for every callback, exposed via `/system` and telemetry. CI reports p50, p99, p99.9 and max. Absolute gates run only on the pinned perf runner; shared runners use relative gates.

---

## 7. Performance

- **BenchmarkDotNet** with `[MemoryDiagnoser]` per block and for reference presets: "clean", "crunch", "high-gain + 2 delays + reverb", and "worst case: 4 amps, 4 cabs, everything".
- Results are reported as **% of the real-time budget** (64 / 48 000 = 1.33 ms), with history tracked through github-action-benchmark or a similar tool. Alert on a regression above 10 %.
- **CPU meter parity:** the engine's own CPU % (callback time divided by budget) is validated against the benchmark numbers. Users will trust this number the way they trust the Axe-Fx CPU meter, so it must be right.

---

## 8. API and contract testing

- **ASP.NET Core integration tests** with `WebApplicationFactory<Program>`. The audio backend is replaced by `OfflineBackend` or `NullBackend` via `ConfigureTestServices`, using an in-process engine.
- **Out-of-process IPC tests:** start the real Host binary with the NullBackend and run the same suite through `IpcEngineControl`. The suite is shared, the transport differs.
- **OpenAPI drift:**
  - the build regenerates `openapi.json`, and a test diffs it against the committed copy
  - **oasdiff** fails PRs on breaking changes unless the API version is bumped
  - the **TypeScript client** in `editor/libs/api-client` is regenerated in CI (openapi-typescript + openapi-fetch, wrapped in Angular services), and the build fails if the committed client is stale
- **WebSocket tests:** subscribe and unsubscribe, event ordering (a REST mutation is followed by its `paramChanged` event), throttle (≤ 60 Hz), reconnect and resync from `revision`, and multiple clients in sync.
- **Concurrency:** two clients PATCH with a stale `If-Match`, so one gets `412`, and live state is never corrupted.
- Pact is optional. It becomes worthwhile once third-party clients (mobile app, controller bridges) consume the API.

---

## 9. Preset compatibility

- Every preset has a `schemaVersion`. Migrations are pure `vN → vN+1` functions, each with its own unit test.
- **Legacy corpus:** `tests/presets/legacy/` holds presets from every released version, including user-contributed ones. CI migrates each one to the current version and then:
  1. checks it deserialises with no unknown fields
  2. approval-tests the migrated JSON
  3. **renders it and nulls the result against the stored golden render**, so a migration cannot change the sound
- Idempotence: migrating a current-version preset changes nothing.
- A **newer-version preset** loaded into an older build fails with a clear error and never crashes.
- A test fails if any `(typeId, paramId)` disappears from the catalog without a migration entry.

---

## 10. Frontend testing

| Layer | Tooling | Focus |
|---|---|---|
| Unit and component | **Vitest** (Angular CLI default since v21) + Angular Testing Library; zoneless, so `await fixture.whenStable()` | stores, reducers (event → state), knob math (taper, fine-drag), throttling |
| Component library | **Storybook** stories for `cth-knob`, `cth-meter`, `cth-grid` and others; interaction tests through `play`; **Chromatic** visual diffs in light and dark (the same setup xUI already uses) | visual regressions, states |
| Accessibility | Storybook a11y addon + `@axe-core/playwright` | knobs need `role="slider"`, `aria-valuenow`, `aria-valuetext` ("−6.0 dB") and arrow, PageUp/Down and Home/End keys |
| E2E | **Playwright `_electron.launch`**: real Electron + real Host/Server sidecars on the NullBackend; `xvfb-run` on Linux | build a preset from scratch, scenes and channels, modifier assignment, save, reload, undo/redo, IR upload, a remote-server connection |
| Perf | Playwright trace + a custom FPS probe | the grid at 6×14 full with meters at 60 Hz stays ≥ 55 fps; no CD cycles triggered by telemetry |

*(verify)* that the Storybook Vitest addon supports Angular. If it does not, use the Storybook test-runner (Playwright).

---

## 11. Hardware-in-the-loop and listening (T4)

- **Rig:** one machine per OS with 2–3 interfaces: an RME (reference), a Focusrite (common) and a cheap class-compliant interface (worst case). The output is looped back to the input.
- **Latency test:** the app's latency test (the desktop app's audio setup; `GET /system/latency` reports the same device, buffer and graph latency) plays an impulse or MLS, cross-correlates, and reports the round trip. Cross-check it with Oblique RTL Utility. Record the results per release in a latency table.
- **Soak:** 60 min at the smallest stable buffer with a reference high-gain preset and scene changes every 2 s. The pass criterion is **0 xruns**.
- **Robustness:** device unplug and replug, sample-rate change, sleep and wake. The engine recovers with no crash and no stuck notes.
- **Listening:** blind ABX of new models against reference recordings, plus a "tone didn't change" check of factory presets against the previous release.

---

## 12. Definition of done (any block or feature)

- [ ] Descriptor complete: IDs, ranges, units, tapers, smoothing kind, latency
- [ ] Generated suite passes: analytic response, null, determinism, denormal, CsCheck properties
- [ ] Zero allocations after `Prepare`, verified by test
- [ ] Benchmark added; CPU cost recorded in the catalog for the CPU estimator
- [ ] Golden render(s) added
- [ ] Generic editor works; custom panel (if any) has a Storybook story and a11y check
- [ ] Preset migration added if any parameter changed
