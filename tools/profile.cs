#!/usr/bin/env dotnet run
#:package System.CommandLine@2.0.0-beta4.22272.1

using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;

// Colors for terminal output
const string ColorGreen = "\u001b[92m";
const string ColorYellow = "\u001b[93m";
const string ColorRed = "\u001b[91m";
const string ColorCyan = "\u001b[96m";
const string ColorBold = "\u001b[1m";
const string ColorEnd = "\u001b[0m";

void PrintHeader(string text)
{
    Console.WriteLine($"\n{ColorBold}{ColorGreen}{new string('=', 80)}{ColorEnd}");
    Console.WriteLine($"{ColorBold}{ColorGreen}{text}{ColorEnd}");
    Console.WriteLine($"{ColorBold}{ColorGreen}{new string('=', 80)}{ColorEnd}\n");
}

void PrintSection(string text)
{
    Console.WriteLine($"\n{ColorCyan}{ColorBold}{text}{ColorEnd}");
    Console.WriteLine(new string('-', 80));
}

(bool Success, string StdOut, string StdErr) RunCommand(string command, string? workingDir = null, int timeoutMs = 300000)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
            Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (workingDir != null)
            psi.WorkingDirectory = workingDir;

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        process.WaitForExit(timeoutMs);

        return (process.ExitCode == 0, stdout, stderr);
    }
    catch (Exception ex)
    {
        return (false, "", ex.Message);
    }
}

// Find repo root - start from current directory and walk up
var repoRoot = Environment.CurrentDirectory;
while (!Directory.Exists(Path.Combine(repoRoot, "src")) && repoRoot.Length > 1)
{
    var parent = Path.GetDirectoryName(repoRoot);
    if (parent == null || parent == repoRoot) break;
    repoRoot = parent;
}

var toolsDir = Path.Combine(repoRoot, "tools");
var outputDir = Path.Combine(toolsDir, "profile-output");
Directory.CreateDirectory(outputDir);

var profiles = new Dictionary<string, (string Name, string Dir, string Description)>
{
    ["fib"] = ("FibonacciProfile", Path.Combine(toolsDir, "FibonacciProfile"), "Recursive fibonacci(25) - tests recursion"),
    ["forloop"] = ("ForLoopProfile", Path.Combine(toolsDir, "ForLoopProfile"), "For loop with 1M iterations - tests loop performance"),
    ["simplearithmetic"] = ("SimpleArithmeticProfile", Path.Combine(toolsDir, "SimpleArithmeticProfile"), "Simple math operations - tests arithmetic"),
    ["whileloop"] = ("WhileLoopProfile", Path.Combine(toolsDir, "WhileLoopProfile"), "While loop with 100K iterations"),
    ["objectcreation"] = ("ObjectCreationProfile", Path.Combine(toolsDir, "ObjectCreationProfile"), "Create 10K objects with nested properties"),
    ["arrayops"] = ("ArrayOperationsProfile", Path.Combine(toolsDir, "ArrayOperationsProfile"), "Array map/filter/reduce operations"),
    ["stringops"] = ("StringOperationsProfile", Path.Combine(toolsDir, "StringOperationsProfile"), "String concat/split/join operations"),
    ["functioncalls"] = ("FunctionCallsProfile", Path.Combine(toolsDir, "FunctionCallsProfile"), "20K function calls in a loop"),
    ["closures"] = ("ClosuresProfile", Path.Combine(toolsDir, "ClosuresProfile"), "Closure creation and invocation"),
    ["recursion"] = ("RecursionProfile", Path.Combine(toolsDir, "RecursionProfile"), "Factorial and sum recursion"),
    ["propertyaccess"] = ("PropertyAccessProfile", Path.Combine(toolsDir, "PropertyAccessProfile"), "Deep property access in loops"),
    ["classdef"] = ("ClassDefinitionProfile", Path.Combine(toolsDir, "ClassDefinitionProfile"), "Class definition with inheritance"),
    ["destructuring"] = ("DestructuringProfile", Path.Combine(toolsDir, "DestructuringProfile"), "Object and array destructuring"),
    ["spread"] = ("SpreadOperatorProfile", Path.Combine(toolsDir, "SpreadOperatorProfile"), "Spread operator usage"),
    ["mapset"] = ("MapSetProfile", Path.Combine(toolsDir, "MapSetProfile"), "Map and Set operations"),
    ["json"] = ("JsonOperationsProfile", Path.Combine(toolsDir, "JsonOperationsProfile"), "JSON.stringify/parse operations"),
    ["regex"] = ("RegexOperationsProfile", Path.Combine(toolsDir, "RegexOperationsProfile"), "Regex match and replace"),
    ["promise"] = ("PromiseBasicProfile", Path.Combine(toolsDir, "PromiseBasicProfile"), "Promise.resolve and chaining"),
    ["asyncawait"] = ("AsyncAwaitProfile", Path.Combine(toolsDir, "AsyncAwaitProfile"), "Async/await with function calls"),
    ["generator"] = ("GeneratorFunctionProfile", Path.Combine(toolsDir, "GeneratorFunctionProfile"), "Generator function iteration"),
    ["asyncgen"] = ("AsyncGeneratorFunctionProfile", Path.Combine(toolsDir, "AsyncGeneratorFunctionProfile"), "Async generator function"),
    ["asyncforof"] = ("AsyncForOfProfile", Path.Combine(toolsDir, "AsyncForOfProfile"), "for await...of iteration"),
    ["asyncresolved"] = ("AsyncAwaitResolvedProfile", Path.Combine(toolsDir, "AsyncAwaitResolvedProfile"), "Await on resolved promises"),
    ["asyncpending"] = ("AsyncAwaitPendingProfile", Path.Combine(toolsDir, "AsyncAwaitPendingProfile"), "Await on pending promises"),
    ["forofiteration"] = ("ForOfIterationProfile", Path.Combine(toolsDir, "ForOfIterationProfile"), "Regular for...of iteration"),
};

bool Build(string profileDir, string profileName)
{
    Console.WriteLine($"Building {profileName}...");
    var (success, _, stderr) = RunCommand("dotnet build -c Release -v q --nologo", profileDir);

    if (!success)
    {
        Console.WriteLine($"{ColorRed}Build failed: {stderr}{ColorEnd}");
        return false;
    }

    Console.WriteLine($"{ColorGreen}Build successful{ColorEnd}");
    return true;
}

string? GetExecutable(string profileDir, string profileName)
{
    var exePath = Path.Combine(profileDir, "bin", "Release", "net10.0", profileName);
    if (OperatingSystem.IsWindows())
        exePath += ".exe";
    return File.Exists(exePath) ? exePath : null;
}

Dictionary<string, object>? CpuProfile(string profileKey)
{
    var (name, dir, description) = profiles[profileKey];
    var exePath = GetExecutable(dir, name);

    if (exePath == null)
    {
        Console.WriteLine($"{ColorRed}Executable not found. Run build first.{ColorEnd}");
        return null;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var traceFile = Path.Combine(outputDir, $"{name}_{timestamp}.nettrace");

    Console.WriteLine($"Running CPU profile on {exePath}...");

    var (success, stdout, stderr) = RunCommand(
        $"dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler --output \"{traceFile}\" -- \"{exePath}\"",
        timeoutMs: 120000);

    if (!success || !File.Exists(traceFile))
    {
        Console.WriteLine($"{ColorRed}Trace collection failed: {stderr}{ColorEnd}");
        return null;
    }

    // Convert to speedscope
    Console.WriteLine("Converting trace to speedscope format...");
    RunCommand($"dotnet-trace convert \"{traceFile}\" --format Speedscope");

    // Find the speedscope file
    var speedscopeFiles = Directory.GetFiles(outputDir, $"{name}_{timestamp}*.json");
    if (speedscopeFiles.Length == 0)
    {
        Console.WriteLine($"{ColorRed}Speedscope conversion failed{ColorEnd}");
        return null;
    }

    return AnalyzeSpeedscope(speedscopeFiles[0]);
}

Dictionary<string, object>? AnalyzeSpeedscope(string speedscopePath)
{
    var json = File.ReadAllText(speedscopePath);
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    var frames = root.GetProperty("shared").GetProperty("frames");
    var profile = root.GetProperty("profiles")[0];

    var frameTimes = new Dictionary<int, double>();
    var frameCounts = new Dictionary<int, int>();
    var callerCallee = new Dictionary<int, Dictionary<int, int>>();
    var stack = new List<(int FrameIdx, double At)>();

    if (profile.TryGetProperty("events", out var events))
    {
        foreach (var evt in events.EnumerateArray())
        {
            var eventType = evt.GetProperty("type").GetString();
            var frameIdx = evt.GetProperty("frame").GetInt32();
            var at = evt.GetProperty("at").GetDouble();

            if (eventType == "O") // Open
            {
                if (stack.Count > 0)
                {
                    var callerIdx = stack[^1].FrameIdx;
                    if (!callerCallee.ContainsKey(callerIdx))
                        callerCallee[callerIdx] = new Dictionary<int, int>();
                    callerCallee[callerIdx].TryGetValue(frameIdx, out var cnt);
                    callerCallee[callerIdx][frameIdx] = cnt + 1;
                }
                stack.Add((frameIdx, at));
                frameCounts.TryGetValue(frameIdx, out var count);
                frameCounts[frameIdx] = count + 1;
            }
            else if (eventType == "C") // Close
            {
                if (stack.Count > 0 && stack[^1].FrameIdx == frameIdx)
                {
                    var (_, openTime) = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    frameTimes.TryGetValue(frameIdx, out var time);
                    frameTimes[frameIdx] = time + (at - openTime);
                }
            }
        }
    }

    var allFunctions = new List<Dictionary<string, object>>();
    var jsEngineFunctions = new List<Dictionary<string, object>>();
    double totalTime = frameTimes.Values.Sum();
    double jsEngineTime = 0;

    var framesList = new List<string>();
    foreach (var frame in frames.EnumerateArray())
    {
        framesList.Add(frame.GetProperty("name").GetString() ?? "Unknown");
    }

    var sortedFrames = frameTimes.OrderByDescending(kv => kv.Value).ToList();

    foreach (var (frameIdx, timeSpent) in sortedFrames)
    {
        var name = frameIdx < framesList.Count ? framesList[frameIdx] : "Unknown";
        frameCounts.TryGetValue(frameIdx, out var calls);

        var entry = new Dictionary<string, object>
        {
            ["name"] = name,
            ["time_ms"] = timeSpent,
            ["calls"] = calls,
            ["frame_idx"] = frameIdx
        };

        allFunctions.Add(entry);

        if (name.Contains("Asynkron"))
        {
            jsEngineFunctions.Add(entry);
            jsEngineTime += timeSpent;
        }
    }

    return new Dictionary<string, object>
    {
        ["all_functions"] = allFunctions,
        ["jsengine_functions"] = jsEngineFunctions,
        ["total_time"] = totalTime,
        ["jsengine_time"] = jsEngineTime,
        ["frames"] = framesList,
        ["caller_callee"] = callerCallee,
        ["frame_counts"] = frameCounts
    };
}

void PrintCpuResults(Dictionary<string, object>? results, string profileKey)
{
    if (results == null)
    {
        Console.WriteLine($"{ColorRed}No results to display{ColorEnd}");
        return;
    }

    var (name, _, description) = profiles[profileKey];

    PrintSection($"CPU PROFILE: {name}");
    Console.WriteLine($"Description: {description}\n");

    var allFunctions = (List<Dictionary<string, object>>)results["all_functions"];
    var jsEngineFunctions = (List<Dictionary<string, object>>)results["jsengine_functions"];
    var totalTime = (double)results["total_time"];
    var jsEngineTime = (double)results["jsengine_time"];

    // Top functions overall
    Console.WriteLine($"{"Time (ms)",12} {"Calls",10}  {"Function"}");
    Console.WriteLine(new string('-', 100));

    foreach (var entry in allFunctions.Take(20))
    {
        var funcName = (string)entry["name"];
        if (funcName.Length > 75) funcName = funcName[..72] + "...";
        Console.WriteLine($"{(double)entry["time_ms"],12:F2} {(int)entry["calls"],10}  {funcName}");
    }

    // JsEngine functions
    PrintSection("JSENGINE HOT FUNCTIONS");
    Console.WriteLine($"{"Time (ms)",12} {"Calls",10}  {"Function"}");
    Console.WriteLine(new string('-', 100));

    foreach (var entry in jsEngineFunctions.Take(25))
    {
        var funcName = (string)entry["name"];
        if (funcName.Length > 75) funcName = funcName[..72] + "...";
        Console.WriteLine($"{(double)entry["time_ms"],12:F2} {(int)entry["calls"],10}  {funcName}");
    }

    // Summary
    Console.WriteLine();
    var jsPct = totalTime > 0 ? 100 * jsEngineTime / totalTime : 0;
    Console.WriteLine($"{ColorBold}JsEngine time: {jsEngineTime:F2} ms ({jsPct:F1}% of total){ColorEnd}");
    Console.WriteLine($"{ColorBold}Total time: {totalTime:F2} ms{ColorEnd}");
}

void RunBenchmarks()
{
    PrintHeader("RUNNING JINT COMPARISON BENCHMARKS");

    var benchmarkDir = Path.Combine(repoRoot, "benchmarks", "Asynkron.JsEngine.Benchmarks");

    Console.WriteLine("This may take several minutes...");
    var (success, stdout, stderr) = RunCommand(
        "dotnet run -c Release -- jint --job short",
        benchmarkDir,
        600000);

    if (!success)
    {
        Console.WriteLine($"{ColorRed}Benchmarks failed: {stderr}{ColorEnd}");
        return;
    }

    // Find and display the results file
    var resultsDir = Path.Combine(benchmarkDir, "BenchmarkDotNet.Artifacts", "results");
    if (Directory.Exists(resultsDir))
    {
        var mdFiles = Directory.GetFiles(resultsDir, "*JintComparison*-github.md");
        if (mdFiles.Length > 0)
        {
            PrintSection("BENCHMARK RESULTS");
            Console.WriteLine(File.ReadAllText(mdFiles[0]));
        }
    }
}

// Command-line setup
var profileArg = new Argument<string>("profile", () => "fib",
    $"Profile to run. Options: {string.Join(", ", profiles.Keys)}, all");
var cpuOption = new Option<bool>("--cpu", "Run CPU profiling only");
var memoryOption = new Option<bool>("--memory", "Run memory profiling only");
var heapOption = new Option<bool>("--heap", "Capture heap snapshot");
var compareOption = new Option<bool>("--compare", "Run Jint comparison benchmarks");

var rootCommand = new RootCommand("JsEngine Profiler - CPU and Memory profiling for JsEngine benchmarks")
{
    profileArg,
    cpuOption,
    memoryOption,
    heapOption,
    compareOption
};

rootCommand.SetHandler((string profile, bool cpu, bool memory, bool heap, bool compare) =>
{
    if (compare)
    {
        RunBenchmarks();
        return;
    }

    List<string> profilesToRun;
    if (profile == "all")
    {
        profilesToRun = profiles.Keys.ToList();
    }
    else if (profiles.ContainsKey(profile))
    {
        profilesToRun = [profile];
    }
    else
    {
        Console.WriteLine($"Unknown profile: {profile}");
        Console.WriteLine($"Available profiles: {string.Join(", ", profiles.Keys)}");
        return;
    }

    // If specific flags are set, only run those; otherwise run cpu by default
    var runCpu = cpu || (!cpu && !memory && !heap);
    var runMemory = memory;
    var runHeap = heap;

    PrintHeader("JSENGINE PROFILER (C# File-Based App)");
    Console.WriteLine($"Profiles: {string.Join(", ", profilesToRun)}");
    Console.WriteLine($"CPU profiling: {(runCpu ? "Yes" : "No")}");
    Console.WriteLine($"Memory profiling: {(runMemory ? "Yes" : "No")}");
    Console.WriteLine($"Heap snapshot: {(runHeap ? "Yes" : "No")}");

    foreach (var profileKey in profilesToRun)
    {
        var (name, dir, _) = profiles[profileKey];
        PrintHeader($"PROFILING: {name}");

        if (!Build(dir, name))
            continue;

        if (runCpu)
        {
            var results = CpuProfile(profileKey);
            PrintCpuResults(results, profileKey);
        }

        if (runMemory)
        {
            Console.WriteLine($"{ColorYellow}Memory profiling not yet implemented in C# version{ColorEnd}");
        }

        if (runHeap)
        {
            Console.WriteLine($"{ColorYellow}Heap profiling not yet implemented in C# version{ColorEnd}");
        }
    }

    Console.WriteLine($"\n{ColorGreen}Profiling complete!{ColorEnd}");

}, profileArg, cpuOption, memoryOption, heapOption, compareOption);

return await rootCommand.InvokeAsync(args);
