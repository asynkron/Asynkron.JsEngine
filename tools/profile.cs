#!/usr/bin/env dotnet run
#:package System.CommandLine@2.0.0-beta4.22272.1
#:package Spectre.Console@0.49.1

using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using Spectre.Console;

void PrintHeader(string text)
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule($"[bold green]{text}[/]").RuleStyle("green").DoubleBorder());
    AnsiConsole.WriteLine();
}

void PrintSection(string text)
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule($"[bold cyan]{text}[/]").RuleStyle("cyan"));
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

var manifestPath = Path.Combine(toolsDir, "profile-manifest.json");
var manifest = LoadManifest(manifestPath);
var profiles = manifest.Profiles;

var runnerDir = Path.Combine(toolsDir, "ProfileRunner");
var runnerName = "ProfileRunner";

bool BuildRunner()
{
    return AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Building [yellow]{runnerName}[/]...", ctx =>
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

Dictionary<string, object>? CpuProfile(string profileKey)
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

            if (string.Equals(eventType, "O", StringComparison.Ordinal)) // Open
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
            else if (string.Equals(eventType, "C", StringComparison.Ordinal)) // Close
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
        AnsiConsole.MarkupLine("[red]No results to display[/]");
        return;
    }

    var profile = profiles[profileKey];
    var name = profile.Name;
    var description = profile.Description;

    PrintSection($"CPU PROFILE: {name}");
    AnsiConsole.MarkupLine($"[dim]{description}[/]");

    var allFunctions = (List<Dictionary<string, object>>)results["all_functions"];
    var jsEngineFunctions = (List<Dictionary<string, object>>)results["jsengine_functions"];
    var totalTime = (double)results["total_time"];
    var jsEngineTime = (double)results["jsengine_time"];

    // Top functions overall - using Spectre table
    AnsiConsole.WriteLine();
    var table = new Table()
        .Border(TableBorder.Rounded)
        .Title("[bold]Top Functions (All)[/]")
        .AddColumn(new TableColumn("[yellow]Time (ms)[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Calls[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Function[/]"));

    foreach (var entry in allFunctions.Take(15))
    {
        var funcName = (string)entry["name"];
        if (funcName.Length > 70) funcName = funcName[..67] + "...";

        var timeMs = (double)entry["time_ms"];
        var calls = (int)entry["calls"];
        var timeMsText = timeMs.ToString("F2", CultureInfo.InvariantCulture);
        var callsText = calls.ToString("N0", CultureInfo.InvariantCulture);

        table.AddRow(
            $"[green]{timeMsText}[/]",
            $"[blue]{callsText}[/]",
            Markup.Escape(funcName)
        );
    }

    AnsiConsole.Write(table);

    // JsEngine functions
    PrintSection("JsEngine Hot Functions");

    var jsTable = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn(new TableColumn("[yellow]Time (ms)[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Calls[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]% Total[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Function[/]"));

    foreach (var entry in jsEngineFunctions.Take(20))
    {
        var funcName = (string)entry["name"];
        // Extract just the method part for cleaner display
        if (funcName.Contains('!'))
            funcName = funcName.Split('!')[^1];
        if (funcName.Length > 60) funcName = funcName[..57] + "...";

        var timeMs = (double)entry["time_ms"];
        var calls = (int)entry["calls"];
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
        .Border(TableBorder.Double)
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
}

Dictionary<string, string>? MemoryProfile(string profileKey, int iterations = 50)
{
    var allocProfileDir = Path.Combine(toolsDir, "AllocationProfile");
    var allocProject = Path.Combine(allocProfileDir, "AllocationProfile.csproj");

    if (!File.Exists(allocProject))
    {
        AnsiConsole.MarkupLine("[yellow]AllocationProfile project not found, using basic GC stats[/]");
        return BasicMemoryProfile(profileKey, iterations);
    }

    var (buildSuccess, _, buildErr) = RunCommand("dotnet build -c Release -v q --nologo", allocProfileDir);
    if (!buildSuccess)
    {
        AnsiConsole.MarkupLine($"[yellow]AllocationProfile build failed, using basic GC stats:[/] {Markup.Escape(buildErr)}");
        return BasicMemoryProfile(profileKey, iterations);
    }

    var (success, stdout, stderr) = RunCommand(
        $"dotnet run -c Release --no-build -- {profileKey}",
        allocProfileDir,
        timeoutMs: 180000);

    if (!success)
    {
        AnsiConsole.MarkupLine($"[red]Memory profile failed:[/] {Markup.Escape(stderr)}");
        return null;
    }

    return ParseAllocationOutput(stdout);
}

Dictionary<string, string>? BasicMemoryProfile(string profileKey, int iterations)
{
    var exePath = GetExecutable();
    if (exePath == null)
    {
        AnsiConsole.MarkupLine("[red]Executable not found. Run build first.[/]");
        return null;
    }

    AnsiConsole.MarkupLine($"[dim]Running basic memory profile (iterations: {iterations.ToString(CultureInfo.InvariantCulture)})[/]");
    var (success, stdout, stderr) = RunCommand($"\"{exePath}\" {profileKey}", timeoutMs: 120000);

    if (!success)
    {
        AnsiConsole.MarkupLine($"[red]Basic memory profile failed:[/] {Markup.Escape(stderr)}");
    }

    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["raw_output"] = success ? stdout : $"{stdout}\n{stderr}"
    };
}

Dictionary<string, string> ParseAllocationOutput(string output)
{
    var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["raw_output"] = output
    };

    using var reader = new StringReader(output);
    string? line;
    while ((line = reader.ReadLine()) != null)
    {
        var trimmed = line.Trim();
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
            results["iterations"] = value;
        }
        else if (trimmed.StartsWith("Total time:", StringComparison.Ordinal))
        {
            results["total_time"] = value;
        }
        else if (trimmed.StartsWith("Per iteration:", StringComparison.Ordinal))
        {
            if (value.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
            {
                results["per_iteration_time"] = value;
            }
            else
            {
                results["per_iteration_allocated"] = value;
            }
        }
        else if (trimmed.StartsWith("Total allocated:", StringComparison.Ordinal))
        {
            results["total_allocated"] = value;
        }
        else if (trimmed.StartsWith("GC Gen0 collections:", StringComparison.Ordinal))
        {
            results["gen0_collections"] = value;
        }
        else if (trimmed.StartsWith("GC Gen1 collections:", StringComparison.Ordinal))
        {
            results["gen1_collections"] = value;
        }
        else if (trimmed.StartsWith("GC Gen2 collections:", StringComparison.Ordinal))
        {
            results["gen2_collections"] = value;
        }
        else if (trimmed.StartsWith("Heap before:", StringComparison.Ordinal))
        {
            results["heap_before"] = value;
        }
        else if (trimmed.StartsWith("Heap after:", StringComparison.Ordinal))
        {
            results["heap_after"] = value;
        }
    }

    return results;
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

void PrintMemoryResults(Dictionary<string, string>? results, string profileKey)
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
        .Border(TableBorder.Rounded)
        .AddColumn(new TableColumn("[yellow]Metric[/]"))
        .AddColumn(new TableColumn("[yellow]Value[/]"));

    var hasRows = false;

    void AddRow(string label, string key)
    {
        if (results.TryGetValue(key, out var value))
        {
            table.AddRow(label, Markup.Escape(value));
            hasRows = true;
        }
    }

    AddRow("Iterations", "iterations");
    AddRow("Total time", "total_time");
    AddRow("Per iteration (time)", "per_iteration_time");
    AddRow("Total allocated", "total_allocated");
    AddRow("Per iteration (allocated)", "per_iteration_allocated");
    AddRow("GC Gen0 collections", "gen0_collections");
    AddRow("GC Gen1 collections", "gen1_collections");
    AddRow("GC Gen2 collections", "gen2_collections");
    AddRow("Heap before", "heap_before");
    AddRow("Heap after", "heap_after");

    if (hasRows)
    {
        AnsiConsole.Write(table);
    }
    else if (results.TryGetValue("raw_output", out var rawOutput))
    {
        AnsiConsole.WriteLine(rawOutput);
    }
}

Dictionary<string, object>? HeapProfile(string profileKey)
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
    return new Dictionary<string, object>
    {
        ["raw_output"] = reportOut
    };
}

Dictionary<string, object> ParseGcdumpReport(string output)
{
    var results = new Dictionary<string, object>
    {
        ["raw_output"] = output
    };

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

    results["types"] = types;
    return results;
}

bool TryParseLong(string input, out long value)
{
    return long.TryParse(
        input.Replace(",", "", StringComparison.Ordinal),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out value);
}

void PrintHeapResults(Dictionary<string, object>? results, string profileKey)
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

    if (results.TryGetValue("types", out var typesObj) &&
        typesObj is List<HeapTypeEntry> types &&
        types.Count > 0)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[yellow]Size (bytes)[/]").RightAligned())
            .AddColumn(new TableColumn("[yellow]Count[/]").RightAligned())
            .AddColumn(new TableColumn("[yellow]Type[/]"));

        foreach (var entry in types.Take(40))
        {
            var sizeText = entry.Size.ToString("N0", CultureInfo.InvariantCulture);
            var countText = entry.Count.ToString("N0", CultureInfo.InvariantCulture);
            var typeName = entry.Type.Length > 60 ? entry.Type[..57] + "..." : entry.Type;
            table.AddRow(sizeText, countText, Markup.Escape(typeName));
        }

        AnsiConsole.Write(table);

        PrintSection("JsEngine Types Only");
        var jsTypes = types
            .Where(t => t.Type.Contains("Asynkron", StringComparison.Ordinal) ||
                        t.Type.Contains("JsEngine", StringComparison.Ordinal))
            .ToList();

        var jsTable = new Table()
            .Border(TableBorder.Rounded)
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
    else if (results.TryGetValue("raw_output", out var rawOutput))
    {
        AnsiConsole.WriteLine(rawOutput?.ToString() ?? "No output");
    }
}

ProfileManifest LoadManifest(string manifestPath)
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

        profiles[profileProperty.Name] = new ProfileDefinition
        {
            Name = name,
            Description = description
        };
    }

    return new ProfileManifest
    {
        Profiles = profiles
    };
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
        .Start("[yellow]Running benchmarks (this may take several minutes)...[/]", ctx =>
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
        .Border(TableBorder.Rounded)
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
    // Show banner
    AnsiConsole.Write(new FigletText("JsEngine Profiler")
        .Color(Color.Green));

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

    // Show configuration
    var configTable = new Table()
        .Border(TableBorder.Rounded)
        .Title("[bold]Configuration[/]")
        .HideHeaders()
        .AddColumn("")
        .AddColumn("");

    configTable.AddRow("[bold]Profiles[/]", string.Join(", ", profilesToRun.Select(p => $"[cyan]{p}[/]")));
    configTable.AddRow("[bold]CPU Profiling[/]", runCpu ? "[green]Yes[/]" : "[dim]No[/]");
    configTable.AddRow("[bold]Memory Profiling[/]", runMemory ? "[green]Yes[/]" : "[dim]No[/]");
    configTable.AddRow("[bold]Heap Snapshot[/]", runHeap ? "[green]Yes[/]" : "[dim]No[/]");

    AnsiConsole.Write(configTable);
    AnsiConsole.WriteLine();

    foreach (var profileKey in profilesToRun)
    {
        var profileInfo = profiles[profileKey];
        PrintHeader($"PROFILING: {profileInfo.Name}");
        AnsiConsole.MarkupLine($"[dim]{profileInfo.Description}[/]");
        AnsiConsole.WriteLine();

        if (!BuildRunner())
            continue;

        if (runCpu)
        {
            var results = CpuProfile(profileKey);
            PrintCpuResults(results, profileKey);
        }

        if (runMemory)
        {
            var results = MemoryProfile(profileKey);
            PrintMemoryResults(results, profileKey);
        }

        if (runHeap)
        {
            var results = HeapProfile(profileKey);
            PrintHeapResults(results, profileKey);
        }
    }

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[green]Profiling complete![/]").RuleStyle("green"));

}, profileArg, cpuOption, memoryOption, heapOption, compareOption);

return await rootCommand.InvokeAsync(args);

sealed class ProfileManifest
{
    public Dictionary<string, ProfileDefinition> Profiles { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

sealed class ProfileDefinition
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}

record HeapTypeEntry(long Size, long Count, string Type);
