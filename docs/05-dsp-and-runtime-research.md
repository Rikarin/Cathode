# 05 — DSP and .NET runtime research notes

This is the research behind the decisions in [02-architecture.md](02-architecture.md). Items marked *(verify)* could not be confirmed against a primary source and must be checked during Phase 0.

## 1. Amp and effects modelling approaches

| Approach | Strengths | Weaknesses | Where we use it |
|---|---|---|---|
| **DK-method / nodal state-space** (Yeh, Abel & Smith 2010; Holters & Zölzer 2015, ACME.jl) | Physically exact; every knob behaves like the real one | Newton iterations; stiff circuits; roughly O(n³) per iteration | Power amp and transformer, complex preamps (Circuit tier) |
| **Wave Digital Filters** (Werner thesis 2016; chowdsp_wdf) | Modular and cheap for circuits with one nonlinearity | Several nonlinearities need R-type adaptors or iteration | Drives, tone stacks, triode stages |
| **Block-oriented / Wiener-Hammerstein** | Very cheap | Misses memory effects (sag, bias shift) unless they are added by hand | Low-CPU "lite" variants |
| **Neural** (NAM WaveNet/LSTM, RTNeural, AIDA-X, Proteus) | Highest snapshot fidelity; huge community library | Typically one model per knob setting; fixed sample rate; opaque | Capture tier |

### NAM ecosystem (October 2026)
- **NeuralAmpModelerCore** (C++/Eigen, MIT) is the reference inference engine. The `.nam` file is JSON: architecture, config, weights and metadata.
- **NAM A2** was announced on 2 June 2026. Tone3000 claims A2-Full uses 30–40 % less CPU than A1-Standard, and that A2-Lite runs on a 600 MHz Cortex-M7. *(vendor claims, verify)*
- Tone3000 is ToneHunt and TONEZONE3000 merged. It has more than 700k tones, all available as A2.
- Fractal's own firmware 33 beta added a NAM block. Compatibility with NAM is now table stakes.
- Decision: **host the native core through P/Invoke** rather than port it. Porting would mean chasing A2 and every later architecture.

## 2. Anti-aliasing
- Oversample by 2–8× **only around nonlinearities**. Use polyphase IIR half-band filters (HIIR, Laurent de Soras), which have zero added latency, rather than FIR half-bands, which add 0.3–1 ms per stage.
- **ADAA**: (F(x[n]) − F(x[n−1])) / (x[n] − x[n−1]). It suppresses tens of dB of aliasing and adds half a sample of delay. It needs an ill-conditioning fallback. Sources: Parker et al., DAFx-16; Bilbao et al., IEEE SPL 2017.
- Neural models run at their training rate (48 kHz), which is why the engine runs at 48 kHz internally.

## 3. Cabinet convolution
- Guitar cab IRs are typically 1024–4096 taps at 48 kHz. Fractal's DynaCab IRs are **2048** samples; FullRes IRs reach 1.37 s.
- **Uniform partitioned convolution** with a partition equal to the I/O buffer adds no latency beyond the buffer.
- **Gardner non-uniform partitioning** (JAES 1995) gives zero latency for long IRs.
- Reference implementation to port: **FFTConvolver / TwoStageFFTConvolver** (MIT). See also Wefers' 2014 dissertation.
- Minimum-phase trimming (cepstral) lets IRs be cut short. Static IR blends are pre-summed; real-time morphs run 2–4 parallel convolvers with a crossfade.

## 4. Time-based effects, modulation, pitch and tuner
- **Reverb:**
  - Dattorro plate (JAES 1997, *Effect Design* parts 1 and 2)
  - FDN with 8–16 lines and Hadamard/Householder feedback, plus absorption filters (Jot & Chaigne 1991)
  - shimmer = a pitch shifter inside the feedback loop
- **Delay:** Lagrange or Thiran fractional delay; BBD (Raffel & Smith, DAFx-10); tape hysteresis (CHOW Tape, DAFx-19).
- **Pitch:**
  - PSOLA/WSOLA is low latency and monophonic.
  - The phase vocoder (Laroche & Dolson 1999) handles polyphony but needs an FFT of at least 1024 samples, about 21 ms of latency.
  - Dual-delay-line crossfade is the classic H910-style detune.
- **Tuner:** YIN (de Cheveigné & Kawahara 2002) or MPM (McLeod & Wyvill 2005). Use a 2048–4096-sample window for low B, with parabolic interpolation, running on the analysis thread.

## 5. Numerics, smoothing and switching
- Signal path in float32 with SIMD. Use double for IIR filters below 50 Hz or with high Q, for phase accumulators, and for Newton solvers.
- Smooth parameters with a 5–50 ms one-pole or linear ramp. For filters, smooth the cutoff and recompute coefficients every 16–32 samples. Use TPT/ZDF topologies (Zavalishin, *The Art of VA Filter Design*).
- **Denormals:** .NET has no managed MXCSR or FPCR API *(verify for .NET 10)*. Set FTZ/DAZ through a native helper on audio-thread entry, and add ε to feedback paths.
- Presets: build the graph off-thread, pre-roll it, swap it with RCU, crossfade 10–30 ms, and spill the old graph's tails.

## 6. Real-time .NET
- **Key fact:** a GC suspends *all* managed threads, including the audio callback, even when that thread never allocates. NativeAOT uses the same GC.
- Mitigations:
  - zero allocations in steady state, using native or pinned memory
  - workstation GC with `SustainedLowLatency` and a large gen0 budget
  - the REST server in **another process**
  - `GC.TryStartNoGCRegion` for short critical windows *(verify under NativeAOT)*
- NativeAOT removes tier-up and JIT stalls in the middle of a callback. With JIT instead, use ReadyToRun and warm up with silence.
- SIMD: `Vector128<T>` is the portable baseline (SSE and AdvSIMD). `Vector256` is emulated on Apple Silicon. Use `[SkipLocalsInit]` and `Unsafe`/`MemoryMarshal` in inner loops.
- **P/Invoke:** use `[LibraryImport]` with blittable arguments. Never use `SuppressGCTransition` for long calls; a thread inside a native call does not block a GC.
- **ONNX Runtime is not suitable for real time.** It has per-call overhead, allocates, and keeps no streaming state between calls.
- Thread priority: let the driver own the callback thread.
  - Workers on Windows: MMCSS "Pro Audio".
  - Workers on macOS: time-constraint policy plus `os_workgroup_join` (WWDC20 10224).
  - Linux: SCHED_FIFO or rtkit.

## 7. Audio I/O options

| Option | Notes |
|---|---|
| NAudio | Windows only (ASIO, WASAPI). Mature, and a good reference for ASIO COM interop |
| PortAudioSharp2 | Cross-platform. The NuGet binaries probably lack ASIO *(verify)* |
| RtAudio (C++) | ASIO, WASAPI, CoreAudio, ALSA and JACK. No maintained .NET binding, so wrap it with a C shim |
| miniaudio / MiniAudioEx | Mature, but **no ASIO** |
| OwnAudioSharp | Aimed at playback and mixing. No ASIO or JACK |
| ManagedBass + BASSASIO | Commercial licence |
| CoreAudio AUHAL via P/Invoke | Best option on macOS |
| JACK / PipeWire | Best option on Linux. Sample-accurate MIDI |

**ASIO licensing:** since October 2025 the SDK is dual-licensed, proprietary (a free signed agreement) or GPLv3 (heise.de/-10963451). The VST 3.8 SDK is MIT.

## 8. Latency
- Sound travels about 34 cm per ms. Below 5 ms round trip is imperceptible to most players; above 10–12 ms is disturbing (Lester & Boley, AES 2007).
- At 48 kHz, the round trip from buffers alone is 1.3, 2.7 and 5.3 ms for 32, 64 and 128 samples, plus converters (0.5–1.5 ms) and USB safety buffers.
- Measure with a loopback cable plus the built-in test, and cross-check with Oblique RTL Utility.

## 9. Control plane
- An SPSC ring of unmanaged command structs: head and tail via `Volatile`, padded to separate cache lines.
- No `Channel<T>` or `ConcurrentQueue` on the audio side.
- Continuous knob values are written as atomic float targets.
- Telemetry goes from shared memory to a 30–60 Hz WebSocket. Binary `Float32` frames are AOT-friendly, whereas SignalR's NativeAOT support is partial *(verify)*.
- MIDI:
  - DryWetMIDI covers Windows and macOS devices *(Linux: verify)*.
  - RtMidi through P/Invoke covers all platforms.
  - JACK MIDI on Linux.

## 10. Projects to learn from
- **LiveSPICE** (C#): a schematic becomes MNA equations, which are compiled to IL and solved with Newton iterations in real time. This is the most relevant .NET reference for the Circuit tier.
- **Stompbox / StompboxUI** (Mike Oliphant): a C++ engine with a C# UI and a remote protocol. It is architecturally close to ours.
- **NPlug**: VST3 plugins in C# through NativeAOT *(verify maintenance)*.
- **AudioPlugSharp** (Windows, C++/CLI), **NWaves** (an algorithm reference, not real-time safe).
- Native: chowdsp_utils, chowdsp_wdf, CHOW Tape, Airwindows (MIT), FFTConvolver, HIIR, RTNeural, NeuralAmpModelerCore, Faust (which has a C# backend, *verify*), clap and clap-wrapper.
- Books and papers: the DAFx archive, Julius O. Smith's online books (CCRMA), Zölzer's *DAFX*, Pirkle's *Designing Audio Effect Plugins in C++*.

## Sources
- https://github.com/sdatkinson/NeuralAmpModelerCore
- https://www.tone3000.com/blog/introducing-neural-amp-modeler-nam-architecture-2-a2
- https://arxiv.org/abs/2511.07470 (Slimmable NAM)
- https://arxiv.org/abs/2106.03037 (RTNeural)
- https://github.com/dsharlet/LiveSPICE
- https://github.com/mikeoliphant/Stompbox
- https://github.com/xoofx/NPlug
- https://github.com/HiFi-LoFi/FFTConvolver
- https://github.com/Chowdhury-DSP/chowdsp_wdf
- https://github.com/Chowdhury-DSP/chowdsp_utils
- https://github.com/jatinchowdhury18/AnalogTapeModel
- https://ccrma.stanford.edu/~dattorro/EffectDesignPart1.pdf
- https://ccrma.stanford.edu/~jos/
- https://www.dafx.de/paper-archive/
- https://developer.apple.com/documentation/audiotoolbox/adding-parallel-real-time-threads-to-audio-workgroups
- https://learn.microsoft.com/dotnet/api/system.gc.trystartnogcregion
- https://www.heise.de/-10963451
- https://github.com/free-audio/clap-wrapper
- https://www.oblique-audio.com/free/rtlutility
- Testing-specific sources are in [03-testing-strategy.md](03-testing-strategy.md).
