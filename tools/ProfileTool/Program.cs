using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Asynkron.JsEngine.Tools.ProfileTool;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Spectre.Console;
using Spectre.Console.Rendering;

void PrintHeader(string text)
{
    Console.WriteLine(text);
}

void PrintSection(string text)
{
    Console.WriteLine();
    Console.WriteLine(text);
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

var repoRoot = FindRepoRoot();
var toolsDir = Path.Combine(repoRoot, "tools");
var outputDir = Path.Combine(toolsDir, "profile-output");
Directory.CreateDirectory(outputDir);

if (Console.IsOutputRedirected)
{
    var capabilities = AnsiConsole.Profile.Capabilities;
    capabilities.Ansi = false;
    capabilities.Unicode = false;
    capabilities.Links = false;
    capabilities.Interactive = false;
    AnsiConsole.Profile.Capabilities = capabilities;
    AnsiConsole.Profile.Width = 200;
}

var manifestPath = Path.Combine(toolsDir, "profile-manifest.json");
var profiles = LoadManifest(manifestPath);

var runnerDir = Path.Combine(toolsDir, "ProfileRunner");
var runnerName = "ProfileRunner";

bool BuildRunner()
{
    return AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Building [yellow]{runnerName}[/]...", _ =>
        {
            var (success, _, stderr) = RunCommand("dotnet build -c Release -v q --nologo", runnerDir);

            if (!success)
            {
                AnsiConsole.MarkupLine($"[red]Build failed:[/] {Markup.Escape(stderr)}");
                return false;
            }

            AnsiConsole.MarkupLine($"[green]✓[/] Build successful");
            return true;
        });
}

string? GetExecutable()
{
    var exeName = runnerName + (OperatingSystem.IsWindows() ? ".exe" : "");
    var exePath = Path.Combine(runnerDir, "bin", "Release", "net10.0", exeName);
    return File.Exists(exePath) ? exePath : null;
}

CpuProfileResult? CpuProfile(string profileKey)
{
    var profile = profiles[profileKey];
    var exePath = GetExecutable();

    if (exePath == null)
    {
        AnsiConsole.MarkupLine("[red]Executable not found. Run build first.[/]");
        return null;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
    var traceFile = Path.Combine(outputDir, $"{profile.Name}_{timestamp}.nettrace");

    return AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Running CPU profile on [yellow]{profile.Name}[/]...", ctx =>
        {
            ctx.Status($"Collecting trace data...");
            var (success, _, stderr) = RunCommand(
                $"dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler --output \"{traceFile}\" -- \"{exePath}\" {profileKey}",
                timeoutMs: 120000);

            if (!success || !File.Exists(traceFile))
            {
                AnsiConsole.MarkupLine($"[red]Trace collection failed:[/] {Markup.Escape(stderr)}");
                return null;
            }

            ctx.Status("Converting trace to speedscope format...");
            RunCommand($"dotnet-trace convert \"{traceFile}\" --format Speedscope");

            var speedscopeFiles = Directory.GetFiles(outputDir, $"{profile.Name}_{timestamp}*.json");
            if (speedscopeFiles.Length == 0)
            {
                AnsiConsole.MarkupLine("[red]Speedscope conversion failed[/]");
                return null;
            }

            ctx.Status("Analyzing profile data...");
            return AnalyzeSpeedscope(speedscopeFiles[0]);
        });
}

CpuProfileResult? AnalyzeSpeedscope(string speedscopePath)
{
    var json = File.ReadAllText(speedscopePath);
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    var frames = root.GetProperty("shared").GetProperty("frames");
    var profile = root.GetProperty("profiles")[0];

    var framesList = new List<string>();
    foreach (var frame in frames.EnumerateArray())
    {
        framesList.Add(frame.GetProperty("name").GetString() ?? "Unknown");
    }

    var frameTimes = new Dictionary<int, double>();
    var frameSelfTimes = new Dictionary<int, double>();
    var frameCounts = new Dictionary<int, int>();
    var callTreeRoot = new CallTreeNode(-1, "Total");
    var stack = new List<(CallTreeNode Node, double Start, int FrameIdx)>();
    var hasLast = false;
    var lastAt = 0d;
    var callTreeTotal = 0d;

    if (profile.TryGetProperty("events", out var events))
    {
        foreach (var evt in events.EnumerateArray())
        {
            var eventType = evt.GetProperty("type").GetString();
            var frameIdx = evt.GetProperty("frame").GetInt32();
            var at = evt.GetProperty("at").GetDouble();

            if (hasLast && stack.Count > 0)
            {
                var topIdx = stack[^1].FrameIdx;
                frameSelfTimes.TryGetValue(topIdx, out var selfTime);
                var delta = at - lastAt;
                frameSelfTimes[topIdx] = selfTime + delta;
                stack[^1].Node.Self += delta;
            }

            hasLast = true;
            lastAt = at;

            if (string.Equals(eventType, "O", StringComparison.Ordinal)) // Open
            {
                var parentNode = stack.Count > 0 ? stack[^1].Node : callTreeRoot;
                var childNode = GetOrCreateCallTreeChild(parentNode, frameIdx, framesList);
                childNode.Calls += 1;
                stack.Add((childNode, at, frameIdx));
                frameCounts.TryGetValue(frameIdx, out var count);
                frameCounts[frameIdx] = count + 1;
            }
            else if (string.Equals(eventType, "C", StringComparison.Ordinal)) // Close
            {
                if (stack.Count > 0 && stack[^1].FrameIdx == frameIdx)
                {
                    var (node, openTime, _) = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    var duration = at - openTime;
                    frameTimes.TryGetValue(frameIdx, out var time);
                    frameTimes[frameIdx] = time + duration;
                    node.Total += duration;
                    if (stack.Count == 0)
                    {
                        callTreeTotal += duration;
                    }
                }
            }
        }
    }

    var allFunctions = new List<FunctionSample>();
    var jsEngineFunctions = new List<FunctionSample>();
    double totalTime = frameTimes.Values.Sum();
    double jsEngineTime = 0;

    if (callTreeTotal <= 0)
    {
        callTreeTotal = SumCallTreeTotals(callTreeRoot);
    }
    callTreeRoot.Total = callTreeTotal;
    callTreeRoot.Calls = SumCallTreeCalls(callTreeRoot);

    foreach (var (frameIdx, timeSpent) in frameTimes.OrderByDescending(kv => kv.Value))
    {
        var name = frameIdx < framesList.Count ? framesList[frameIdx] : "Unknown";
        frameCounts.TryGetValue(frameIdx, out var calls);

        var entry = new FunctionSample(name, timeSpent, calls, frameIdx);
        allFunctions.Add(entry);

        if (name.Contains("Asynkron", StringComparison.Ordinal))
        {
            jsEngineFunctions.Add(entry);
            jsEngineTime += timeSpent;
        }
    }

    return new CpuProfileResult(
        allFunctions,
        jsEngineFunctions,
        totalTime,
        jsEngineTime,
        callTreeRoot,
        callTreeTotal);
}

void PrintCpuResults(
    CpuProfileResult? results,
    string profileKey,
    string? rootFilter,
    string? functionFilter,
    bool includeRuntime,
    int callTreeDepth,
    int callTreeWidth,
    string? callTreeRootMode,
    bool showSelfTimeTree,
    int callTreeSiblingCutoffPercent)
{
    if (results == null)
    {
        AnsiConsole.MarkupLine("[red]No results to display[/]");
        return;
    }

    var profile = profiles[profileKey];
    var name = profile.Name;
    var description = profile.Description;

    PrintSection($"CPU PROFILE: {name}");
    AnsiConsole.MarkupLine($"[dim]{description}[/]");

    var allFunctions = results.AllFunctions;
    var jsEngineFunctions = results.JsEngineFunctions;
    var totalTime = results.TotalTime;
    var jsEngineTime = results.JsEngineTime;

    // Top functions overall - using Spectre table
    AnsiConsole.WriteLine();
    var filteredAll = allFunctions.Where(entry => MatchesFunctionFilter(entry.Name, functionFilter));
    if (!includeRuntime)
    {
        filteredAll = filteredAll.Where(entry => !IsRuntimeNoise(entry.Name));
    }
    var filteredList = filteredAll.ToList();

    var topTitle = includeRuntime && string.IsNullOrWhiteSpace(functionFilter)
        ? "Top Functions (All)"
        : "Top Functions (Filtered)";
    var table = new Table()
        .Border(TableBorder.None)
        .Title($"[bold]{topTitle}[/]")
        .AddColumn(new TableColumn("[yellow]Time (ms)[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Calls[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Function[/]"));

    foreach (var entry in filteredList.Take(15))
    {
        var funcName = FormatMethodDisplayName(entry.Name);
        if (funcName.Length > 70) funcName = funcName[..67] + "...";

        var timeMs = entry.TimeMs;
        var calls = entry.Calls;
        var timeMsText = timeMs.ToString("F2", CultureInfo.InvariantCulture);
        var callsText = calls.ToString("N0", CultureInfo.InvariantCulture);

        table.AddRow(
            $"[green]{timeMsText}[/]",
            $"[blue]{callsText}[/]",
            Markup.Escape(funcName)
        );
    }

    AnsiConsole.Write(table);
    var filteredOut = allFunctions.Count - filteredList.Count;
    if (filteredOut > 0)
    {
        var filteredOutText = filteredOut.ToString("N0", CultureInfo.InvariantCulture);
        AnsiConsole.MarkupLine(
            $"[dim]Filtered out {filteredOutText} runtime frames. Use --include-runtime to show all.[/]");
    }
    if (!string.IsNullOrWhiteSpace(functionFilter))
    {
        AnsiConsole.MarkupLine(
            $"[dim]Filter: {Markup.Escape(functionFilter)} (use --filter to change).[/]");
    }

    // JsEngine functions
    PrintSection("JsEngine Hot Functions");

    var jsTable = new Table()
        .Border(TableBorder.None)
        .AddColumn(new TableColumn("[yellow]Time (ms)[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Calls[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]% Total[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Function[/]"));

    foreach (var entry in jsEngineFunctions.Take(20))
    {
        var funcName = FormatMethodDisplayName(entry.Name);
        if (funcName.Length > 60) funcName = funcName[..57] + "...";

        var timeMs = entry.TimeMs;
        var calls = entry.Calls;
        var pct = totalTime > 0 ? 100 * timeMs / totalTime : 0;
        var timeMsText = timeMs.ToString("F2", CultureInfo.InvariantCulture);
        var callsText = calls.ToString("N0", CultureInfo.InvariantCulture);
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);

        jsTable.AddRow(
            $"[green]{timeMsText}[/]",
            $"[blue]{callsText}[/]",
            $"[cyan]{pctText}%[/]",
            $"[white]{Markup.Escape(funcName)}[/]"
        );
    }

    AnsiConsole.Write(jsTable);

    // Summary panel
    AnsiConsole.WriteLine();
    var jsPct = totalTime > 0 ? 100 * jsEngineTime / totalTime : 0;

    var summaryTable = new Table()
        .Border(TableBorder.None)
        .Title("[bold yellow]Summary[/]")
        .HideHeaders()
        .AddColumn("")
        .AddColumn("");

    var jsEngineTimeText = jsEngineTime.ToString("F2", CultureInfo.InvariantCulture);
    var jsPctText = jsPct.ToString("F1", CultureInfo.InvariantCulture);
    var totalTimeText = totalTime.ToString("F2", CultureInfo.InvariantCulture);
    var hotCountText = jsEngineFunctions.Count.ToString(CultureInfo.InvariantCulture);
    summaryTable.AddRow("[bold]JsEngine Time[/]", $"[green]{jsEngineTimeText} ms[/] ([cyan]{jsPctText}%[/] of total)");
    summaryTable.AddRow("[bold]Total Time[/]", $"[green]{totalTimeText} ms[/]");
    summaryTable.AddRow("[bold]Hot Functions[/]", $"[blue]{hotCountText}[/] JsEngine functions profiled");

    AnsiConsole.Write(summaryTable);

    var resolvedRoot = ResolveCallTreeRootFilter(results, rootFilter);
    AnsiConsole.Write(BuildCallTree(
        results,
        useSelfTime: false,
        resolvedRoot,
        includeRuntime,
        callTreeDepth,
        callTreeWidth,
        callTreeRootMode,
        callTreeSiblingCutoffPercent));
    if (showSelfTimeTree)
    {
        AnsiConsole.Write(BuildCallTree(
            results,
            useSelfTime: true,
            resolvedRoot,
            includeRuntime,
            callTreeDepth,
            callTreeWidth,
            callTreeRootMode,
            callTreeSiblingCutoffPercent));
    }
}

MemoryProfileResult? MemoryProfile(string profileKey)
{
    var exePath = GetExecutable();
    if (exePath == null)
    {
        AnsiConsole.MarkupLine("[red]Executable not found. Run build first.[/]");
        return null;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    var jsonPath = Path.Combine(outputDir, $"{profileKey}_{timestamp}.memory.json");

    var (success, stdout, stderr) = RunCommand(
        $"\"{exePath}\" {profileKey} --memory --json-output \"{jsonPath}\"",
        timeoutMs: 180000);

    if (!success)
    {
        AnsiConsole.MarkupLine($"[red]Memory profile failed:[/] {Markup.Escape(stderr)}");
        return null;
    }

    if (File.Exists(jsonPath))
    {
        var results = ParseMemoryJson(jsonPath);
        return AttachAllocationCallTree(results, profileKey);
    }

    return AttachAllocationCallTree(ParseAllocationOutput(stdout), profileKey);
}

MemoryProfileResult? AttachAllocationCallTree(MemoryProfileResult? results, string profileKey)
{
    if (results == null)
    {
        return null;
    }

    var callTree = AllocationCallTree(profileKey);
    return results with { AllocationCallTree = callTree };
}

AllocationCallTreeResult? AllocationCallTree(string profileKey)
{
    var profile = profiles[profileKey];
    var exePath = GetExecutable();
    if (exePath == null)
    {
        AnsiConsole.MarkupLine("[red]Executable not found. Run build first.[/]");
        return null;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    var traceFile = Path.Combine(outputDir, $"{profile.Name}_{timestamp}.alloc.nettrace");

    return AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Collecting allocation trace for [yellow]{profile.Name}[/]...", ctx =>
        {
            ctx.Status("Collecting trace data...");
            var (success, _, stderr) = RunCommand(
                $"dotnet-trace collect --profile gc-verbose --output \"{traceFile}\" -- \"{exePath}\" {profileKey}",
                timeoutMs: 180000);

            if (!success || !File.Exists(traceFile))
            {
                AnsiConsole.MarkupLine($"[red]Allocation trace failed:[/] {Markup.Escape(stderr)}");
                return null;
            }

            ctx.Status("Analyzing allocation trace...");
            return AnalyzeAllocationTrace(traceFile);
        });
}

AllocationCallTreeResult? AnalyzeAllocationTrace(string traceFile)
{
    try
    {
        var typeRoots = new Dictionary<string, AllocationCallTreeNode>(StringComparer.Ordinal);
        long totalBytes = 0;
        long totalCount = 0;

        var etlxPath = traceFile;
        if (traceFile.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
        {
            var targetPath = Path.ChangeExtension(traceFile, ".etlx");
            var options = new TraceLogOptions { ConversionLog = TextWriter.Null };
            etlxPath = TraceLog.CreateFromEventPipeDataFile(traceFile, targetPath, options);
        }

        using var traceLog = TraceLog.OpenOrConvert(etlxPath, new TraceLogOptions { ConversionLog = TextWriter.Null });
        using var source = traceLog.Events.GetSource();
        source.Clr.GCAllocationTick += data =>
        {
            var bytes = data.AllocationAmount64;
            if (bytes <= 0)
            {
                return;
            }

            var typeName = string.IsNullOrWhiteSpace(data.TypeName) ? "Unknown" : data.TypeName;
            if (!typeRoots.TryGetValue(typeName, out var typeRoot))
            {
                typeRoot = new AllocationCallTreeNode(typeName);
                typeRoots[typeName] = typeRoot;
            }

            totalBytes += bytes;
            totalCount++;
            typeRoot.TotalBytes += bytes;
            typeRoot.Count++;

            var stack = data.CallStack();
            if (stack == null)
            {
                return;
            }

            var node = typeRoot;
            foreach (var frame in EnumerateAllocationFrames(stack))
            {
                if (string.IsNullOrWhiteSpace(frame))
                {
                    continue;
                }

                if (!node.Children.TryGetValue(frame, out var child))
                {
                    child = new AllocationCallTreeNode(frame);
                    node.Children[frame] = child;
                }

                child.TotalBytes += bytes;
                child.Count++;
                node = child;
            }
        };

        source.Process();

        var roots = typeRoots.Values
            .OrderByDescending(node => node.TotalBytes)
            .ToList();

        return new AllocationCallTreeResult(totalBytes, totalCount, roots);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[yellow]Allocation trace parse failed:[/] {Markup.Escape(ex.Message)}");
        return null;
    }
}

IEnumerable<string> EnumerateAllocationFrames(TraceCallStack stack)
{
    for (var current = stack; current != null; current = current.Caller)
    {
        var methodName = current.CodeAddress?.FullMethodName;
        if (string.IsNullOrWhiteSpace(methodName))
        {
            methodName = current.CodeAddress?.Method?.FullMethodName;
        }

        yield return methodName ?? "Unknown";
    }
}

MemoryProfileResult ParseMemoryJson(string jsonPath)
{
    var json = File.ReadAllText(jsonPath);
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    var allocationEntries = new List<AllocationEntry>();
    if (root.TryGetProperty("allocation_by_type", out var allocations) &&
        allocations.ValueKind == JsonValueKind.Array)
    {
        foreach (var entry in allocations.EnumerateArray())
        {
            var typeName = entry.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString() ?? "Unknown"
                : "Unknown";
            var count = entry.TryGetProperty("count", out var countElement) && countElement.TryGetInt64(out var countValue)
                ? countValue
                : 0;
            var totalText = entry.TryGetProperty("total", out var totalElement)
                ? totalElement.GetString() ?? string.Empty
                : string.Empty;

            allocationEntries.Add(new AllocationEntry(typeName, count, totalText));
        }
    }

    return new MemoryProfileResult(
        ReadJsonValue(root, "iterations"),
        ReadJsonValue(root, "total_time"),
        ReadJsonValue(root, "per_iteration_time"),
        ReadJsonValue(root, "total_allocated"),
        ReadJsonValue(root, "per_iteration_allocated"),
        ReadJsonValue(root, "gen0_collections"),
        ReadJsonValue(root, "gen1_collections"),
        ReadJsonValue(root, "gen2_collections"),
        ReadJsonValue(root, "parse_allocated"),
        ReadJsonValue(root, "evaluate_allocated"),
        ReadJsonValue(root, "heap_before"),
        ReadJsonValue(root, "heap_after"),
        ReadJsonValue(root, "allocation_total"),
        allocationEntries,
        null,
        null,
        null);
}

string? ReadJsonValue(JsonElement root, string key)
{
    if (!root.TryGetProperty(key, out var element))
    {
        return null;
    }

    return element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True or JsonValueKind.False => element.GetBoolean().ToString(),
        _ => null
    };
}

MemoryProfileResult ParseAllocationOutput(string output)
{
    string? iterations = null;
    string? totalTime = null;
    string? perIterationTime = null;
    string? totalAllocated = null;
    string? perIterationAllocated = null;
    string? gen0Collections = null;
    string? gen1Collections = null;
    string? gen2Collections = null;
    string? parseAllocated = null;
    string? evaluateAllocated = null;
    string? heapBefore = null;
    string? heapAfter = null;

    var allocationLines = new List<string>();
    var inAllocationByType = false;

    using var reader = new StringReader(output);
    string? line;
    while ((line = reader.ReadLine()) != null)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("=== ALLOCATION BY TYPE", StringComparison.Ordinal))
        {
            inAllocationByType = true;
            continue;
        }

        if (inAllocationByType)
        {
            if (trimmed.StartsWith("===", StringComparison.Ordinal))
            {
                inAllocationByType = false;
            }
            else
            {
                allocationLines.Add(line);
            }
        }

        if (trimmed.Length == 0)
        {
            continue;
        }

        var value = GetValueAfterColon(trimmed);
        if (value == null)
        {
            continue;
        }

        if (trimmed.StartsWith("Iterations:", StringComparison.Ordinal))
        {
            iterations = value;
        }
        else if (trimmed.StartsWith("Total time:", StringComparison.Ordinal))
        {
            totalTime = value;
        }
        else if (trimmed.StartsWith("Per iteration:", StringComparison.Ordinal))
        {
            if (value.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
            {
                perIterationTime = value;
            }
            else
            {
                perIterationAllocated = value;
            }
        }
        else if (trimmed.StartsWith("Total allocated:", StringComparison.Ordinal))
        {
            totalAllocated = value;
        }
        else if (trimmed.StartsWith("GC Gen0 collections:", StringComparison.Ordinal))
        {
            gen0Collections = value;
        }
        else if (trimmed.StartsWith("GC Gen1 collections:", StringComparison.Ordinal))
        {
            gen1Collections = value;
        }
        else if (trimmed.StartsWith("GC Gen2 collections:", StringComparison.Ordinal))
        {
            gen2Collections = value;
        }
        else if (trimmed.StartsWith("Parse:", StringComparison.Ordinal))
        {
            parseAllocated = value;
        }
        else if (trimmed.StartsWith("Evaluate:", StringComparison.Ordinal))
        {
            evaluateAllocated = value;
        }
        else if (trimmed.StartsWith("Heap before:", StringComparison.Ordinal))
        {
            heapBefore = value;
        }
        else if (trimmed.StartsWith("Heap after:", StringComparison.Ordinal))
        {
            heapAfter = value;
        }
    }

    var allocationByTypeRaw = allocationLines.Count > 0
        ? string.Join(Environment.NewLine, allocationLines).Trim()
        : null;

    return new MemoryProfileResult(
        iterations,
        totalTime,
        perIterationTime,
        totalAllocated,
        perIterationAllocated,
        gen0Collections,
        gen1Collections,
        gen2Collections,
        parseAllocated,
        evaluateAllocated,
        heapBefore,
        heapAfter,
        null,
        Array.Empty<AllocationEntry>(),
        null,
        allocationByTypeRaw,
        output);
}

string? GetValueAfterColon(string line)
{
    var idx = line.IndexOf(':');
    if (idx < 0 || idx == line.Length - 1)
    {
        return null;
    }

    return line[(idx + 1)..].Trim();
}

void PrintMemoryResults(
    MemoryProfileResult? results,
    string profileKey,
    string? callTreeRoot,
    bool includeRuntime,
    int callTreeDepth,
    int callTreeWidth,
    int callTreeSiblingCutoffPercent)
{
    if (results == null)
    {
        AnsiConsole.MarkupLine("[red]No results to display[/]");
        return;
    }

    var profile = profiles[profileKey];
    var name = profile.Name;
    var description = profile.Description;

    PrintSection($"MEMORY PROFILE: {name}");
    AnsiConsole.MarkupLine($"[dim]{description}[/]");

    var table = new Table()
        .Border(TableBorder.None)
        .AddColumn(new TableColumn("[yellow]Metric[/]"))
        .AddColumn(new TableColumn("[yellow]Value[/]"));

    var hasRows = false;

    void AddRow(string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            table.AddRow(label, Markup.Escape(value));
            hasRows = true;
        }
    }

    AddRow("Iterations", results.Iterations);
    AddRow("Total time", results.TotalTime);
    AddRow("Per iteration (time)", results.PerIterationTime);
    AddRow("Total allocated", results.TotalAllocated);
    AddRow("Per iteration (allocated)", results.PerIterationAllocated);
    AddRow("GC Gen0 collections", results.Gen0Collections);
    AddRow("GC Gen1 collections", results.Gen1Collections);
    AddRow("GC Gen2 collections", results.Gen2Collections);
    AddRow("Parse (allocated)", results.ParseAllocated);
    AddRow("Evaluate (allocated)", results.EvaluateAllocated);
    AddRow("Heap before", results.HeapBefore);
    AddRow("Heap after", results.HeapAfter);

    if (hasRows)
    {
        AnsiConsole.Write(table);
    }

    if (!string.IsNullOrWhiteSpace(results.AllocationByTypeRaw))
    {
        PrintSection("Allocation By Type (Sampled)");
        AnsiConsole.WriteLine(results.AllocationByTypeRaw);
    }
    else if (results.AllocationEntries.Count > 0)
    {
        PrintSection("Allocation By Type (Sampled)");
        PrintAllocationTable(results.AllocationEntries, results.AllocationTotal);
    }
    else if (!hasRows && !string.IsNullOrWhiteSpace(results.RawOutput))
    {
        AnsiConsole.WriteLine(results.RawOutput);
    }

    if (results.AllocationCallTree != null)
    {
        PrintSection("Allocation Call Tree (Sampled)");
        PrintAllocationCallTree(
            results.AllocationCallTree,
            callTreeRoot,
            includeRuntime,
            callTreeDepth,
            callTreeWidth,
            callTreeSiblingCutoffPercent);
    }
}

void PrintAllocationCallTree(
    AllocationCallTreeResult results,
    string? rootFilter,
    bool includeRuntime,
    int maxDepth,
    int maxWidth,
    int siblingCutoffPercent)
{
    maxDepth = Math.Max(1, maxDepth);
    maxWidth = Math.Max(1, maxWidth);
    siblingCutoffPercent = Math.Max(0, siblingCutoffPercent);

    var roots = FilterAllocationRoots(results.TypeRoots, rootFilter);
    var visibleRoots = GetVisibleAllocationRoots(roots, maxWidth, siblingCutoffPercent);
    var totalBytes = results.TotalBytes;

    if (visibleRoots.Count == 0)
    {
        AnsiConsole.MarkupLine("[dim]No allocation call stacks captured.[/]");
        return;
    }

    foreach (var root in visibleRoots)
    {
        var pct = totalBytes > 0 ? 100d * root.TotalBytes / totalBytes : 0d;
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
        var bytesText = FormatBytes(root.TotalBytes);
        var countText = root.Count.ToString("N0", CultureInfo.InvariantCulture);
        var header = $"{FormatTypeDisplayName(root.Name)} ({bytesText}, {pctText}%, {countText}x)";

        var tree = BuildAllocationCallTree(root, includeRuntime, maxDepth, maxWidth, siblingCutoffPercent);
        AnsiConsole.Write(new Rows(new Markup($"[bold yellow]{Markup.Escape(header)}[/]"), tree));
    }
}

IEnumerable<AllocationCallTreeNode> FilterAllocationRoots(
    IReadOnlyList<AllocationCallTreeNode> roots,
    string? rootFilter)
{
    if (string.IsNullOrWhiteSpace(rootFilter))
    {
        return roots;
    }

    var filter = rootFilter.Trim();
    return roots.Where(root =>
        root.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        FormatTypeDisplayName(root.Name).Contains(filter, StringComparison.OrdinalIgnoreCase));
}

IReadOnlyList<AllocationCallTreeNode> GetVisibleAllocationRoots(
    IEnumerable<AllocationCallTreeNode> roots,
    int maxWidth,
    int siblingCutoffPercent)
{
    var ordered = roots
        .OrderByDescending(root => root.TotalBytes)
        .ToList();

    if (ordered.Count == 0)
    {
        return ordered;
    }

    if (siblingCutoffPercent <= 0)
    {
        return ordered.Take(maxWidth).ToList();
    }

    var topBytes = ordered[0].TotalBytes;
    if (topBytes <= 0)
    {
        return ordered.Take(maxWidth).ToList();
    }

    var minBytes = topBytes * siblingCutoffPercent / 100d;
    return ordered
        .Where(root => root.TotalBytes >= minBytes)
        .Take(maxWidth)
        .ToList();
}

IRenderable BuildAllocationCallTree(
    AllocationCallTreeNode root,
    bool includeRuntime,
    int maxDepth,
    int maxWidth,
    int siblingCutoffPercent)
{
    var rootLabel = FormatAllocationCallTreeLine(root, root.TotalBytes, isRoot: true, isLeaf: false);
    var tree = new Tree(rootLabel)
    {
        Style = new Style(Color.Grey),
        Guide = new CompactTreeGuide()
    };
    var children = GetVisibleAllocationChildren(root, includeRuntime, maxWidth, siblingCutoffPercent);
    foreach (var child in children)
    {
        var isSpecialLeaf = ShouldStopAtLeaf(FormatMethodDisplayName(child.Name));
        var childChildren = !isSpecialLeaf
            ? GetVisibleAllocationChildren(child, includeRuntime, maxWidth, siblingCutoffPercent)
            : Array.Empty<AllocationCallTreeNode>();
        var isLeaf = isSpecialLeaf || maxDepth <= 1 || childChildren.Count == 0;

        var childNode = tree.AddNode(FormatAllocationCallTreeLine(child, root.TotalBytes, isRoot: false, isLeaf));
        if (!isSpecialLeaf)
        {
            AddAllocationCallTreeChildren(
                childNode,
                child,
                root.TotalBytes,
                includeRuntime,
                2,
                maxDepth,
                maxWidth,
                siblingCutoffPercent);
        }
    }

    return tree;
}

void AddAllocationCallTreeChildren(
    TreeNode parent,
    AllocationCallTreeNode node,
    long rootTotalBytes,
    bool includeRuntime,
    int depth,
    int maxDepth,
    int maxWidth,
    int siblingCutoffPercent)
{
    if (depth > maxDepth)
    {
        return;
    }

    var children = GetVisibleAllocationChildren(node, includeRuntime, maxWidth, siblingCutoffPercent);
    foreach (var child in children)
    {
        var nextDepth = depth + 1;
        var isSpecialLeaf = ShouldStopAtLeaf(FormatMethodDisplayName(child.Name));
        var childChildren = !isSpecialLeaf && nextDepth <= maxDepth
            ? GetVisibleAllocationChildren(child, includeRuntime, maxWidth, siblingCutoffPercent)
            : Array.Empty<AllocationCallTreeNode>();
        var isLeaf = isSpecialLeaf || nextDepth > maxDepth || childChildren.Count == 0;

        var childNode = parent.AddNode(FormatAllocationCallTreeLine(child, rootTotalBytes, isRoot: false, isLeaf));
        if (!isSpecialLeaf)
        {
            AddAllocationCallTreeChildren(
                childNode,
                child,
                rootTotalBytes,
                includeRuntime,
                nextDepth,
                maxDepth,
                maxWidth,
                siblingCutoffPercent);
        }
    }
}

IReadOnlyList<AllocationCallTreeNode> GetVisibleAllocationChildren(
    AllocationCallTreeNode node,
    bool includeRuntime,
    int maxWidth,
    int siblingCutoffPercent)
{
    var ordered = EnumerateVisibleAllocationChildren(node, includeRuntime)
        .OrderByDescending(child => child.TotalBytes)
        .ToList();

    if (ordered.Count == 0)
    {
        return ordered;
    }

    if (siblingCutoffPercent <= 0)
    {
        return ordered.Take(maxWidth).ToList();
    }

    var topBytes = ordered[0].TotalBytes;
    if (topBytes <= 0)
    {
        return ordered.Take(maxWidth).ToList();
    }

    var minBytes = topBytes * siblingCutoffPercent / 100d;
    return ordered
        .Where(child => child.TotalBytes >= minBytes)
        .Take(maxWidth)
        .ToList();
}

IEnumerable<AllocationCallTreeNode> EnumerateVisibleAllocationChildren(
    AllocationCallTreeNode node,
    bool includeRuntime)
{
    foreach (var child in node.Children.Values)
    {
        if (includeRuntime || !IsRuntimeNoise(child.Name))
        {
            yield return child;
            continue;
        }

        foreach (var grandChild in EnumerateVisibleAllocationChildren(child, includeRuntime))
        {
            yield return grandChild;
        }
    }
}

string FormatAllocationCallTreeLine(
    AllocationCallTreeNode node,
    long rootTotalBytes,
    bool isRoot,
    bool isLeaf)
{
    var bytes = node.TotalBytes;
    var pct = rootTotalBytes > 0 ? 100d * bytes / rootTotalBytes : 0d;
    var count = node.Count;
    var bytesText = FormatBytes(bytes);
    var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
    var countText = count.ToString("N0", CultureInfo.InvariantCulture);

    var displayName = isRoot ? FormatTypeDisplayName(node.Name) : FormatMethodDisplayName(node.Name);
    if (displayName.Length > 80)
    {
        displayName = displayName[..77] + "...";
    }

    var nameText = isRoot
        ? $"[white]{Markup.Escape(displayName)}[/]"
        : FormatCallTreeName(displayName, displayName, isLeaf);

    return $"[green]{bytesText}[/] [cyan]{pctText}%[/] [blue]{countText}x[/] {nameText}";
}

IRenderable BuildCallTree(
    CpuProfileResult results,
    bool useSelfTime,
    string? rootFilter,
    bool includeRuntime,
    int maxDepth,
    int maxWidth,
    string? rootMode,
    int siblingCutoffPercent)
{
    var callTreeRoot = results.CallTreeRoot;
    var totalTime = results.CallTreeTotal;
    var title = useSelfTime ? "Call Tree (Self Time)" : "Call Tree (Total Time)";
    maxDepth = Math.Max(1, maxDepth);
    maxWidth = Math.Max(1, maxWidth);
    siblingCutoffPercent = Math.Max(0, siblingCutoffPercent);

    var rootNode = callTreeRoot;
    var rootTotal = totalTime;
    if (!string.IsNullOrWhiteSpace(rootFilter))
    {
        var matches = FindCallTreeMatches(callTreeRoot, rootFilter);
        if (matches.Count > 0)
        {
            rootNode = SelectRootMatch(matches, includeRuntime, rootMode);
            rootTotal = GetCallTreeTime(rootNode, useSelfTime: false);
            title = $"{title} - root: {Markup.Escape(rootFilter)}";
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]No call tree nodes matched '{Markup.Escape(rootFilter)}'. Showing full tree.[/]");
        }
    }

    var rootLabel = FormatCallTreeLine(rootNode, rootTotal, useSelfTime, isRoot: true);
    var tree = new Tree(rootLabel)
    {
        Style = new Style(Color.Grey),
        Guide = new CompactTreeGuide()
    };
    var children = GetVisibleChildren(rootNode, includeRuntime, useSelfTime, maxWidth, siblingCutoffPercent);
    foreach (var child in children)
    {
        var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
        var isLeaf = isSpecialLeaf || maxDepth <= 1 ||
                     GetVisibleChildren(child, includeRuntime, useSelfTime, maxWidth, siblingCutoffPercent).Count == 0;
        var childNode = tree.AddNode(FormatCallTreeLine(child, rootTotal, useSelfTime, isRoot: false, isLeaf));
        if (!isSpecialLeaf)
        {
            AddCallTreeChildren(
                childNode,
                child,
                rootTotal,
                useSelfTime,
                includeRuntime,
                2,
                maxDepth,
                maxWidth,
                siblingCutoffPercent);
        }
    }

    return new Rows(
        new Markup($"[bold yellow]{title}[/]"),
        tree);
}

void AddCallTreeChildren(
    TreeNode parent,
    CallTreeNode node,
    double totalTime,
    bool useSelfTime,
    bool includeRuntime,
    int depth,
    int maxDepth,
    int maxWidth,
    int siblingCutoffPercent)
{
    if (depth > maxDepth)
    {
        return;
    }

    var children = GetVisibleChildren(node, includeRuntime, useSelfTime, maxWidth, siblingCutoffPercent);

    foreach (var child in children)
    {
        var nextDepth = depth + 1;
        var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
        var childChildren = !isSpecialLeaf && nextDepth <= maxDepth
            ? GetVisibleChildren(child, includeRuntime, useSelfTime, maxWidth, siblingCutoffPercent)
            : Array.Empty<CallTreeNode>();
        var isLeaf = isSpecialLeaf || nextDepth > maxDepth || childChildren.Count == 0;

        var childNode = parent.AddNode(FormatCallTreeLine(child, totalTime, useSelfTime, isRoot: false, isLeaf));
        if (!isSpecialLeaf)
        {
            AddCallTreeChildren(
                childNode,
                child,
                totalTime,
                useSelfTime,
                includeRuntime,
                depth + 1,
                maxDepth,
                maxWidth,
                siblingCutoffPercent);
        }
    }
}

string FormatCallTreeLine(
    CallTreeNode node,
    double totalTime,
    bool useSelfTime,
    bool isRoot,
    bool isLeaf = false)
{
    var matchName = GetCallTreeMatchName(node);
    var displayName = matchName;
    if (displayName.Length > 80)
    {
        displayName = displayName[..77] + "...";
    }

    var timeSpent = isRoot && useSelfTime
        ? GetCallTreeTime(node, useSelfTime: false)
        : GetCallTreeTime(node, useSelfTime);
    var calls = node.Calls;

    var pct = totalTime > 0 ? 100 * timeSpent / totalTime : 0;
    var timeText = timeSpent.ToString("F2", CultureInfo.InvariantCulture);
    var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
    var callsText = calls.ToString("N0", CultureInfo.InvariantCulture);
    var nameText = FormatCallTreeName(displayName, matchName, isLeaf);

    return $"[green]{timeText} ms[/] [cyan]{pctText}%[/] [blue]{callsText}x[/] {nameText}";
}

CallTreeNode GetOrCreateCallTreeChild(
    CallTreeNode parent,
    int frameIdx,
    IReadOnlyList<string> frames)
{
    if (!parent.Children.TryGetValue(frameIdx, out var child))
    {
        var name = frameIdx >= 0 && frameIdx < frames.Count ? frames[frameIdx] : "Unknown";
        child = new CallTreeNode(frameIdx, name);
        parent.Children[frameIdx] = child;
    }

    return child;
}

double GetCallTreeTime(CallTreeNode node, bool useSelfTime)
{
    return useSelfTime ? node.Self : node.Total;
}

double SumCallTreeTotals(CallTreeNode node)
{
    var sum = 0d;
    foreach (var child in node.Children.Values)
    {
        sum += child.Total;
    }
    return sum;
}

int SumCallTreeCalls(CallTreeNode node)
{
    var sum = 0;
    foreach (var child in node.Children.Values)
    {
        sum += child.Calls;
    }
    return sum;
}

List<CallTreeMatch> FindCallTreeMatches(CallTreeNode node, string filter)
{
    var matches = new List<CallTreeMatch>();
    var normalizedFilter = filter.Trim();
    if (normalizedFilter.Length == 0)
    {
        return matches;
    }

    var order = 0;
    void Visit(CallTreeNode current, int depth)
    {
        if (current.FrameIdx >= 0)
        {
            var displayName = FormatMethodDisplayName(current.Name);
            if (displayName.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
                current.Name.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(new CallTreeMatch(current, depth, order++));
            }
        }

        foreach (var child in current.Children.Values)
        {
            Visit(child, depth + 1);
        }
    }

    Visit(node, 0);
    return matches;
}

string? ResolveCallTreeRootFilter(CpuProfileResult results, string? rootFilter)
{
    if (!string.IsNullOrWhiteSpace(rootFilter))
    {
        return rootFilter;
    }

    const string defaultNamespace = "Asynkron.JsEngine";
    var matches = FindCallTreeMatches(results.CallTreeRoot, defaultNamespace);
    return matches.Count > 0 ? defaultNamespace : null;
}

CallTreeNode SelectRootMatch(List<CallTreeMatch> matches, bool includeRuntime, string? rootMode)
{
    if (matches.Count == 0)
    {
        throw new InvalidOperationException("No call tree matches available.");
    }

    var mode = NormalizeRootMode(rootMode);
    var candidates = includeRuntime
        ? matches
        : matches.Where(match => !IsRuntimeNoise(match.Node.Name)).ToList();
    if (candidates.Count == 0)
    {
        candidates = matches;
    }

    return mode switch
    {
        "first" or "shallowest" => candidates
            .OrderBy(match => match.Depth)
            .ThenBy(match => match.Order)
            .Select(match => match.Node)
            .First(),
        _ => candidates
            .OrderByDescending(match => GetCallTreeTime(match.Node, useSelfTime: false))
            .Select(match => match.Node)
            .First()
    };
}

string NormalizeRootMode(string? rootMode)
{
    if (string.IsNullOrWhiteSpace(rootMode))
    {
        return "hottest";
    }

    return rootMode.Trim().ToLowerInvariant();
}

IEnumerable<CallTreeNode> EnumerateVisibleChildren(CallTreeNode node, bool includeRuntime)
{
    foreach (var child in node.Children.Values)
    {
        if (includeRuntime || !IsRuntimeNoise(child.Name))
        {
            yield return child;
            continue;
        }

        foreach (var grandChild in EnumerateVisibleChildren(child, includeRuntime))
        {
            yield return grandChild;
        }
    }
}

void PrintAllocationTable(IReadOnlyList<AllocationEntry> entries, string? allocationTotal)
{
    if (entries.Count == 0)
    {
        return;
    }

    var table = new Table()
        .Border(TableBorder.None)
        .AddColumn(new TableColumn("[yellow]Type[/]"))
        .AddColumn(new TableColumn("[yellow]Count[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Total[/]").RightAligned());

    long totalCount = 0;

    foreach (var entry in entries)
    {
        var typeName = FormatTypeDisplayName(entry.Type);
        if (typeName.Length > 80)
        {
            typeName = typeName[..77] + "...";
        }
        var count = entry.Count;
        var totalText = entry.Total ?? string.Empty;

        totalCount += count;

        var countText = count.ToString("N0", CultureInfo.InvariantCulture);
        table.AddRow(
            $"[white]{Markup.Escape(typeName)}[/]",
            $"[blue]{Markup.Escape(countText)}[/]",
            $"[green]{Markup.Escape(totalText)}[/]");
    }

    if (!string.IsNullOrWhiteSpace(allocationTotal))
    {
        var countText = totalCount.ToString("N0", CultureInfo.InvariantCulture);
        table.AddRow(
            "[bold white]TOTAL (shown)[/]",
            $"[bold blue]{Markup.Escape(countText)}[/]",
            $"[bold green]{Markup.Escape(allocationTotal)}[/]");
    }

    AnsiConsole.Write(table);
}

string FormatMethodDisplayName(string rawName)
{
    if (string.IsNullOrWhiteSpace(rawName))
    {
        return rawName;
    }

    var name = rawName;
    if (name.Contains('!'))
    {
        name = name.Split('!')[^1];
    }

    var parenIdx = name.IndexOf('(');
    if (parenIdx > 0)
    {
        name = name[..parenIdx];
    }

    var lastDot = name.LastIndexOf('.');
    if (lastDot > 0 && lastDot < name.Length - 1)
    {
        var typePart = name[..lastDot].TrimEnd('.');
        var methodPart = name[(lastDot + 1)..];
        var compilerGenerated = FormatCompilerGeneratedMethod(typePart, methodPart);
        if (!string.IsNullOrWhiteSpace(compilerGenerated))
        {
            return compilerGenerated;
        }

        return $"{CleanTypeName(typePart)}.{methodPart}";
    }

    return CleanTypeName(name);
}

string FormatTypeDisplayName(string rawName)
{
    if (string.IsNullOrWhiteSpace(rawName))
    {
        return rawName;
    }

    return CleanTypeName(rawName);
}

string FormatBytes(long bytes)
{
    if (bytes < 1024)
    {
        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }

    if (bytes < 1024 * 1024)
    {
        return (bytes / 1024d).ToString("F2", CultureInfo.InvariantCulture) + " KB";
    }

    if (bytes < 1024L * 1024L * 1024L)
    {
        return (bytes / (1024d * 1024d)).ToString("F2", CultureInfo.InvariantCulture) + " MB";
    }

    return (bytes / (1024d * 1024d * 1024d)).ToString("F2", CultureInfo.InvariantCulture) + " GB";
}

string CleanTypeName(string name)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        return name;
    }

    var timeout = TimeSpan.FromMilliseconds(100);
    var normalized = Regex.Replace(
        name,
        @"\b(?:[A-Za-z_][A-Za-z0-9_]*\.)+(?<type>[A-Za-z_][A-Za-z0-9_]*)",
        "${type}",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        timeout);

    normalized = Regex.Replace(
        normalized,
        @"`\d+",
        "",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        timeout);

    const string arrayToken = "__ARRAY__";
    normalized = normalized.Replace("[]", arrayToken, StringComparison.Ordinal);
    normalized = normalized.Replace('[', '<').Replace(']', '>');
    normalized = normalized.Replace(arrayToken, "[]", StringComparison.Ordinal);
    normalized = normalized.Replace('+', '.');

    return normalized;
}

string? FormatCompilerGeneratedMethod(string typePart, string methodPart)
{
    if (string.IsNullOrWhiteSpace(typePart) || string.IsNullOrWhiteSpace(methodPart))
    {
        return null;
    }

    if (string.Equals(methodPart, "MoveNext", StringComparison.Ordinal))
    {
        var stateMethod = ExtractStateMachineMethodName(typePart);
        if (!string.IsNullOrWhiteSpace(stateMethod))
        {
            return $"StateMachine.{stateMethod}.MoveNext";
        }
    }

    var lambdaOwner = ExtractLambdaOwner(methodPart);
    if (!string.IsNullOrWhiteSpace(lambdaOwner) && IsDisplayClassType(typePart))
    {
        var outerType = ExtractOuterType(typePart);
        var prefix = string.IsNullOrWhiteSpace(outerType)
            ? string.Empty
            : CleanTypeName(outerType) + ".";
        return $"{prefix}{lambdaOwner} lambda";
    }

    if (!string.IsNullOrWhiteSpace(lambdaOwner))
    {
        var prefix = string.IsNullOrWhiteSpace(typePart)
            ? string.Empty
            : CleanTypeName(typePart) + ".";
        return $"{prefix}{lambdaOwner} lambda";
    }

    return null;
}

bool IsDisplayClassType(string typePart)
{
    return typePart.Contains("<>c__DisplayClass", StringComparison.Ordinal) ||
           typePart.Contains("+<>c", StringComparison.Ordinal);
}

string? ExtractStateMachineMethodName(string typePart)
{
    var localFunctionIndex = typePart.LastIndexOf("g__", StringComparison.Ordinal);
    if (localFunctionIndex >= 0)
    {
        var localStart = localFunctionIndex + 3;
        var localEnd = typePart.IndexOfAny(new[] { '|', '>' }, localStart);
        if (localEnd < 0)
        {
            localEnd = typePart.Length;
        }

        var name = typePart[localStart..localEnd];
        return TrimCompilerGeneratedName(name);
    }

    var methodEnd = typePart.LastIndexOf(">d__", StringComparison.Ordinal);
    if (methodEnd < 0)
    {
        methodEnd = typePart.LastIndexOf(">d", StringComparison.Ordinal);
    }

    if (methodEnd < 0)
    {
        methodEnd = typePart.LastIndexOf('>');
    }

    if (methodEnd < 0)
    {
        return null;
    }

    var methodStart = typePart.LastIndexOf('<', methodEnd);
    if (methodStart < 0 || methodStart + 1 >= methodEnd)
    {
        return null;
    }

    var methodName = typePart[(methodStart + 1)..methodEnd];
    return TrimCompilerGeneratedName(methodName);
}

string? ExtractLambdaOwner(string methodPart)
{
    if (string.IsNullOrWhiteSpace(methodPart))
    {
        return null;
    }

    var ownerStart = methodPart.IndexOf('<');
    var ownerEnd = methodPart.IndexOf('>');
    if (ownerStart < 0 || ownerEnd <= ownerStart)
    {
        return null;
    }

    var owner = methodPart[(ownerStart + 1)..ownerEnd];
    return string.IsNullOrWhiteSpace(owner) ? null : owner;
}

string ExtractOuterType(string typePart)
{
    var markerIndex = typePart.IndexOf("+<", StringComparison.Ordinal);
    if (markerIndex > 0)
    {
        return typePart[..markerIndex];
    }

    markerIndex = typePart.IndexOf("+<>c", StringComparison.Ordinal);
    if (markerIndex > 0)
    {
        return typePart[..markerIndex];
    }

    return typePart;
}

string? TrimCompilerGeneratedName(string name)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        return null;
    }

    var trimmed = name.Trim();
    while (trimmed.StartsWith("<", StringComparison.Ordinal) ||
           trimmed.EndsWith(">", StringComparison.Ordinal))
    {
        trimmed = trimmed.Trim('<', '>');
    }

    while (trimmed.EndsWith("$", StringComparison.Ordinal))
    {
        trimmed = trimmed[..^1];
        while (trimmed.EndsWith(">", StringComparison.Ordinal))
        {
            trimmed = trimmed.TrimEnd('>');
        }
    }

    return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
}

IReadOnlyList<CallTreeNode> GetVisibleChildren(
    CallTreeNode node,
    bool includeRuntime,
    bool useSelfTime,
    int maxWidth,
    int siblingCutoffPercent)
{
    var ordered = EnumerateVisibleChildren(node, includeRuntime)
        .OrderByDescending(child => GetCallTreeTime(child, useSelfTime))
        .ToList();

    if (ordered.Count == 0)
    {
        return ordered;
    }

    if (siblingCutoffPercent <= 0)
    {
        return ordered.Take(maxWidth).ToList();
    }

    var topTime = GetCallTreeTime(ordered[0], useSelfTime);
    if (topTime <= 0)
    {
        return ordered.Take(maxWidth).ToList();
    }

    var minTime = topTime * siblingCutoffPercent / 100d;
    return ordered
        .Where(child => GetCallTreeTime(child, useSelfTime) >= minTime)
        .Take(maxWidth)
        .ToList();
}

string FormatCallTreeName(string displayName, string matchName, bool isLeaf)
{
    var escaped = Markup.Escape(displayName);
    if (!isLeaf)
    {
        return $"[white]{escaped}[/]";
    }

    const string leafHighlightColor = "plum1";
    if (matchName.Contains("CastHelpers.", StringComparison.Ordinal))
    {
        return $"[{leafHighlightColor}]{escaped}[/]";
    }

    if (matchName.Contains("Array.Copy", StringComparison.Ordinal) ||
        matchName.Contains("Dictionary<__Canon,__Canon>.Resize", StringComparison.Ordinal) ||
        matchName.Contains("Buffer.BulkMoveWithWriteBarrier", StringComparison.Ordinal) ||
        matchName.Contains("SpanHelpers.SequenceEqual", StringComparison.Ordinal) ||
        matchName.Contains("HashSet<", StringComparison.Ordinal) ||
        matchName.Contains("Enumerable+ArrayWhereSelectIterator<", StringComparison.Ordinal) ||
        matchName.Contains("ImmutableDictionary<", StringComparison.Ordinal) ||
        matchName.Contains("SegmentedArrayBuilder<__Canon>.ToArray", StringComparison.Ordinal) ||
        matchName.Contains("__Canon", StringComparison.Ordinal))
    {
        return $"[{leafHighlightColor}]{escaped}[/]";
    }

    if (matchName.Contains("List<", StringComparison.Ordinal) &&
        matchName.EndsWith(".ToArray", StringComparison.Ordinal))
    {
        return $"[{leafHighlightColor}]{escaped}[/]";
    }

    return $"[white]{escaped}[/]";
}

string GetCallTreeMatchName(CallTreeNode node)
{
    return FormatMethodDisplayName(node.Name);
}

bool ShouldStopAtLeaf(string matchName)
{
    return matchName.Contains("CastHelpers.", StringComparison.Ordinal) ||
           matchName.Contains("Array.Copy", StringComparison.Ordinal) ||
           matchName.Contains("Dictionary<__Canon,__Canon>.Resize", StringComparison.Ordinal) ||
           matchName.Contains("Buffer.BulkMoveWithWriteBarrier", StringComparison.Ordinal) ||
           matchName.Contains("SpanHelpers.SequenceEqual", StringComparison.Ordinal) ||
           matchName.Contains("HashSet<", StringComparison.Ordinal) ||
           matchName.Contains("Enumerable+ArrayWhereSelectIterator<", StringComparison.Ordinal) ||
           matchName.Contains("ImmutableDictionary<", StringComparison.Ordinal) ||
           matchName.Contains("SegmentedArrayBuilder<__Canon>.ToArray", StringComparison.Ordinal) ||
           matchName.Contains("__Canon", StringComparison.Ordinal) ||
           (matchName.Contains("List<", StringComparison.Ordinal) &&
            matchName.EndsWith(".ToArray", StringComparison.Ordinal));
}

bool MatchesFunctionFilter(string name, string? filter)
{
    if (string.IsNullOrWhiteSpace(filter))
    {
        return true;
    }

    return name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
           FormatMethodDisplayName(name).Contains(filter, StringComparison.OrdinalIgnoreCase);
}

bool IsRuntimeNoise(string name)
{
    var trimmed = name.TrimStart();
    var formatted = FormatMethodDisplayName(trimmed);
    return trimmed.Contains("UNMANAGED_CODE_TIME", StringComparison.Ordinal) ||
           trimmed.Contains("(Non-Activities)", StringComparison.Ordinal) ||
           trimmed.Contains("Thread", StringComparison.Ordinal) ||
           trimmed.Contains("Threads", StringComparison.Ordinal) ||
           trimmed.Contains("Process", StringComparison.Ordinal) ||
           StartsWithDigits(trimmed) ||
           StartsWithDigits(formatted) ||
           trimmed.StartsWith("Program.", StringComparison.Ordinal);
}

bool StartsWithDigits(string name)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        return false;
    }

    var trimmed = name.TrimStart();
    return trimmed.Length > 0 && char.IsDigit(trimmed[0]);
}

HeapProfileResult? HeapProfile(string profileKey)
{
    var profile = profiles[profileKey];
    var name = profile.Name;
    var exePath = GetExecutable();
    if (exePath == null)
    {
        AnsiConsole.MarkupLine("[red]Executable not found. Run build first.[/]");
        return null;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    var gcdumpFile = Path.Combine(outputDir, $"{name}_{timestamp}.gcdump");

    AnsiConsole.MarkupLine("[dim]Capturing heap snapshot...[/]");

    using var proc = Process.Start(new ProcessStartInfo
    {
        FileName = exePath,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    });

    if (proc == null)
    {
        AnsiConsole.MarkupLine("[red]Failed to start process for heap snapshot.[/]");
        return null;
    }

    Thread.Sleep(500);

    var (success, stdout, stderr) = RunCommand(
        $"dotnet-gcdump collect -p {proc.Id.ToString(CultureInfo.InvariantCulture)} -o \"{gcdumpFile}\"",
        timeoutMs: 60000);

    proc.WaitForExit();

    if (!success || !File.Exists(gcdumpFile))
    {
        AnsiConsole.MarkupLine($"[red]GC dump collection failed:[/] {Markup.Escape(stderr)}");
        return null;
    }

    var (reportSuccess, reportOut, reportErr) = RunCommand(
        $"dotnet-gcdump report \"{gcdumpFile}\"",
        timeoutMs: 60000);

    if (reportSuccess)
    {
        return ParseGcdumpReport(reportOut);
    }

    AnsiConsole.MarkupLine($"[yellow]Could not parse gcdump, showing raw output:[/] {Markup.Escape(reportErr)}");
    return new HeapProfileResult(reportOut, Array.Empty<HeapTypeEntry>());
}

HeapProfileResult ParseGcdumpReport(string output)
{
    var types = new List<HeapTypeEntry>();
    using var reader = new StringReader(output);
    var inTable = false;

    string? line;
    while ((line = reader.ReadLine()) != null)
    {
        if (!inTable)
        {
            if (line.Contains("Size", StringComparison.Ordinal) &&
                line.Contains("Count", StringComparison.Ordinal) &&
                line.Contains("Type", StringComparison.Ordinal))
            {
                inTable = true;
            }
            continue;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            continue;
        }

        if (!TryParseLong(parts[0], out var size) || !TryParseLong(parts[1], out var count))
        {
            continue;
        }

        var typeName = string.Join(' ', parts.Skip(2));
        types.Add(new HeapTypeEntry(size, count, typeName));
    }

    return new HeapProfileResult(output, types);
}

bool TryParseLong(string input, out long value)
{
    return long.TryParse(
        input.Replace(",", "", StringComparison.Ordinal),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out value);
}

void PrintHeapResults(HeapProfileResult? results, string profileKey)
{
    if (results == null)
    {
        AnsiConsole.MarkupLine("[red]No results to display[/]");
        return;
    }

    var profile = profiles[profileKey];
    var name = profile.Name;
    var description = profile.Description;

    PrintSection($"HEAP SNAPSHOT: {name}");
    AnsiConsole.MarkupLine($"[dim]{description}[/]");

    if (results.Types.Count > 0)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .AddColumn(new TableColumn("[yellow]Size (bytes)[/]").RightAligned())
            .AddColumn(new TableColumn("[yellow]Count[/]").RightAligned())
            .AddColumn(new TableColumn("[yellow]Type[/]"));

        foreach (var entry in results.Types.Take(40))
        {
            var sizeText = entry.Size.ToString("N0", CultureInfo.InvariantCulture);
            var countText = entry.Count.ToString("N0", CultureInfo.InvariantCulture);
            var typeName = entry.Type.Length > 60 ? entry.Type[..57] + "..." : entry.Type;
            table.AddRow(sizeText, countText, Markup.Escape(typeName));
        }

        AnsiConsole.Write(table);

        PrintSection("JsEngine Types Only");
        var jsTypes = results.Types
            .Where(t => t.Type.Contains("Asynkron", StringComparison.Ordinal) ||
                        t.Type.Contains("JsEngine", StringComparison.Ordinal))
            .ToList();

        var jsTable = new Table()
            .Border(TableBorder.None)
            .AddColumn(new TableColumn("[yellow]Size (bytes)[/]").RightAligned())
            .AddColumn(new TableColumn("[yellow]Count[/]").RightAligned())
            .AddColumn(new TableColumn("[yellow]Type[/]"));

        long totalSize = 0;
        long totalCount = 0;
        foreach (var entry in jsTypes.Take(30))
        {
            var sizeText = entry.Size.ToString("N0", CultureInfo.InvariantCulture);
            var countText = entry.Count.ToString("N0", CultureInfo.InvariantCulture);
            var typeName = entry.Type.Length > 60 ? entry.Type[..57] + "..." : entry.Type;
            jsTable.AddRow(sizeText, countText, Markup.Escape(typeName));
            totalSize += entry.Size;
            totalCount += entry.Count;
        }

        AnsiConsole.Write(jsTable);
        var totalSizeText = totalSize.ToString("N0", CultureInfo.InvariantCulture);
        var totalCountText = totalCount.ToString("N0", CultureInfo.InvariantCulture);
        AnsiConsole.MarkupLine($"[bold]Total JsEngine:[/] {totalSizeText} bytes, {totalCountText} instances");
    }
    else if (!string.IsNullOrWhiteSpace(results.RawOutput))
    {
        AnsiConsole.WriteLine(results.RawOutput);
    }
}

Dictionary<string, ProfileDefinition> LoadManifest(string manifestPath)
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
        var name = profileProperty.Name;
        var description = string.Empty;

        if (profileElement.TryGetProperty("name", out var nameElement))
        {
            name = nameElement.GetString() ?? name;
        }

        if (profileElement.TryGetProperty("description", out var descElement))
        {
            description = descElement.GetString() ?? string.Empty;
        }

        profiles[profileProperty.Name] = new ProfileDefinition(name, description);
    }
    return profiles;
}

string FindRepoRoot()
{
    var current = Environment.CurrentDirectory;
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

void RunBenchmarks()
{
    PrintHeader("JINT COMPARISON BENCHMARKS");

    var benchmarkDir = Path.Combine(repoRoot, "benchmarks", "Asynkron.JsEngine.Benchmarks");

    AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start("[yellow]Running benchmarks (this may take several minutes)...[/]", _ =>
        {
            var (success, stdout, stderr) = RunCommand(
                "dotnet run -c Release -- jint --job short",
                benchmarkDir,
                600000);

            if (!success)
            {
                AnsiConsole.MarkupLine($"[red]Benchmarks failed:[/] {Markup.Escape(stderr)}");
                return;
            }

            var resultsDir = Path.Combine(benchmarkDir, "BenchmarkDotNet.Artifacts", "results");
            if (Directory.Exists(resultsDir))
            {
                var mdFiles = Directory.GetFiles(resultsDir, "*JintComparison*-github.md");
                if (mdFiles.Length > 0)
                {
                    PrintSection("BENCHMARK RESULTS");
                    AnsiConsole.WriteLine(File.ReadAllText(mdFiles[0]));
                }
            }
        });
}

void ShowAvailableProfiles()
{
    var table = new Table()
        .Border(TableBorder.None)
        .Title("[bold yellow]Available Profiles[/]")
        .AddColumn("[green]Key[/]")
        .AddColumn("[cyan]Name[/]")
        .AddColumn("[white]Description[/]");

    foreach (var (key, profile) in profiles.OrderBy(p => p.Key, StringComparer.Ordinal))
    {
        table.AddRow($"[green]{key}[/]", $"[cyan]{profile.Name}[/]", profile.Description);
    }

    AnsiConsole.Write(table);
}

// Command-line setup
var profileArg = new Argument<string>("profile", () => "fib",
    "Profile to run (use 'list' to see all, 'all' to run all)");
var cpuOption = new Option<bool>("--cpu", "Run CPU profiling only");
var memoryOption = new Option<bool>("--memory", "Run memory profiling only");
var heapOption = new Option<bool>("--heap", "Capture heap snapshot");
var callTreeRootOption = new Option<string?>("--root", "Filter call tree to a root method (substring match)");
var callTreeDepthOption = new Option<int>("--calltree-depth", () => 30, "Maximum call tree depth (default: 30)");
var callTreeWidthOption = new Option<int>("--calltree-width", () => 4, "Maximum children per node (default: 4)");
var callTreeRootModeOption = new Option<string?>("--root-mode", () => "hottest", "Root selection mode when multiple matches (hottest|shallowest|first)");
var callTreeSelfOption = new Option<bool>("--calltree-self", "Show self-time call tree in addition to total time");
var callTreeSiblingCutoffOption = new Option<int>("--calltree-sibling-cutoff", () => 5, "Hide siblings below X% of the top sibling (default: 5)");
var functionFilterOption = new Option<string?>("--filter", "Filter CPU function tables by substring (case-insensitive)");
var includeRuntimeOption = new Option<bool>("--include-runtime", "Include runtime/process frames in CPU tables and call tree");
var compareOption = new Option<bool>("--compare", "Run Jint comparison benchmarks");

var rootCommand = new RootCommand("JsEngine Profiler - CPU and Memory profiling for JsEngine benchmarks")
{
    profileArg,
    cpuOption,
    memoryOption,
    heapOption,
    callTreeRootOption,
    callTreeDepthOption,
    callTreeWidthOption,
    callTreeRootModeOption,
    callTreeSelfOption,
    callTreeSiblingCutoffOption,
    functionFilterOption,
    includeRuntimeOption,
    compareOption
};

rootCommand.SetHandler(context =>
{
    var profile = context.ParseResult.GetValueForArgument(profileArg);
    var cpu = context.ParseResult.GetValueForOption(cpuOption);
    var memory = context.ParseResult.GetValueForOption(memoryOption);
    var heap = context.ParseResult.GetValueForOption(heapOption);
    var callTreeRoot = context.ParseResult.GetValueForOption(callTreeRootOption);
    var callTreeDepth = context.ParseResult.GetValueForOption(callTreeDepthOption);
    var callTreeWidth = context.ParseResult.GetValueForOption(callTreeWidthOption);
    var callTreeRootMode = context.ParseResult.GetValueForOption(callTreeRootModeOption);
    var callTreeSelf = context.ParseResult.GetValueForOption(callTreeSelfOption);
    var callTreeSiblingCutoff = context.ParseResult.GetValueForOption(callTreeSiblingCutoffOption);
    var functionFilter = context.ParseResult.GetValueForOption(functionFilterOption);
    var includeRuntime = context.ParseResult.GetValueForOption(includeRuntimeOption);
    var compare = context.ParseResult.GetValueForOption(compareOption);

    if (compare)
    {
        RunBenchmarks();
        return;
    }

    if (string.Equals(profile, "list", StringComparison.OrdinalIgnoreCase))
    {
        ShowAvailableProfiles();
        return;
    }

    List<string> profilesToRun;
    if (string.Equals(profile, "all", StringComparison.OrdinalIgnoreCase))
    {
        profilesToRun = profiles.Keys.ToList();
    }
    else if (profiles.ContainsKey(profile))
    {
        profilesToRun = [profile];
    }
    else
    {
        AnsiConsole.MarkupLine($"[red]Unknown profile:[/] {profile}");
        AnsiConsole.MarkupLine("[dim]Use 'list' to see available profiles[/]");
        return;
    }

    // If specific flags are set, only run those; otherwise run cpu by default
    var runCpu = cpu || (!cpu && !memory && !heap);
    var runMemory = memory || (!cpu && !memory && !heap);
    var runHeap = heap;

    foreach (var profileKey in profilesToRun)
    {
        if (!BuildRunner())
            continue;

        if (runCpu)
        {
            Console.WriteLine($"{profileKey} - cpu");
            var results = CpuProfile(profileKey);
            PrintCpuResults(
                results,
                profileKey,
                callTreeRoot,
                functionFilter,
                includeRuntime,
                callTreeDepth,
                callTreeWidth,
                callTreeRootMode,
                callTreeSelf,
                callTreeSiblingCutoff);
        }

        if (runMemory)
        {
            Console.WriteLine($"{profileKey} - memory");
            var results = MemoryProfile(profileKey);
            PrintMemoryResults(
                results,
                profileKey,
                callTreeRoot,
                includeRuntime,
                callTreeDepth,
                callTreeWidth,
                callTreeSiblingCutoff);
        }

        if (runHeap)
        {
            Console.WriteLine($"{profileKey} - heap");
            var results = HeapProfile(profileKey);
            PrintHeapResults(results, profileKey);
        }
    }

});

return await rootCommand.InvokeAsync(args);
