using Cathode.References;
using Cathode.Testing.References;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

// cathode-references: the amp reference harness (docs/06 §4, §5.4).
//
//   status                      what the cache holds; where to fetch the local-only references by hand
//   fetch [--force]             download the redistributable (GPL-compatible) references into the cache
//   hash --file <path>          SHA-256 of a file (to record a hand-fetched reference in the manifest)
//   match [--id <id>]... [--family <f>] [--type <Type>] [--set <param>=<v>]... [--input-dbu <dBu>]
//         [--json] [--html] [--out <dir>]
//                               compare circuit amps with references (default: every reference with a circuit)
//   fit --id <id> --tol <param>:<min>:<max>[:<nominal>]... [--max-evals <n>] [--input-dbu <dBu>]
//   fit --synthetic [--type <Type>]
//                               fit physical tolerances (Nelder-Mead on ESR + MRSTFT)
//
// Options for every command: --manifest <path> (default: found above the working folder), --cache <dir> (default
// $CATHODE_REFERENCES or ~/.cache/cathode/references).

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length == 0 || args[0] is "-h" or "--help" or "help") {
    PrintUsage();
    return args.Length == 0 ? 2 : 0;
}

// Ctrl+C cancels the downloads under way (each then reports FAILED); a second Ctrl+C ends the process.
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => {
    e.Cancel = !cancel.IsCancellationRequested;
    cancel.Cancel();
};

try {
    var options = new Options(
        args[1..],
        new HashSet<string>(StringComparer.Ordinal) { "force", "json", "html", "synthetic" }
    );
    var manifestPath = options.Value("manifest") ?? ReferenceManifest.Locate(Directory.GetCurrentDirectory());
    var manifest = ReferenceManifest.Load(manifestPath);
    var cache = options.Value("cache") is { } root
        ? new(Path.GetFullPath(root))
        : ReferenceCache.Default;

    return args[0] switch {
        "status" => Status(manifest, cache),
        "fetch" => await Fetch(manifest, cache, options.Flag("force"), cancel.Token),
        "hash" => Hash(options),
        "match" => Match(manifest, cache, options, manifestPath),
        "fit" => Fit(manifest, cache, options),
        _ => Unknown(args[0])
    };
} catch (Exception e) when (e is ArgumentException
                                or FileNotFoundException
                                or KeyNotFoundException
                                or InvalidDataException
                                or JsonException
                                or FormatException
                                or NotSupportedException) {
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

static int Unknown(string command) {
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 2;
}

static void PrintUsage() =>
    Console.WriteLine(
        """
        Usage: cathode-references <command> [options]
          status                         cache contents; local-only references to fetch by hand
          fetch [--force]                download the redistributable references (GPL-compatible only)
          hash --file <path>             SHA-256 of a file
          match [--id <id>]... [--family <f>] [--type <Type>] [--set <param>=<v>]... [--input-dbu <dBu>]
                [--json] [--html] [--out <dir>]
          fit --id <id> --tol <param>:<min>:<max>[:<nominal>]... [--max-evals <n>] [--input-dbu <dBu>]
          fit --synthetic [--type <Type>]
        Common: --manifest <path>, --cache <dir> (default $CATHODE_REFERENCES or ~/.cache/cathode/references)
        """
    );

static int Status(ReferenceManifest manifest, ReferenceCache cache) {
    Console.WriteLine($"Cache: {cache.Root}{(cache.Exists ? string.Empty : " (does not exist)")}");
    Console.WriteLine();
    Console.WriteLine($"{"id",-44} {"licence",-8} {"format",-14} state");
    foreach (var entry in manifest.References) {
        Console.WriteLine($"{entry.Id,-44} {entry.Licence,-8} {entry.Format,-14} {cache.StateOf(entry)}");
    }

    var missing = manifest.References
        .Where(e => !e.Redistributable && cache.StateOf(e) == ReferenceState.Missing)
        .ToList();
    if (missing.Count > 0) {
        Console.WriteLine();
        Console.WriteLine("Local-only references (licence forbids scripted download and redistribution). To use one,");
        Console.WriteLine("download it by hand from its page and save it in the cache under the name shown:");
        foreach (var entry in missing) {
            Console.WriteLine();
            Console.WriteLine($"  {entry.Id}  ({entry.Licence}, {entry.Author})");
            Console.WriteLine($"    page:  {entry.Url}");
            Console.WriteLine($"    model: {entry.SourceName ?? "(choose one; record its settings in the manifest)"}");
            Console.WriteLine($"    save:  {cache.PathOf(entry)}");
        }
    }

    var mismatched = manifest.References.Where(e => cache.StateOf(e) == ReferenceState.Mismatch).ToList();
    foreach (var entry in mismatched) {
        Console.Error.WriteLine($"warning: {entry.Id} does not match its SHA-256; it is ignored");
    }

    return 0;
}

static async Task<int> Fetch(
    ReferenceManifest manifest,
    ReferenceCache cache,
    bool force,
    CancellationToken cancellationToken
) {
#pragma warning disable SK3541 // Fetch runs once per process, so this client already lives as long as the command.
    using var http = new HttpClient();
#pragma warning restore SK3541
    http.DefaultRequestHeaders.UserAgent.ParseAdd("cathode-references/1");
    http.Timeout = TimeSpan.FromMinutes(2);
    Console.WriteLine($"Fetching redistributable references into {cache.Root}");
    var failures = await new Fetcher(cache, http).FetchAllAsync(manifest.References, force, cancellationToken);
    var local = manifest.References.Count(e => !Fetcher.MayFetch(e) && cache.StateOf(e) == ReferenceState.Missing);
    if (local > 0) {
        Console.WriteLine($"{local} local-only reference(s) are missing; `status` lists where to fetch them by hand.");
    }

    return failures == 0 ? 0 : 1;
}

static int Hash(Options options) {
    var path = options.Value("file") ?? throw new ArgumentException("hash needs --file <path>.");
    Console.WriteLine($"{ReferenceCache.Sha256Of(path)}  {new FileInfo(path).Length} bytes  {path}");
    return 0;
}

static int Match(ReferenceManifest manifest, ReferenceCache cache, Options options, string manifestPath) {
    var entries = Select(manifest, options).ToList();
    var type = options.Value("type");
    var overrides = options.Assignments("set");
    var reports = new List<MatchReport>();
    Console.WriteLine(
        $"{"reference",-36} {"circuit",-28} {"dBu",6} {"ESR",7} {"ESRpre",7} {"MRSTFT",7} {"harm dB",8} {"resp dB",8} {"pass",5}"
    );
    foreach (var entry in entries) {
        var settings = Settings(entry, type, overrides);
        if (settings is null) {
            continue;
        }

        if (ReferenceModels.Unsupported(entry) is { } reason) {
            Console.WriteLine($"{entry.Id,-36} skipped: {reason}");
            continue;
        }

        if (cache.TryLocate(entry) is not { } path) {
            Console.WriteLine(
                $"{entry.Id,-36} skipped: {cache.StateOf(entry).ToString().ToLowerInvariant()} in the cache"
            );
            continue;
        }

        var watch = Stopwatch.StartNew();
        using var reference = ReferenceModels.Open(entry, path);
        var report = ReferenceMatcher.Match(entry, reference, settings, options.Number("input-dbu"));
        reports.Add(report);
        Console.WriteLine(
            $"{entry.Id,-36} {Truncate(report.Circuit, 28),-28} {report.InputDbu,5:0.0}{(report.InputDbuFitted ? "*" : " ")} "
            + $"{report.Esr,7:0.000} {report.EsrPreEmphasis,7:0.000} {report.Mrstft,7:0.000} {report.HarmonicDistanceDb,8:0.0} "
            + $"{report.ResponseRmsDb,8:0.0} {report.Pass switch { true => "yes", false => "NO", null => "-" },5}"
            + $"  ({watch.Elapsed.TotalSeconds:0}s)"
        );
        foreach (var warning in report.Warnings) {
            Console.WriteLine($"{string.Empty,-36}   note: {warning}");
        }
    }

    Console.WriteLine("* input calibration fitted (the source does not state it)");
    if (reports.Count > 0 && (options.Flag("json") || options.Flag("html"))) {
        var engineRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, "..", "..", ".."));
        var output = options.Value("out") ?? Path.Combine(engineRoot, "artifacts", "references");
        Directory.CreateDirectory(output);
        if (options.Flag("json")) {
            var json = Path.Combine(output, "match.json");
            File.WriteAllText(json, JsonSerializer.Serialize(reports, ReferenceJsonContext.Default.ListMatchReport));
            Console.WriteLine($"wrote {json}");
        }

        if (options.Flag("html")) {
            var html = Path.Combine(output, "match.html");
            File.WriteAllText(html, HtmlReport.Render(reports));
            Console.WriteLine($"wrote {html}");
        }
    }

    return reports.Any(static r => r.Pass == false) ? 1 : 0;
}

static int Fit(ReferenceManifest manifest, ReferenceCache cache, Options options) {
    var maxEvaluations = (int)(options.Number("max-evals") ?? 120);
    if (options.Flag("synthetic")) {
        return FitSynthetic(options.Value("type") ?? "Plexi", maxEvaluations);
    }

    var id = options.Value("id") ?? throw new ArgumentException("fit needs --id <id> or --synthetic.");
    var entry = manifest.Find(id);
    var settings = Settings(entry, options.Value("type"), options.Assignments("set"))
        ?? throw new ArgumentException($"{id} has no circuit settings; pass --type.");
    var path = cache.TryLocate(entry) ?? throw new FileNotFoundException($"{id} is not in the cache ({cache.Root}).");
    var tolerances = options.All("tol").Select(ParseTolerance).ToList();
    if (tolerances.Count == 0) {
        throw new ArgumentException("fit needs at least one --tol <param>:<min>:<max>[:<nominal>].");
    }

    using var reference = ReferenceModels.Open(entry, path);
    using var circuit = new CircuitAmpRenderer(reference.SampleRate);
    var dbu = options.Number("input-dbu")
        ?? entry.Rig.InputDbu ?? ReferenceMatcher.FitInputDbu(reference, circuit, settings);
    Console.WriteLine(
        $"{id}: input calibration {dbu:0.0} dBu; fitting {string.Join(", ", tolerances.Select(static t => t.Name))}"
    );
    var fit = ToleranceFit.AgainstReference(reference, circuit, settings, tolerances, dbu);
    var result = fit.Run(maxEvaluations);
    PrintFit(tolerances, result, null);
    return 0;
}

static int FitSynthetic(string type, int maxEvaluations) {
    // The demonstration for the family streams: a circuit amp with perturbed values plays the reference, and the fit
    // starts from the stock values. It should land on the perturbation.
    const double Rate = 48_000;
    var settings = new CircuitSettings(type, new Dictionary<string, double> { ["gain"] = 6 });
    var truth = new Dictionary<string, double> { ["gain"] = 6.8, ["bias"] = 58, ["feedback"] = 4.2 };
    var tolerances = new List<Tolerance> {
        new("gain", 6, 4.5, 7.5), new("bias", 50, 35, 65), new("feedback", 5, 3, 7)
    };
    using var circuit = new CircuitAmpRenderer(Rate);
    var volts = MatchSignals.Performance(Rate, [0.05, 0.3], 0.25);
    var target = circuit.Render(volts, settings, truth);
    Console.WriteLine($"Synthetic reference: {CircuitAmpRenderer.Describe(settings, truth)}");
    var result = ToleranceFit.ForCircuitAmp(circuit, settings, tolerances, volts, target).Run(maxEvaluations);
    PrintFit(tolerances, result, truth);
    return 0;
}

static void PrintFit(
    IReadOnlyList<Tolerance> tolerances,
    FitResult result,
    IReadOnlyDictionary<string, double>? truth
) {
    Console.WriteLine(
        $"{"tolerance",-12} {"nominal",8} {"min",8} {"max",8} {"fitted",8}{(truth is null ? string.Empty : $" {"truth",8}")}"
    );
    foreach (var t in tolerances) {
        Console.WriteLine(
            $"{t.Name,-12} {t.Nominal,8:0.###} {t.Min,8:0.###} {t.Max,8:0.###} {result.Values[t.Name],8:0.###}"
            + (truth is null ? string.Empty : $" {truth[t.Name],8:0.###}")
        );
    }

    Console.WriteLine(
        $"score {result.NominalScore:0.0000} at nominal -> {result.Score:0.0000} fitted, {result.Evaluations} renders"
    );
    if (result.AtBound.Count > 0) {
        Console.WriteLine(
            $"at a bound: {string.Join(", ", result.AtBound)} (the cause may lie outside the tolerances)"
        );
    }
}

static Tolerance ParseTolerance(string text) {
    var parts = text.Split(':');
    if (parts.Length is < 3 or > 4) {
        throw new ArgumentException($"Expected --tol <param>:<min>:<max>[:<nominal>], got '{text}'.");
    }

    var min = double.Parse(parts[1], CultureInfo.InvariantCulture);
    var max = double.Parse(parts[2], CultureInfo.InvariantCulture);
    var nominal = parts.Length == 4 ? double.Parse(parts[3], CultureInfo.InvariantCulture) : (min + max) / 2;
    return new(parts[0], nominal, min, max);
}

static IEnumerable<ReferenceEntry> Select(ReferenceManifest manifest, Options options) {
    var ids = options.All("id");
    var family = options.Value("family");
    return manifest.References.Where(e =>
        (ids.Count == 0 || ids.Contains(e.Id, StringComparer.OrdinalIgnoreCase))
        && (family is null || string.Equals(e.Family, family, StringComparison.OrdinalIgnoreCase))
    );
}

// The manifest's circuit settings, or the given type instead, with the overrides applied.
static CircuitSettings? Settings(ReferenceEntry entry, string? type, Dictionary<string, double> overrides) {
    if (entry.Circuit is null && type is null) {
        return null;
    }

    var parameters = new Dictionary<string, double>(entry.Circuit?.Params ?? new Dictionary<string, double>());
    foreach (var (id, value) in overrides) {
        parameters[id] = value;
    }

    return new(type ?? entry.Circuit!.Type, parameters);
}

static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
