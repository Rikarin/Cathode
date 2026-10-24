# ADR-0002: Native audio shim with a planar C ABI (spike S1, macOS and Linux)

- **Status:** accepted for macOS. Windows has a WASAPI backend (see [Windows](#windows-wasapi)); ASIO still needs its spike. Linux (ALSA, with JACK as a second backend) is implemented and container-tested (see [Linux](#linux-backend)), and still needs a hardware run.
- **Date:** 2026-10-23
- **Spikes:** S1 (native audio shim) and S5 (FTZ/DAZ), from [04-implementation-plan.md](../04-implementation-plan.md#phase-0-feasibility-spikes-34-weeks--answer-the--items-before-committing)

## Decision

Audio I/O goes through `engine/native/cth_audio`, a small C library with one C ABI on every platform:
- The OS driver owns the real-time thread (CoreAudio, JACK), or the shim owns one (ALSA). There is no intermediate queue.
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

## Windows (WASAPI)

`cth_audio_wasapi.c` implements the same ABI, and `cth_midi_winmm.c` the MIDI half:
- A cth device is a **render endpoint paired with a capture endpoint** of the same hardware (same container id, the default capture endpoint first). A uid `"<render id>|<capture id>"` pairs any two, `"<render id>|"` opens no input, and NULL opens the default render and capture endpoints.
- Each endpoint opens **exclusive event-driven** when the device allows it, else shared: IAudioClient3's small engine periods at the mix rate, then the classic shared stream (the capture side converts if it must follow another rate). `CTH_WASAPI_SHARED=1` skips exclusive mode so other applications keep playing.
- One MMCSS "Pro Audio" thread waits on both events. Capture goes through a FIFO primed with one period (counted in the reported input latency); each render event calls the managed callback in chunks of at most one period.
- Overloads count capture discontinuities, FIFO underruns and overflows, a shared render buffer that ran dry, and 2 s without a device event.
- MIDI uids are `in:`/`out:` + port name + `#n` (WinMM numbers ports by position, which shifts on hot-plug); events are timestamped on the QueryPerformanceCounter clock.

Verified on Windows 11 x64 over Remote Desktop, whose only endpoint is "Remote Audio" (shared mode only, 10 ms periods, no input): 100 callbacks/s at 480 frames and 48 kHz as expected, 0 overloads, the NativeAOT host at ≤ 2 % of budget, `--self-test` and `--serve` working. Exclusive mode, duplex and the latency test still need a machine with a local interface.

## Linux backend

The target is a headless box in the rig (a Raspberry Pi 5 or a mini PC, x64 or ARM64) running the Host and Server as systemd services, so the primary Linux backend talks to ALSA directly and needs no sound server. JACK is a second backend for desktops (jackd, or PipeWire's JACK).

- **ALSA** (`cth_audio_alsa.c`): `hw:` PCMs opened full duplex on one device, capture linked to playback (`snd_pcm_link`) when the driver allows. Read/write access, interleaved or not. Formats S32_LE, S24_3LE, S24_LE, S16_LE or float, converted to and from planar float. The period is the requested buffer. There are 2 periods, or 3 on USB-Audio cards; `CTH_ALSA_PERIODS` overrides. Resampling in `plughw:` is turned off.
  - The shim runs its own thread: `SCHED_FIFO` at priority 70 (`CTH_RT_PRIORITY` changes it, 0 turns it off). If the process may not use it, the thread runs at normal priority and logs a warning, so the systemd unit should set `LimitRTPRIO=`.
  - The thread is non-blocking and waits in `poll()` on the PCM and a wake pipe, so stopping never waits on the device. Each cycle reads one period, processes it and writes one period.
  - An xrun, a suspend or a 4-buffer stall counts one overload. Both streams are then dropped, re-primed with a full buffer of silence and restarted together, which keeps input and output a fixed distance apart. A lost device keeps retrying five times a second until stopped. `cth_audio_stop` and `close` still return at once.
  - Reported latency: input 1 period, output (periods − 1) periods. The round trip is periods × period, plus converter and USB latency that ALSA does not report.
- **Device ids:** `alsa:<card id>` is PCM device 0 of a card (`alsa:USB` → `hw:CARD=USB,DEV=0`) and `alsa:<card id>,<n>` another device on it. Card ids survive reboots and replugging; card numbers do not. Any other id is passed to `snd_pcm_open` as a PCM name, with or without the `alsa:` prefix (`hw:1,0`, `alsa:plughw:USB`, `null`). With no id, the shim opens the first duplex device, or else the first one with outputs. Enumeration opens each PCM briefly to read its channel counts and picks 48 kHz when the device allows it (the ABI carries one rate per device). A busy device keeps the counts it had last time. ALSA has no channel names.
- **JACK** (`cth_audio_jack.c`, ids `jack:<client>`): libjack is loaded with `dlopen`, so it is neither a build nor a run-time dependency. A device is a client that owns physical ports: `jack:system` under jackd, one per card under PipeWire. Rate and buffer size come from the server. Latency is the physical ports' latency ranges, and xruns come from the xrun callback. JACK buffers are planar float and are used in place. A server whose buffer grows after open still gets callbacks at the size the stream opened with.
- **MIDI** (`cth_midi_alsa.c`) uses the ALSA sequencer, which shares ports with other programs and covers USB, serial and software ports alike. Every source is subscribed, and Midi Through and PipeWire's bridge ports are skipped. A reader thread blocked in `poll()` stamps events on arrival with `CLOCK_MONOTONIC` (the `cth_midi_now_ns` clock), decodes them with `snd_midi_event` and queues them in the same lock-free ring as CoreMIDI. SysEx and system common messages are dropped. Uids are `<client name>:<port>`, with `#2` appended for a second identical device.
- **Verified in a Linux container (Debian, ARM64; no sound hardware):** it compiles warning-free under gcc and clang with `-Wall -Wextra -Werror`, and `cathode-host --self-test` passes. The ALSA `null` PCM runs open, start, stop and duplex. A JACK-paced ALSA PCM (the alsa-plugins `jack` type on jackd's dummy driver) runs at the exact callback rate (375/s at 48 kHz / 128). So does the JACK backend. Killing the server mid-stream counts overloads, and the stream then stops cleanly on both backends. The container could not run real-time, so its xrun counts mean nothing.

## Still open

- A **physical** loopback measurement on a real interface (ProFX or better), to compare its reported latency with what is measured.
- **S2:** callback timing under REST and GC load. This is the process-split decision, ADR-0001.
- Windows: exclusive mode, duplex and a loopback latency measurement on a local interface; **ASIO** (needs the Steinberg SDK, under GPLv3).
- Linux on real hardware: a loopback round trip and a 30 min soak on a USB interface (Pi 5 and x64), with `SCHED_FIFO` granted. Check that 2 or 3 periods hold at 64 frames, and the MIDI path with a USB foot controller.
- Device hot-plug, sample-rate change notifications, and aggregate devices (separate input and output devices).
