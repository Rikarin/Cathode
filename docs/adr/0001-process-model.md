# ADR-0001: The audio engine and the REST server run in separate processes (spike S2)

- **Status:** accepted
- **Date:** 2026-10-23
- **Spike:** S2, from [04-implementation-plan.md](../04-implementation-plan.md#phase-0-feasibility-spikes-34-weeks--answer-the--items-before-committing). Harness: `engine/spikes/Cathode.Spikes.GcLoad`.

## Decision

**Cathode.Host** (audio engine) and **Cathode.Server** (ASP.NET Core REST and WebSocket) are **separate processes**, as proposed in [02-architecture.md §2](../02-architecture.md#why-the-engine-and-the-rest-server-are-separate-processes-). There is no in-process mode for production; in-process remains only for tests (`InProcEngineControl`).

**Both processes:**
- Server: ASP.NET Core defaults. Its GCs cannot touch the audio thread.
- Communication: an IPC channel for commands and events, plus shared memory for telemetry ([02 §5.9](../02-architecture.md#59-telemetry)).

**Host must treat *any* GC during a performance as a dropout risk:**
- workstation, non-concurrent GC with a large gen0 budget (`GCgen0size`), so the rare control-thread allocations almost never trigger a collection
- zero allocation on the audio path, which is already enforced by tests
- preallocated or native memory for everything a preset load creates
- no JSON parsing in the Host beyond small command payloads; large payloads (IR or model uploads) are written to disk by the Server, and the Host reads them by path

## How it was measured

- **Machine:** M1 Max, macOS.
- **Device:** Mackie ProFX (USB) through `cth_audio`, at 48 kHz, 64 and 32 frames, output muted.
- **Graph:** Input → NAM A2 Full → Output, about 4 % of one core.
- **Timing:** measured **natively in the shim around the whole managed call**. This includes the time the driver thread waits to enter the runtime while a GC suspends managed threads, which managed timing cannot see.
- **Driver overloads:** CoreAudio's processor-overload notifications, which are audible dropouts.
- **Each run:** 5 s warm-up, then 20 s measured. Framework-dependent JIT build, with GC modes set through `DOTNET_gc*`.
- **API under load:** a realistic slice of the planned API:
  - knob PATCH: 70 %
  - full-preset GET, about 50 KB JSON: 27 %
  - 2 MB IR upload: 3 %
- **Load generator:** a separate process, at two levels:
  - *Realistic:* 100 req/s, an editor dragging knobs with polling and occasional uploads.
  - *Stress:* unthrottled with 8 connections, 10–14 k req/s.
- **Allocation storm:** background garbage at 50 or 500 MB/s with some survivors, and no HTTP, which isolates the GC itself.
- **Reproduce:** `engine/spikes/Cathode.Spikes.GcLoad/run.sh "<device uid>"`, then `summarize.py results.jsonl`.

## Results

Callback duration is shown as a percentage of the buffer period (1.33 ms at 64 frames, 0.67 ms at 32). "Over budget" counts callbacks that took longer than the period, and "driver overloads" counts dropouts reported by CoreAudio.

| Configuration | Buffer | p50 | p99.9 | p99.99 | Max | ≥ 50 % | Over budget | Driver overloads | GCs 0/1/2 | GC pause (ms) | Alloc (MB/s) | Requests |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| baseline | 64 | 5 % | 7 % | 9 % | 9.0 % | 0 | 0 | 0 | 0/0/0 | 0.0 | 0 | — |
| alloc storm 50 MB/s | 64 | 5 % | 66 % | 201 % | 476.1 % | 23 | 7 | 7 | 160/5/1 | 86.0 | 50 | — |
| alloc storm 500 MB/s | 64 | 5 % | 201 % | 201 % | 420.6 % | 182 | 59 | 64 | 1600/30/7 | 695.3 | 501 | — |
| in-proc realistic | 64 | 5 % | 34 % | 82 % | 159.1 % | 10 | 1 | 1 | 65/64/64 | 46.4 | 33 | 2429 |
| in-proc stress | 64 | 6 % | 98 % | 201 % | 574.8 % | 772 | 12 | 22 | 3473/3119/3119 | 3378.1 | 4998 | 327160 |
| in-proc stress, non-concurrent | 64 | 6 % | 87 % | 131 % | 939.6 % | 606 | 10 | 12 | 3623/3258/3258 | 3352.6 | 5214 | 341657 |
| in-proc stress, server GC | 64 | 6 % | 195 % | 201 % | 299.9 % | 783 | 314 | 379 | 546/513/513 | 1310.7 | 5483 | 357019 |
| in-proc stress, gen0 256 MB | 64 | 6 % | 93 % | 201 % | 1858.9 % | 528 | 11 | 16 | 2712/2712/2694 | 2718.7 | 4985 | 324610 |
| in-proc stress, SustainedLowLatency | 64 | 6 % | 201 % | 201 % | 1302.4 % | 740 | 48 | 56 | 3224/2920/2920 | 3219.0 | 4263 | 283346 |
| out-of-proc realistic | 64 | 5 % | 7 % | 8 % | 8.0 % | 0 | 0 | 0 | 0/0/0 | 0.0 | 0 | — |
| out-of-proc stress | 64 | 6 % | 9 % | 10 % | 9.8 % | 0 | 0 | 0 | 0/0/0 | 0.0 | 0 | — |
| baseline | 32 | 6 % | 7 % | 8 % | 8.7 % | 0 | 0 | 0 | 0/0/0 | 0.0 | 0 | — |
| in-proc realistic | 32 | 6 % | 61 % | 201 % | 446.1 % | 45 | 17 | 19 | 65/65/64 | 55.7 | 33 | 2429 |
| in-proc stress | 32 | 6 % | 180 % | 201 % | 536.6 % | 3803 | 998 | 1418 | 3136/2831/2831 | 3052.9 | 4369 | 290640 |
| out-of-proc realistic | 32 | 6 % | 7 % | 8 % | 7.7 % | 0 | 0 | 0 | 0/0/0 | 0.0 | 0 | — |
| out-of-proc stress | 32 | 6 % | 8 % | 8 % | 8.3 % | 0 | 0 | 0 | 0/0/0 | 0.0 | 0 | — |

## Findings

1. **Out-of-process is indistinguishable from no load.** At 14,000 req/s, the same load that produced about 5 GB/s of allocation and over 3,000 collections in 20 s when it ran in-process, the audio process's worst callback was 9.8 % of budget (baseline 9.0 %), with zero overloads at both 64 and 32 frames. CPU contention from a busy API on a 10-core machine is not a problem.
2. **In-process fails even under realistic load.** At 100 req/s the worst callback was 159 % of budget at 64 frames (1 dropout in 20 s) and 446 % at 32 frames (19 dropouts). At one dropout every 20 s, a gig has hundreds.
3. **The GC is the cause, not the web server.** A pure allocation storm of 50 MB/s, mostly gen0 (160/5/1 collections), caused 7 dropouts in 20 s, with one callback blocked for 4.8 buffer periods (6.3 ms). A gen0 collection only needs to suspend threads, and the driver thread waits at the managed boundary for as long as the suspension lasts.
4. **No GC setting rescues in-process.**

   | Setting | Dropouts in 20 s (stress, 64 frames) | Comment |
   |---|---|---|
   | Non-concurrent | 12 | |
   | Default | 22 | |
   | gen0 256 MB | 16 | worst stall 24.8 ms (1 859 %) |
   | SustainedLowLatency | 56 | |
   | Server GC | 379 | worst; it suspends with more threads |

   Under real allocation pressure, none of them avoids suspensions.
5. **Uploads drive gen2.** Each 2 MB upload lands on the large object heap, and realistic load produced 64 gen2 collections in 20 s. That is another reason uploads must never be parsed in the audio process.

## Consequences

- Phase 3 builds `Cathode.Server` as its own process, with an `IpcEngineControl` transport to the Host. The shared contract test suite runs against both in-process and IPC (docs/03 §8).
- The Electron app spawns and supervises **two** sidecars.
- **Host allocation budget:** the Host must stay GC-free during a performance, not merely "low-allocation".
  - Follow-up (S2b): measure Host-side allocations and GC count during preset-switch storms, which build graphs, load NAM models and compute IR partitions.
  - Gate it in the soak test: zero GCs in the Host during a 1-hour run with scene and preset changes.
- Even in tests, the in-process mode must not be used for latency or soak measurements.

## Notes and limits

- The data comes from one machine and macOS only. The mechanism, GC thread suspension blocking reverse P/Invoke, is platform-independent, so the decision does not wait for Windows and Linux numbers. Those come with their backends.
- In-process runs show load-generator failures in the last seconds because the audio process stopped its server first. That is a harness artefact, not a server failure. Out-of-process runs had none.
- Tiered JIT build: NativeAOT changes JIT warm-up but not GC suspension, so the conclusion holds for the AOT Host.
