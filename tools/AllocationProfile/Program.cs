using System.Diagnostics.Tracing;
using System.Runtime;
using AllocationProfile;
using Asynkron.JsEngine;

// Allocation profiler that tracks allocations during JS execution
// and outputs a detailed report

Console.WriteLine("=== JsEngine Allocation Profiler ===");
Console.WriteLine();

// Create allocation listener early
using var allocationListener = new AllocationEventListener();

var profileType = args.Length > 0 ? args[0] : "fib";

string script = profileType switch
{
    "fib" or "fibonacci" => """
        function fib(n) {
            if (n <= 1) return n;
            return fib(n - 1) + fib(n - 2);
        }
        fib(20);
        """,
    "for" or "forloop" => """
        for (let i = 0, s = 0; i < 100000; i++) { s += i; }
        """,
    "object" => """
        let objects = [];
        for (let i = 0; i < 1000; i++) {
            objects.push({ id: i, name: "item" + i, value: i * 2 });
        }
        objects.length;
        """,
    "closure" => """
        function makeCounter() {
            let count = 0;
            return function() { return ++count; };
        }
        let counters = [];
        for (let i = 0; i < 100; i++) {
            counters.push(makeCounter());
        }
        let sum = 0;
        for (let i = 0; i < 100; i++) {
            for (let j = 0; j < 10; j++) {
                sum += counters[i]();
            }
        }
        sum;
        """,
    _ => throw new ArgumentException($"Unknown profile type: {profileType}")
};

Console.WriteLine($"Profile: {profileType}");
Console.WriteLine($"Script: {script.Replace("\n", " ").Substring(0, Math.Min(80, script.Length))}...");
Console.WriteLine();

// Force full GC before starting
GC.Collect(2, GCCollectionMode.Forced, true, true);
GC.WaitForPendingFinalizers();
GC.Collect(2, GCCollectionMode.Forced, true, true);

var engine = new JsEngine();
var parsed = engine.ParseProgram(script);

// Warmup
await engine.Evaluate(parsed);
await engine.DisposeAsync();

// Record baseline
var baselineAllocated = GC.GetAllocatedBytesForCurrentThread();
var baselineGen0 = GC.CollectionCount(0);
var baselineGen1 = GC.CollectionCount(1);
var baselineGen2 = GC.CollectionCount(2);
var baselineTotal = GC.GetTotalMemory(false);

const int iterations = 100;

Console.WriteLine($"Running {iterations} iterations...");
Console.WriteLine();

// Start allocation tracking
allocationListener.Reset();
allocationListener.Start();

var sw = System.Diagnostics.Stopwatch.StartNew();

for (var i = 0; i < iterations; i++)
{
    // Create fresh engine each iteration to avoid let re-declaration issues
    await using var iterEngine = new JsEngine();
    var iterParsed = iterEngine.ParseProgram(script);
    await iterEngine.Evaluate(iterParsed);
}

sw.Stop();

// Stop allocation tracking
allocationListener.Stop();

// Record final
var finalAllocated = GC.GetAllocatedBytesForCurrentThread();
var finalGen0 = GC.CollectionCount(0);
var finalGen1 = GC.CollectionCount(1);
var finalGen2 = GC.CollectionCount(2);
var finalTotal = GC.GetTotalMemory(false);

// Calculate
var totalAllocated = finalAllocated - baselineAllocated;
var perIterationBytes = totalAllocated / iterations;
var gen0Collections = finalGen0 - baselineGen0;
var gen1Collections = finalGen1 - baselineGen1;
var gen2Collections = finalGen2 - baselineGen2;

Console.WriteLine("=== ALLOCATION REPORT ===");
Console.WriteLine();
Console.WriteLine($"Iterations:           {iterations}");
Console.WriteLine($"Total time:           {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"Per iteration:        {sw.ElapsedMilliseconds / (double)iterations:F2} ms");
Console.WriteLine();
Console.WriteLine($"Total allocated:      {FormatBytes(totalAllocated)}");
Console.WriteLine($"Per iteration:        {FormatBytes(perIterationBytes)}");
Console.WriteLine();
Console.WriteLine($"GC Gen0 collections:  {gen0Collections}");
Console.WriteLine($"GC Gen1 collections:  {gen1Collections}");
Console.WriteLine($"GC Gen2 collections:  {gen2Collections}");
Console.WriteLine();
Console.WriteLine($"Heap before:          {FormatBytes(baselineTotal)}");
Console.WriteLine($"Heap after:           {FormatBytes(finalTotal)}");
Console.WriteLine();

// Now let's do per-phase breakdown if possible
Console.WriteLine("=== PER-PHASE BREAKDOWN (single iteration) ===");
Console.WriteLine();

GC.Collect(2, GCCollectionMode.Forced, true, true);

// Create fresh engine for phase breakdown
await using var phaseEngine = new JsEngine();

// Parse phase
var parseStart = GC.GetAllocatedBytesForCurrentThread();
var parsed2 = phaseEngine.ParseProgram(script);
var parseEnd = GC.GetAllocatedBytesForCurrentThread();
Console.WriteLine($"Parse:                {FormatBytes(parseEnd - parseStart)}");

// Evaluate phase
var evalStart = GC.GetAllocatedBytesForCurrentThread();
await phaseEngine.Evaluate(parsed2);
var evalEnd = GC.GetAllocatedBytesForCurrentThread();
Console.WriteLine($"Evaluate:             {FormatBytes(evalEnd - evalStart)}");

Console.WriteLine();

// Print allocation by type report
allocationListener.PrintReport(50);

Console.WriteLine("=== END REPORT ===");

static string FormatBytes(long bytes)
{
    if (bytes < 1024) return $"{bytes} B";
    if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F2} KB";
    return $"{bytes / (1024.0 * 1024.0):F2} MB";
}
