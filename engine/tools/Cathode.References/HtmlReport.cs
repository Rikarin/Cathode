using Cathode.Testing.References;
using System.Globalization;
using System.Net;
using System.Text;

namespace Cathode.References;

/// <summary>
///     A self-contained HTML page of match reports: a summary table, then per reference its response and harmonics.
/// </summary>
static class HtmlReport {
    const int Width = 560;
    const int Height = 200;

    public static string Render(IReadOnlyList<MatchReport> reports) {
        var html = new StringBuilder();
        html.Append(
            """
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            """
            + """<meta name="viewport" content="width=device-width, initial-scale=1">"""
            + "\n"
            + """
              <title>Cathode amp references</title>
              <style>
              :root { --bg: #fff; --fg: #1d1d1f; --muted: #6e6e73; --line: #d2d2d7; --ref: #1f6feb; --amp: #d1495b; }
              @media (prefers-color-scheme: dark) { :root { --bg: #161618; --fg: #f5f5f7; --muted: #a1a1a6;
              """
            + " --line: #3a3a3c; --ref: #58a6ff; --amp: #ff7b72; } }\n"
            + """
              body { background: var(--bg); color: var(--fg); font: 14px/1.45 system-ui, sans-serif;
              """
            + " margin: 24px 16px; }\n"
            + """
              table { border-collapse: collapse; width: 100%; overflow-x: auto; display: block; }
              th, td { border-bottom: 1px solid var(--line); padding: 4px 8px; text-align: right; white-space: nowrap; }
              th:first-child, td:first-child, td.text { text-align: left; }
              .muted { color: var(--muted); } .fail { color: var(--amp); } section { margin-top: 32px; }
              svg { max-width: 100%; height: auto; } .ref { stroke: var(--ref); } .amp { stroke: var(--amp); }
              </style></head><body>
              <h1>Circuit amp vs references</h1>
              <p class="muted">ESR pre-emphasised (1 − 0.95 z⁻¹); MRSTFT averaged over 256/1024/4096;
              """
            + " response and harmonics level-matched.\n"
            + """
              <span style="color:var(--ref)">Reference</span> vs <span style="color:var(--amp)">circuit amp</span>.</p>
              <table><tr><th>Reference</th><th>Circuit</th><th>Input dBu</th><th>ESR</th><th>ESR (pre)</th>
              """
            + "<th>MRSTFT</th><th>Harm. dB</th><th>Resp. dB</th><th>Pass</th></tr>"
        );
        foreach (var r in reports) {
            AppendSummaryRow(html, r);
        }

        html.Append("</table>");
        foreach (var r in reports) {
            AppendSection(html, r);
        }

        html.Append("</body></html>\n");
        return html.ToString();
    }

    static void AppendSummaryRow(StringBuilder html, MatchReport r) {
        html.Append(
            CultureInfo.InvariantCulture,
            $"<tr><td><a href=\"#{Encode(r.Id)}\">{Encode(r.Id)}</a></td><td class=\"text\">{Encode(r.Circuit)}</td>"
            + $"<td>{r.InputDbu:0.0}{(r.InputDbuFitted ? " (fit)" : string.Empty)}</td><td>{r.Esr:0.000}</td>"
            + $"<td>{r.EsrPreEmphasis:0.000}</td><td>{r.Mrstft:0.000}</td><td>{r.HarmonicDistanceDb:0.0}</td>"
            + $"<td>{r.ResponseRmsDb:0.0}</td>"
            + $"<td class=\"{(r.Pass == false ? "fail" : string.Empty)}\">{PassText(r)}</td></tr>"
        );
    }

    /// <summary>A reference's section: what was compared, warnings, the response chart, the harmonics.</summary>
    static void AppendSection(StringBuilder html, MatchReport r) {
        html.Append(CultureInfo.InvariantCulture, $"<section id=\"{Encode(r.Id)}\"><h2>{Encode(r.Id)}</h2>");
        html.Append(
            CultureInfo.InvariantCulture,
            $"<p>{Encode(r.Amp)} ({Encode(r.Licence)}, {Encode(r.Architecture)}) vs <b>{Encode(r.Circuit)}</b>. "
            + $"Latency {r.LatencySamples} samples, output gain {r.OutputGainDb:0.0} dB"
            + $"{(r.PolarityInverted ? ", polarity inverted" : string.Empty)}.</p>"
        );
        foreach (var warning in r.Warnings) {
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"fail\">{Encode(warning)}</p>");
        }

        html.Append("<h3>Small-signal response (dB, level-matched)</h3>");
        var offset = r.ReferenceResponse.Zip(r.CircuitResponse, static (a, b) => a - b)
            .Where((_, i) => r.ResponseCenters[i] is >= 80 and <= 8_000)
            .Average();
        html.Append(
            Chart(
                [..r.ResponseCenters.Select(static f => Math.Log10(f))],
                [r.ReferenceResponse.ToArray(), r.CircuitResponse.Select(v => v + offset).ToArray()]
            )
        );
        AppendHarmonics(html, r);
        html.Append("</table></section>");
    }

    /// <summary>The harmonics table: each input level's profile, reference then circuit.</summary>
    static void AppendHarmonics(StringBuilder html, MatchReport r) {
        html.Append(
            "<h3>Harmonics 2–9 (dB re fundamental) at each input level</h3><table><tr><th>Level</th><th>Model</th>"
        );
        for (var h = 1; h <= MatchMetrics.Harmonics; h++) {
            html.Append(CultureInfo.InvariantCulture, $"<th>H{h}</th>");
        }

        html.Append("</tr>");
        for (var l = 0; l < r.HarmonicLevels.Count; l++) {
            foreach (var (name, profile) in new[] {
                         ("reference", r.ReferenceHarmonics[l]), ("circuit", r.CircuitHarmonics[l])
                     }) {
                html.Append(
                    CultureInfo.InvariantCulture,
                    $"<tr><td>{r.HarmonicLevels[l]:0.###} V</td><td class=\"text\">{name}</td>"
                );
                foreach (var value in profile) {
                    html.Append(CultureInfo.InvariantCulture, $"<td>{value:0.0}</td>");
                }

                html.Append("</tr>");
            }
        }
    }

    static string PassText(MatchReport report) =>
        report.Pass switch {
            true => "yes",
            false => "no",
            null => "–"
        };

    static string Encode(string text) => WebUtility.HtmlEncode(text);

    static string Chart(double[] x, double[][] series) {
        var all = series.SelectMany(static s => s).Where(double.IsFinite).ToArray();
        var top = Math.Ceiling(all.Max() / 6) * 6;
        var bottom = Math.Max(top - 48, Math.Floor(all.Min() / 6) * 6);
        double X(double v) => (v - x[0]) / (x[^1] - x[0]) * (Width - 40) + 36;
        double Y(double v) => (top - Math.Clamp(v, bottom, top)) / (top - bottom) * (Height - 20) + 4;

        var svg = new StringBuilder();
        svg.Append(
            CultureInfo.InvariantCulture,
            $"<svg viewBox=\"0 0 {Width} {Height}\" role=\"img\" aria-label=\"Frequency response\" "
            + $"fill=\"none\" stroke-width=\"2\">"
        );
        for (var db = bottom; db <= top; db += 6) {
            svg.Append(
                CultureInfo.InvariantCulture,
                $"<line x1=\"36\" x2=\"{Width - 4}\" y1=\"{Y(db):0.0}\" y2=\"{Y(db):0.0}\" stroke=\"currentColor\" "
                + $"stroke-opacity=\"0.12\" stroke-width=\"1\"/>"
                + $"<text x=\"0\" y=\"{Y(db) + 4:0.0}\" font-size=\"10\" fill=\"currentColor\" "
                + $"stroke=\"none\">{db:0}</text>"
            );
        }

        foreach (var f in new[] { 100.0, 1_000, 10_000 }) {
            svg.Append(
                CultureInfo.InvariantCulture,
                $"<text x=\"{X(Math.Log10(f)) - 10:0.0}\" y=\"{Height - 2}\" font-size=\"10\" fill=\"currentColor\" "
                + $"stroke=\"none\">{(f >= 1_000 ? $"{f / 1_000:0}k" : $"{f:0}")}</text>"
            );
        }

        var classes = new[] { "ref", "amp" };
        for (var s = 0; s < series.Length; s++) {
            var points = string.Join(
                ' ',
                x.Select((v, i) => string.Create(CultureInfo.InvariantCulture, $"{X(v):0.0},{Y(series[s][i]):0.0}"))
            );
            svg.Append(CultureInfo.InvariantCulture, $"<polyline class=\"{classes[s % 2]}\" points=\"{points}\"/>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }
}
