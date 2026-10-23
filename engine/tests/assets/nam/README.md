# NAM golden renders

These are reference outputs from **NeuralAmpModelerCore v0.6.0's own `render` tool**. Cathode's `cth_nam` wrapper must reproduce them (see `NamAmpBlockTests`).

- `input.wav`: a deterministic, guitar-like DI made by `tools/make_input.py`. It is 0.1 s of silence, then Karplus–Strong plucks (a soft low E, a hard A, a chord, a muted chug) peaking at −6 dBFS, 0.75 s long, mono 24-bit at 48 kHz.

  Upstream's own `example_audio/input.wav` is not used because it is one second of silence followed by a steady tone. An earlier version of these files used its silent opening, which made the golden test meaningless.
- `<model>.ref.wav`: `tools/render example_models/<model>.nam input.wav <model>.ref.wav`, rendered with 64-sample buffers and the default exact tanh.
- `A2-lite.ref.wav`: the same, with `--slim 0`.

Models and the `render` tool © Steven Atkinson, MIT License (https://github.com/sdatkinson/NeuralAmpModelerCore).

**When the pinned NAM core version changes** (`engine/native/cth_nam/CMakeLists.txt`), regenerate these files with that version's `render` tool, and review any change in the null test results before accepting them.
