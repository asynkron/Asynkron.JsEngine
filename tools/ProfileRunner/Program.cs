using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Microsoft.Extensions.Logging;
using ProfileRunner;

#pragma warning disable MA0047 // Top-level statements live in script-style Program.
#pragma warning disable MA0048 // File name matches project entry point, not nested types.

const string listCommand = "list";
const string memoryFlag = "--memory";
const string jsonOutputFlag = "--json-output";

var argList = new List<string>(args);
var runMemory = argList.Remove(memoryFlag);
var jsonOutputPath = ExtractJsonOutput(argList);

var profileKey = argList.Count > 0 ? argList[0] : "fib";
var repoRoot = FindRepoRoot();
var manifestPath = Path.Combine(repoRoot, "tools", "profile-manifest.json");
var manifest = LoadManifest(manifestPath);

if (string.Equals(profileKey, listCommand, StringComparison.OrdinalIgnoreCase))
{
    PrintProfiles(manifest);
    return;
}

if (!manifest.Profiles.TryGetValue(profileKey, out var profile))
{
    Console.Error.WriteLine($"Unknown profile: {profileKey}");
    Console.Error.WriteLine("Use 'list' to see available profiles.");
    return;
}

var scriptPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.ScriptsDir, profile.Script);
if (!File.Exists(scriptPath))
{
    Console.Error.WriteLine($"Script not found: {scriptPath}");
    return;
}

var script = File.ReadAllText(scriptPath);
var warmup = profile.Warmup > 0 ? profile.Warmup : 1;
var iterations = profile.Iterations > 0 ? profile.Iterations : 1;
var isAsync = string.Equals(profile.Mode, "async", StringComparison.OrdinalIgnoreCase);

var traceRealm = false;
var runsForAverage = iterations;
if (!string.IsNullOrWhiteSpace(profile.TraceRealmEnv))
{
    traceRealm = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(profile.TraceRealmEnv));
    if (traceRealm && profile.TraceRealmRuns > 0)
    {
        runsForAverage = profile.TraceRealmRuns;
    }
}

if (!string.IsNullOrWhiteSpace(profile.Header))
{
    var header = profile.Header
        .Replace("{runs}", runsForAverage.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{traceRealm}", traceRealm.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    Console.WriteLine(header);
}

if (runMemory)
{
    await RunMemoryProfileAsync(script, isAsync, profile, traceRealm, profileKey, jsonOutputPath);
    return;
}

if (profile.FreshEnginePerIteration)
{
    await RunWithFreshEnginesAsync(script, isAsync, profile, traceRealm, warmup, iterations, runsForAverage);
}
else
{
    await RunWithSharedEngineAsync(script, isAsync, profile, traceRealm, warmup, iterations, runsForAverage);
}

await Task.CompletedTask;

async Task RunWithSharedEngineAsync(
    string source,
    bool isAsyncRun,
    ProfileDefinition profile,
    bool traceRealm,
    int warmup,
    int iterations,
    int runsForAverage)
{
    await using var engine = CreateEngine(traceRealm);
    var parsed = engine.ParseProgram(source);

    for (var i = 0; i < warmup; i++)
    {
        await EvaluateAsync(engine, parsed, isAsyncRun);
    }

    var sw = profile.ShowTiming ? Stopwatch.StartNew() : null;
    for (var iter = 0; iter < iterations; iter++)
    {
        await EvaluateAsync(engine, parsed, isAsyncRun);
        if (profile.ShowProgress)
        {
            Console.Write(".");
        }
    }

    sw?.Stop();
    PrintCompletion(profile, sw?.ElapsedMilliseconds ?? 0, runsForAverage);
}

async Task RunWithFreshEnginesAsync(
    string source,
    bool isAsyncRun,
    ProfileDefinition profile,
    bool traceRealm,
    int warmup,
    int iterations,
    int runsForAverage)
{
    ProgramNode parsed;
    await using (var setupEngine = CreateEngine(traceRealm))
    {
        parsed = setupEngine.ParseProgram(source);
    }

    for (var i = 0; i < warmup; i++)
    {
        await using var warmEngine = CreateEngine(traceRealm);
        await EvaluateAsync(warmEngine, parsed, isAsyncRun);
    }

    var sw = profile.ShowTiming ? Stopwatch.StartNew() : null;
    for (var iter = 0; iter < iterations; iter++)
    {
        await using var engine = CreateEngine(traceRealm);
        await EvaluateAsync(engine, parsed, isAsyncRun);
        if (profile.ShowProgress)
        {
            Console.Write(".");
        }
    }

    sw?.Stop();
    PrintCompletion(profile, sw?.ElapsedMilliseconds ?? 0, runsForAverage);
}

async Task EvaluateAsync(JsEngine engine, ProgramNode parsed, bool isAsyncRun)
{
    if (isAsyncRun)
    {
        await engine.EvaluateAndAwait(parsed);
    }
    else
    {
        await engine.Evaluate(parsed);
    }
}

void PrintCompletion(ProfileDefinition profile, long elapsedMs, int runsForAverage)
{
    if (!profile.ShowTiming)
    {
        Console.WriteLine("Done");
        return;
    }

    if (profile.ShowProgress)
    {
        Console.WriteLine();
    }

    var avgMs = runsForAverage > 0 ? elapsedMs / (double)runsForAverage : 0d;
    var elapsedText = elapsedMs.ToString(CultureInfo.InvariantCulture);
    var avgText = avgMs.ToString("F2", CultureInfo.InvariantCulture);
    Console.WriteLine($"Done in {elapsedText}ms (avg {avgText}ms per iteration)");
}

async Task RunMemoryProfileAsync(
    string source,
    bool isAsyncRun,
    ProfileDefinition profile,
    bool traceRealm,
    string profileKey,
    string? jsonOutputPath)
{
    var iterations = profile.Iterations > 0 ? profile.Iterations : 100;
    var warmup = profile.Warmup > 0 ? profile.Warmup : 1;

    using var allocationListener = new AllocationEventListener();

    for (var i = 0; i < warmup; i++)
    {
        await using var warmEngine = CreateEngine(traceRealm);
        var warmParsed = warmEngine.ParseProgram(source);
        await EvaluateAsync(warmEngine, warmParsed, isAsyncRun);
    }

    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, true, true);

    var baselineAllocated = GC.GetAllocatedBytesForCurrentThread();
    var baselineGen0 = GC.CollectionCount(0);
    var baselineGen1 = GC.CollectionCount(1);
    var baselineGen2 = GC.CollectionCount(2);
    var baselineTotal = GC.GetTotalMemory(false);

    allocationListener.Reset();
    allocationListener.Start();

    var sw = Stopwatch.StartNew();
    for (var i = 0; i < iterations; i++)
    {
        await using var iterEngine = CreateEngine(traceRealm);
        var iterParsed = iterEngine.ParseProgram(source);
        await EvaluateAsync(iterEngine, iterParsed, isAsyncRun);
    }
    sw.Stop();

    allocationListener.Stop();

    var finalAllocated = GC.GetAllocatedBytesForCurrentThread();
    var finalGen0 = GC.CollectionCount(0);
    var finalGen1 = GC.CollectionCount(1);
    var finalGen2 = GC.CollectionCount(2);
    var finalTotal = GC.GetTotalMemory(false);

    var totalAllocated = finalAllocated - baselineAllocated;
    var perIterationBytes = iterations > 0 ? totalAllocated / iterations : 0;
    var gen0Collections = finalGen0 - baselineGen0;
    var gen1Collections = finalGen1 - baselineGen1;
    var gen2Collections = finalGen2 - baselineGen2;

    var perIterationTimeMs = iterations > 0 ? sw.ElapsedMilliseconds / (double)iterations : 0d;

    Console.WriteLine("=== ALLOCATION REPORT ===");
    Console.WriteLine();
    Console.WriteLine($"Iterations:           {iterations.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Total time:           {sw.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)} ms");
    Console.WriteLine(
        $"Per iteration:        {perIterationTimeMs.ToString("F2", CultureInfo.InvariantCulture)} ms");
    Console.WriteLine();
    Console.WriteLine($"Total allocated:      {FormatBytes(totalAllocated)}");
    Console.WriteLine($"Per iteration:        {FormatBytes(perIterationBytes)}");
    Console.WriteLine();
    Console.WriteLine($"GC Gen0 collections:  {gen0Collections.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine($"GC Gen1 collections:  {gen1Collections.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine($"GC Gen2 collections:  {gen2Collections.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine();
    Console.WriteLine($"Heap before:          {FormatBytes(baselineTotal)}");
    Console.WriteLine($"Heap after:           {FormatBytes(finalTotal)}");
    Console.WriteLine();

    Console.WriteLine("=== PER-PHASE BREAKDOWN (single iteration) ===");
    Console.WriteLine();

    GC.Collect(2, GCCollectionMode.Forced, true, true);

    await using var phaseEngine = CreateEngine(traceRealm);
    var parseStart = GC.GetAllocatedBytesForCurrentThread();
    var parsed = phaseEngine.ParseProgram(source);
    var parseEnd = GC.GetAllocatedBytesForCurrentThread();
    var parseAllocatedBytes = parseEnd - parseStart;
    Console.WriteLine($"Parse:                {FormatBytes(parseAllocatedBytes)}");

    var evalStart = GC.GetAllocatedBytesForCurrentThread();
    await EvaluateAsync(phaseEngine, parsed, isAsyncRun);
    var evalEnd = GC.GetAllocatedBytesForCurrentThread();
    var evalAllocatedBytes = evalEnd - evalStart;
    Console.WriteLine($"Evaluate:             {FormatBytes(evalAllocatedBytes)}");
    Console.WriteLine();

    var topAllocations = allocationListener.GetTopAllocations(50);
    allocationListener.PrintReport(topAllocations);

    if (!string.IsNullOrWhiteSpace(jsonOutputPath))
    {
        WriteMemoryProfileJson(
            jsonOutputPath,
            profileKey,
            profile.Name,
            profile.Description,
            iterations,
            sw.ElapsedMilliseconds,
            perIterationTimeMs,
            totalAllocated,
            perIterationBytes,
            gen0Collections,
            gen1Collections,
            gen2Collections,
            parseAllocatedBytes,
            evalAllocatedBytes,
            baselineTotal,
            finalTotal,
            topAllocations);
    }

    Console.WriteLine("=== END REPORT ===");
}

JsEngine CreateEngine(bool traceRealm)
{
    if (!traceRealm)
    {
        return new JsEngine();
    }

    var logger = new ConsoleLogger("ProfileRunner");
    return new JsEngine(new JsEngineOptions { Logger = logger });
}

static ProfileManifest LoadManifest(string manifestPath)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var root = doc.RootElement;
    if (!root.TryGetProperty("profiles", out var profilesElement))
    {
        throw new InvalidOperationException($"Manifest missing profiles: {manifestPath}");
    }

    var profiles = new Dictionary<string, ProfileDefinition>(StringComparer.OrdinalIgnoreCase);
    foreach (var profileProperty in profilesElement.EnumerateObject())
    {
        var profileElement = profileProperty.Value;
        var definition = new ProfileDefinition
        {
            Name = GetString(profileElement, "name", profileProperty.Name),
            Description = GetString(profileElement, "description", string.Empty),
            Script = GetString(profileElement, "script", string.Empty),
            Mode = GetString(profileElement, "mode", "sync"),
            Iterations = GetInt(profileElement, "iterations", 1),
            Warmup = GetInt(profileElement, "warmup", 1),
            ShowProgress = GetBool(profileElement, "show_progress", false),
            ShowTiming = GetBool(profileElement, "show_timing", false),
            FreshEnginePerIteration = GetBool(profileElement, "fresh_engine_per_iteration", false),
            Header = GetOptionalString(profileElement, "header"),
            TraceRealmEnv = GetOptionalString(profileElement, "trace_realm_env"),
            TraceRealmRuns = GetInt(profileElement, "trace_realm_runs", 0)
        };

        profiles[profileProperty.Name] = definition;
    }

    var scriptsDir = "profile-scripts";
    if (root.TryGetProperty("scripts_dir", out var scriptsDirElement))
    {
        scriptsDir = scriptsDirElement.GetString() ?? scriptsDir;
    }

    return new ProfileManifest
    {
        ScriptsDir = scriptsDir,
        Profiles = profiles
    };
}

static string GetString(JsonElement element, string propertyName, string fallback)
{
    return element.TryGetProperty(propertyName, out var value) ? value.GetString() ?? fallback : fallback;
}

static string? GetOptionalString(JsonElement element, string propertyName)
{
    return element.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
}

static int GetInt(JsonElement element, string propertyName, int fallback)
{
    return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
        ? result
        : fallback;
}

static bool GetBool(JsonElement element, string propertyName, bool fallback)
{
    return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True
        ? true
        : element.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.False
            ? false
            : fallback;
}

static void PrintProfiles(ProfileManifest manifest)
{
    foreach (var (key, profile) in manifest.Profiles.OrderBy(p => p.Key, StringComparer.Ordinal))
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{key,-16} {profile.Name} - {profile.Description}"));
    }
}

static string FindRepoRoot()
{
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(current))
    {
        var manifestPath = Path.Combine(current, "tools", "profile-manifest.json");
        if (File.Exists(manifestPath))
        {
            return current;
        }

        var parent = Directory.GetParent(current);
        if (parent == null)
        {
            break;
        }

        current = parent.FullName;
    }

    throw new InvalidOperationException("Unable to locate profile-manifest.json.");
}

static string? ExtractJsonOutput(List<string> argList)
{
    for (var i = 0; i < argList.Count; i++)
    {
        var arg = argList[i];
        if (string.Equals(arg, jsonOutputFlag, StringComparison.Ordinal))
        {
            if (i + 1 < argList.Count)
            {
                var value = argList[i + 1];
                argList.RemoveAt(i + 1);
                argList.RemoveAt(i);
                return value;
            }
        }

        if (arg.StartsWith(jsonOutputFlag + "=", StringComparison.Ordinal))
        {
            var value = arg[(jsonOutputFlag.Length + 1)..];
            argList.RemoveAt(i);
            return value;
        }
    }

    return null;
}

static void WriteMemoryProfileJson(
    string outputPath,
    string profileKey,
    string profileName,
    string description,
    int iterations,
    long totalTimeMs,
    double perIterationTimeMs,
    long totalAllocatedBytes,
    long perIterationAllocatedBytes,
    int gen0Collections,
    int gen1Collections,
    int gen2Collections,
    long parseAllocatedBytes,
    long evalAllocatedBytes,
    long heapBeforeBytes,
    long heapAfterBytes,
    IReadOnlyList<AllocationEventListener.AllocationInfo> allocations)
{
    var allocationEntries = new List<Dictionary<string, object?>>(allocations.Count);
    long allocationTotalBytes = 0;
    long allocationTotalCount = 0;

    foreach (var allocation in allocations)
    {
        allocationTotalBytes += allocation.TotalBytes;
        allocationTotalCount += allocation.Count;

        allocationEntries.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = allocation.TypeName,
            ["count"] = allocation.Count,
            ["total_bytes"] = allocation.TotalBytes,
            ["total"] = FormatBytes(allocation.TotalBytes)
        });
    }

    var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["profile_key"] = profileKey,
        ["profile_name"] = profileName,
        ["description"] = description,
        ["iterations"] = iterations,
        ["total_time"] = totalTimeMs.ToString(CultureInfo.InvariantCulture) + " ms",
        ["per_iteration_time"] = perIterationTimeMs.ToString("F2", CultureInfo.InvariantCulture) + " ms",
        ["total_allocated"] = FormatBytes(totalAllocatedBytes),
        ["per_iteration_allocated"] = FormatBytes(perIterationAllocatedBytes),
        ["gen0_collections"] = gen0Collections,
        ["gen1_collections"] = gen1Collections,
        ["gen2_collections"] = gen2Collections,
        ["parse_allocated"] = FormatBytes(parseAllocatedBytes),
        ["evaluate_allocated"] = FormatBytes(evalAllocatedBytes),
        ["heap_before"] = FormatBytes(heapBeforeBytes),
        ["heap_after"] = FormatBytes(heapAfterBytes),
        ["total_allocated_bytes"] = totalAllocatedBytes,
        ["per_iteration_allocated_bytes"] = perIterationAllocatedBytes,
        ["parse_allocated_bytes"] = parseAllocatedBytes,
        ["evaluate_allocated_bytes"] = evalAllocatedBytes,
        ["heap_before_bytes"] = heapBeforeBytes,
        ["heap_after_bytes"] = heapAfterBytes,
        ["allocation_total_count"] = allocationTotalCount,
        ["allocation_total_bytes"] = allocationTotalBytes,
        ["allocation_total"] = FormatBytes(allocationTotalBytes),
        ["allocation_by_type"] = allocationEntries
    };

    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var options = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    var json = JsonSerializer.Serialize(payload, options);
    File.WriteAllText(outputPath, json);
}

static string FormatBytes(long bytes)
{
    if (bytes < 1024)
    {
        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }

    if (bytes < 1024 * 1024)
    {
        return (bytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture) + " KB";
    }

    return (bytes / (1024.0 * 1024.0)).ToString("F2", CultureInfo.InvariantCulture) + " MB";
}

sealed class ProfileManifest
{
    public string ScriptsDir { get; init; } = "profile-scripts";

    public Dictionary<string, ProfileDefinition> Profiles { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

sealed class ProfileDefinition
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Script { get; init; } = string.Empty;

    public string Mode { get; init; } = "sync";

    public int Iterations { get; init; } = 1;

    public int Warmup { get; init; } = 1;

    public bool ShowProgress { get; init; }

    public bool ShowTiming { get; init; }

    public bool FreshEnginePerIteration { get; init; }

    public string? Header { get; init; }

    public string? TraceRealmEnv { get; init; }

    public int TraceRealmRuns { get; init; }
}

sealed class ConsoleLogger(string name) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{name}] {logLevel}: {message}"));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
