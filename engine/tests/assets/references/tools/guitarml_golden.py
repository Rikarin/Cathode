"""Golden render of a GuitarML (Proteus/NeuralPi) LSTM model, independent of Cathode's C# runner.

A plain numpy port of PyTorch's nn.LSTM + nn.Linear (as in Automated-GuitarAmpModelling's SimpleRNN) in float32.
Usage: python3 -I guitarml_golden.py <model.json> <out.txt> [conditioning ...]

The input is 4,000 samples of a 0.5-amplitude 220 Hz sine at 44.1 kHz with a 1,000-sample linear fade-in, the same
signal GuitarMlLstmModelTests builds. Every tenth output sample is written, one per line.
"""
import json
import sys

import numpy as np


def main():
    model = json.load(open(sys.argv[1]))
    conditioning = [float(v) for v in sys.argv[3:]]
    info, weights = model["model_data"], model["state_dict"]
    hidden, layers = info["hidden_size"], info.get("num_layers", 1)
    f32 = lambda name: np.asarray(weights[name], dtype=np.float32)

    n = 4000
    t = np.arange(n)
    x = (0.5 * np.minimum(1.0, t / 1000.0) * np.sin(2 * np.pi * 220 * t / 44100.0)).astype(np.float32)

    h = [np.zeros(hidden, np.float32) for _ in range(layers)]
    c = [np.zeros(hidden, np.float32) for _ in range(layers)]
    sigmoid = lambda v: (1 / (1 + np.exp(-v))).astype(np.float32)
    out = np.zeros(n, np.float32)
    for i in range(n):
        state = np.asarray([x[i]] + conditioning, np.float32)
        for k in range(layers):
            g = (f32(f"rec.weight_ih_l{k}") @ state + f32(f"rec.bias_ih_l{k}")
                 + f32(f"rec.weight_hh_l{k}") @ h[k] + f32(f"rec.bias_hh_l{k}"))
            gi, gf, gg, go = np.split(g, 4)
            c[k] = sigmoid(gf) * c[k] + sigmoid(gi) * np.tanh(gg)
            h[k] = sigmoid(go) * np.tanh(c[k])
            state = h[k]
        y = f32("lin.weight") @ state + f32("lin.bias")
        out[i] = y[0] + (x[i] if info.get("skip", 0) else 0)

    with open(sys.argv[2], "w") as f:
        for v in out[::10]:
            f.write(f"{v:.8e}\n")


if __name__ == "__main__":
    main()
