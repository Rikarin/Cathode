# ADR-0002: Native audio shim with a planar C ABI (spike S1, macOS)

- **Status:** accepted for macOS. Windows (ASIO/WASAPI) and Linux (JACK) still need their spikes.
- **Date:** 2026-10-23
- **Spikes:** S1 (native audio shim) and S5 (FTZ/DAZ), from [04-implementation-plan.md](../04-implementation-plan.md#phase-0-feasibility-spikes-34-weeks--answer-the--items-before-committing)

## Decision

Audio I/O goes through `engine/native/cth_audio`, a small C library with one C ABI on every platform:
- The OS driver owns the real-time thread. There is no intermediate queue.
- The shim deinterleaves device buffers into planar float channels, enables flush-to-zero on the callback thread, and calls a managed `[UnmanagedCallersOnly]` function pointer. Output channels arrive zeroed, and callbacks larger than 4096 frames are split.
- On macOS the backend is a **HAL IOProc** on a single device (`AudioDeviceCreateIOProcID`) rather than AUHAL. It is the lowest layer, gives direct buffer-size control, and runs on CoreAudio's real-time thread.
- Dropouts are counted from `kAudioDeviceProcessorOverload`.
- Reported latency is device latency + safety offset + stream latency + buffer, per direction.

Managed side: `Cathode.Audio.Native` (`LibraryImport` bindings, `NativeAudioSession`), and `cathode-host --devices | --audio-test | --latency-test`.

## Results (MacBook Pro M1 Max, macOS, NativeAOT host, Release)

**Callback timing** through managed code (`--audio-test`, Basic graph, output muted):

| Device | Buffer | Callbacks/s (expected) | Driver overloads | Engine worst callback | Reported round trip |
|---|---|---|---|---|---|
| Mackie ProFX (USB), output only | 64 | 753 (750) | 0 | 0.6 % of budget | — |
| Mackie ProFX (USB), output only | 32 | 1 507 (1 500) | 0 | 0.8 % | — |
| Mackie ProFX (USB), duplex | 32 | — | 0 | 0.7 % | 312 frames = **6.50 ms** |
| Mackie ProFX (USB), duplex | 64 | — | 0 | 1.0 % | 376 frames = 7.83 ms |
| Mackie ProFX (USB), duplex | 128 | — | 0 | 0.5 % | 504 frames = 10.50 ms |

**Measured round trip** through a loopback (`--latency-test`, BlackHole 2ch virtual device, 14 clicks each, zero spread):

| Buffer | Measured | Driver-reported |
|---|---|---|
| 32 | 64 frames = 1.33 ms | 64 frames |
| 64 | 128 frames = 2.67 ms | 128 frames |
| 128 | 256 frames = 5.33 ms | 256 frames |

## Findings

1. **The managed callback path is sound.** A .NET NativeAOT callback driven by CoreAudio runs at exact rate with zero overloads down to 32-sample buffers. The engine's worst callback stayed under 1 % of budget, although the graph is still trivial.
2. **The shim adds no latency.** On a virtual loopback the measured round trip is exactly two buffers and matches the driver's report to the frame.
3. **The interface dominates latency.** The ProFX, a class-compliant USB mixer, adds about 248 frames (5.2 ms) of fixed converter, USB and safety latency. Even at 32 samples the reported round trip is 6.5 ms. The < 6 ms target in the architecture needs a pro interface with low fixed latency, as the plan's hardware rig assumed. This should be stated in user docs, and the editor's audio setup page should show the reported round trip.
4. **FTZ works from the shim.** `cth_fpu_flush_denormals` sets FPCR.FZ on ARM64 (MXCSR FTZ|DAZ on x86). A unit test shows a subnormal product becomes 0 on a flushed thread. Blocks keep their explicit state flushing as a second line of defence.

## Still open

- A **physical** loopback measurement on a real interface (ProFX or better), to compare its reported latency with what is measured.
- **S2:** callback timing under REST and GC load. This is the process-split decision, ADR-0001.
- Windows backend (ASIO under GPLv3 plus WASAPI) and Linux backend (JACK/PipeWire). The library currently builds as a stub on both, so the managed code and CI work there.
- Device hot-plug, sample-rate change notifications, and aggregate devices (separate input and output devices).
