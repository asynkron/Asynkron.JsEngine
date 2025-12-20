using System.Diagnostics.Tracing;
using System.Globalization;
using System.Runtime;
using System.Text.Json;
using System.Text.Json.Serialization;
using AllocationProfile;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;

Console.WriteLine("=== JsEngine Allocation Profiler ===");
Console.WriteLine();

using var allocationListener = new AllocationEventListener();

var profileKeyInput = args.Length > 0 ? args[0] : "fib";
var profileKey = NormalizeProfileKey(profileKeyInput);

var repoRoot = FindRepoRoot();
var manifestPath = Path.Combine(repoRoot, "tools", "profile-manifest.json");
var manifest = LoadManifest(manifestPath);

if (!manifest.Profiles.TryGetValue(profileKey, out var profile))
{
    Console.Error.WriteLine($"Unknown profile type: {profileKeyInput}");
    Console.Error.WriteLine("Use tools/profile.cs list for options.");
    return;
}

var scriptPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.ScriptsDir, profile.Script);
if (!File.Exists(scriptPath))
{
    Console.Error.WriteLine($"Script not found: {scriptPath}");
    return;
}

var script = File.ReadAllText(scriptPath);
var isAsync = string.Equals(profile.Mode, "async", StringComparison.OrdinalIgnoreCase);

Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Profile: {profileKey}"));
Console.WriteLine($"Script: {TrimForDisplay(script, 80)}...");
Console.WriteLine();

GC.Collect(2, GCCollectionMode.Forced, true, true);
GC.WaitForPendingFinalizers();
GC.Collect(2, GCCollectionMode.Forced, true, true);

var engine = new JsEngine();
var parsed = engine.ParseProgram(script);

await EvaluateAsync(engine, parsed, isAsync);
await engine.DisposeAsync();

var baselineAllocated = GC.GetAllocatedBytesForCurrentThread();
var baselineGen0 = GC.CollectionCount(0);
var baselineGen1 = GC.CollectionCount(1);
var baselineGen2 = GC.CollectionCount(2);
var baselineTotal = GC.GetTotalMemory(false);

const int iterations = 100;

Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Running {iterations} iterations..."));
Console.WriteLine();

allocationListener.Reset();
allocationListener.Start();

var sw = System.Diagnostics.Stopwatch.StartNew();

for (var i = 0; i < iterations; i++)
{
    await using var iterEngine = new JsEngine();
    var iterParsed = iterEngine.ParseProgram(script);
    await EvaluateAsync(iterEngine, iterParsed, isAsync);
}

sw.Stop();

allocationListener.Stop();

var finalAllocated = GC.GetAllocatedBytesForCurrentThread();
var finalGen0 = GC.CollectionCount(0);
var finalGen1 = GC.CollectionCount(1);
var finalGen2 = GC.CollectionCount(2);
var finalTotal = GC.GetTotalMemory(false);

var totalAllocated = finalAllocated - baselineAllocated;
var perIterationBytes = totalAllocated / iterations;
var gen0Collections = finalGen0 - baselineGen0;
var gen1Collections = finalGen1 - baselineGen1;
var gen2Collections = finalGen2 - baselineGen2;

Console.WriteLine("=== ALLOCATION REPORT ===");
Console.WriteLine();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Iterations:           {iterations}"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Total time:           {sw.ElapsedMilliseconds} ms"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"Per iteration:        {(sw.ElapsedMilliseconds / (double)iterations).ToString(\"F2\", CultureInfo.InvariantCulture)} ms"));
Console.WriteLine();
Console.WriteLine($"Total allocated:      {FormatBytes(totalAllocated)}");
Console.WriteLine($"Per iteration:        {FormatBytes(perIterationBytes)}");
Console.WriteLine();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"GC Gen0 collections:  {gen0Collections}"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"GC Gen1 collections:  {gen1Collections}"));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"GC Gen2 collections:  {gen2Collections}"));
Console.WriteLine();
Console.WriteLine($"Heap before:          {FormatBytes(baselineTotal)}");
Console.WriteLine($"Heap after:           {FormatBytes(finalTotal)}");
Console.WriteLine();

Console.WriteLine("=== PER-PHASE BREAKDOWN (single iteration) ===");
Console.WriteLine();

GC.Collect(2, GCCollectionMode.Forced, true, true);

await using var phaseEngine = new JsEngine();

var parseStart = GC.GetAllocatedBytesForCurrentThread();
var parsed2 = phaseEngine.ParseProgram(script);
var parseEnd = GC.GetAllocatedBytesForCurrentThread();
Console.WriteLine($"Parse:                {FormatBytes(parseEnd - parseStart)}");

var evalStart = GC.GetAllocatedBytesForCurrentThread();
await EvaluateAsync(phaseEngine, parsed2, isAsync);
var evalEnd = GC.GetAllocatedBytesForCurrentThread();
Console.WriteLine($"Evaluate:             {FormatBytes(evalEnd - evalStart)}");

Console.WriteLine();

allocationListener.PrintReport(50);

Console.WriteLine("=== END REPORT ===");

static string NormalizeProfileKey(string profileKey)
{
    return profileKey.ToLowerInvariant() switch
    {
        "fibonacci" => "fib",
        "for" => "forloop",
        "object" => "objectcreation",
        "closure" => "closures",
        _ => profileKey.ToLowerInvariant()
    };
}

static ProfileManifest LoadManifest(string manifestPath)
{
    var json = File.ReadAllText(manifestPath);
    var manifest = JsonSerializer.Deserialize<ProfileManifest>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });

    if (manifest == null)
    {
        throw new InvalidOperationException($"Failed to parse manifest: {manifestPath}");
    }

    return manifest;
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

static string TrimForDisplay(string script, int maxLength)
{
    var normalized = script.Replace("\r", string.Empty, StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal)
        .Trim();

    if (normalized.Length <= maxLength)
    {
        return normalized;
    }

    return normalized[..maxLength];
}

static async Task EvaluateAsync(JsEngine engine, JsProgram parsed, bool isAsync)
{
    if (isAsync)
    {
        await engine.EvaluateAndAwait(parsed);
    }
    else
    {
        await engine.Evaluate(parsed);
    }
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
    [JsonPropertyName("scripts_dir")]
    public string ScriptsDir { get; init; } = "profile-scripts";

    [JsonPropertyName("profiles")]
    public Dictionary<string, ProfileDefinition> Profiles { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

sealed class ProfileDefinition
{
    [JsonPropertyName("script")]
    public string Script { get; init; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "sync";
}
