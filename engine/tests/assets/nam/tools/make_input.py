#!/usr/bin/env python3
"""Writes input.wav: a deterministic, guitar-like DI used as the stimulus for the NAM golden renders.

0.1 s of silence (idle behaviour and onset), then Karplus-Strong plucks at realistic DI levels:
a soft low E, a hard-picked A, a three-note chord and a short muted chug. Mono, 24-bit, 48 kHz, 0.75 s.
Run with: python3 -I make_input.py <output.wav>
"""
import random
import struct
import sys
import wave

RATE = 48_000
LENGTH = int(0.75 * RATE)


def pluck(freq, start, amp, decay, rng, out):
    period = max(2, int(round(RATE / freq)))
    buf = [rng.uniform(-1, 1) for _ in range(period)]
    for i in range(start, LENGTH):
        k = (i - start) % period
        nxt = (k + 1) % period
        value = buf[k]
        buf[k] = decay * 0.5 * (buf[k] + buf[nxt])
        out[i] += amp * value


def main(path):
    rng = random.Random(1234)
    out = [0.0] * LENGTH
    pluck(82.41, int(0.10 * RATE), 0.12, 0.996, rng, out)   # soft low E
    pluck(110.0, int(0.30 * RATE), 0.35, 0.995, rng, out)   # hard A
    for f in (146.83, 196.0, 246.94):                       # D-G-B chord
        pluck(f, int(0.45 * RATE), 0.15, 0.994, rng, out)
    pluck(82.41, int(0.65 * RATE), 0.40, 0.97, rng, out)    # palm-muted chug
    peak = max(abs(v) for v in out)
    scale = 0.5 / peak if peak > 0.5 else 1.0               # keep peaks at or below -6 dBFS
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(3)
        w.setframerate(RATE)
        frames = bytearray()
        for v in out:
            q = max(-8388608, min(8388607, int(round(v * scale * 8388608))))
            frames += struct.pack("<i", q)[:3]
        w.writeframes(bytes(frames))


if __name__ == "__main__":
    main(sys.argv[1])
