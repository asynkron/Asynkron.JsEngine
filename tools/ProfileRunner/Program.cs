using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Microsoft.Extensions.Logging;

const string listCommand = "list";

var profileKey = args.Length > 0 ? args[0] : "fib";
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
