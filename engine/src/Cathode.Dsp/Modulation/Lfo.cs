namespace Cathode.Dsp.Modulation;

public enum LfoShape {
    Sine,
    Triangle,

    /// <summary>A square with softened edges (tanh of a sine), so gain and delay modulation never click.</summary>
    Square,
}

/// <summary>Low-frequency oscillator with a phase in [0, 1). Allocation-free; one instance drives both channels.</summary>
public struct Lfo {
    private double _phase;

    public void Reset(double phase = 0) => _phase = phase;

    /// <summary>Advances by one sample at <paramref name="rateHz" />.</summary>
    public void Advance(double rateHz, double sampleRate) {
        _phase += rateHz / sampleRate;
        if (_phase >= 1) {
            _phase -= Math.Floor(_phase);
        }
    }

    /// <summary>Bipolar value in [-1, 1] at the current phase plus <paramref name="offset" /> (in cycles, e.g. 0.25 = 90°).</summary>
    public readonly float Value(LfoShape shape, double offset = 0) {
        var p = _phase + offset;
        p -= Math.Floor(p);
        return shape switch {
            LfoShape.Triangle => (float)(p < 0.5 ? 4 * p - 1 : 3 - 4 * p),
            LfoShape.Square => (float)(Math.Tanh(4 * Math.Sin(2 * Math.PI * p)) / Math.Tanh(4)),
            _ => (float)Math.Sin(2 * Math.PI * p),
        };
    }
}
