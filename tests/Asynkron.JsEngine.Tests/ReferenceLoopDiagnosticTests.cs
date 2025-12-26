using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Diagnostic tests to investigate Reference mode for-loop hanging issue.
/// </summary>
public class ReferenceLoopDiagnosticTests(ITestOutputHelper output)
{
    [Fact]
    public async Task VarLoop_FastPath_ShouldWork()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await engine.Evaluate("""
            var result = 0;
            for (var i = 0; i < 5; i = i + 1) {
                result = result + i;
            }
            result;
            """, cts.Token);

        Assert.Equal(10.0, result);

        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
        output.WriteLine($"FastPath var loop - {messages.Length} log messages");
        foreach (var msg in messages.Take(20))
        {
            output.WriteLine(msg);
        }
    }

    [Fact]
    public async Task VarLoop_ReferencePath_ShouldWork()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            EnableFastPaths = false,
            DebugMode = true,
            Logger = logger
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await engine.Evaluate("""
            var result = 0;
            for (var i = 0; i < 5; i = i + 1) {
                result = result + i;
            }
            result;
            """, cts.Token);

        Assert.Equal(10.0, result);

        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
        output.WriteLine($"ReferencePath var loop - {messages.Length} log messages");
        foreach (var msg in messages.Take(20))
        {
            output.WriteLine(msg);
        }
    }

    [Fact]
    public async Task LetLoop_FastPath_ShouldWork()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await engine.Evaluate("""
            let result = 0;
            for (let i = 0; i < 5; i = i + 1) {
                result = result + i;
            }
            result;
            """, cts.Token);

        Assert.Equal(10.0, result);

        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
        output.WriteLine($"FastPath let loop - {messages.Length} log messages");
        foreach (var msg in messages.Take(20))
        {
            output.WriteLine(msg);
        }

        // Check for loop iteration logs
        var iterationLogs = messages.Where(m => m.Contains("Loop iteration")).ToArray();
        output.WriteLine($"Found {iterationLogs.Length} iteration logs");
        foreach (var log in iterationLogs)
        {
            output.WriteLine(log);
        }
    }

    [Fact]
    public async Task LetLoop_ReferencePath_ShouldWork()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            EnableFastPaths = false,
            DebugMode = true,
            Logger = logger
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            var result = await engine.Evaluate("""
                let result = 0;
                let guard = 0;
                for (let i = 0; i < 5; i = i + 1) {
                    guard = guard + 1;
                    if (guard > 10) {
                        break;
                    }
                    result = result + i;
                }
                JSON.stringify({ result, guard });
                """, cts.Token);

            var parsed = System.Text.Json.JsonDocument.Parse((string)result);
            var guardValue = parsed.RootElement.GetProperty("guard").GetInt32();
            var sumValue = parsed.RootElement.GetProperty("result").GetDouble();

            // If the loop behaved, guard is 5 and sum is 10. If it stalled, guard > 10 and sum likely wrong.
            if (guardValue > 10)
            {
                DumpLogs(output, logger.Collector.Snapshot().Select(r => r.Message).ToArray());
                Assert.True(false, $"Loop guard tripped at {guardValue}");
            }

            Assert.Equal(10.0, sumValue);
        }
        catch (OperationCanceledException)
        {
            // Timeout - the loop is hanging
            var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
            output.WriteLine($"ReferencePath let loop TIMED OUT after 5s - {messages.Length} log messages");

            var envLogs = messages.Where(m =>
                    m.Contains("for-iteration", StringComparison.Ordinal) ||
                    m.Contains("Reset(", StringComparison.Ordinal) ||
                    m.Contains("CreatePerIterationEnvironment", StringComparison.Ordinal) ||
                    m.Contains("Identifier slot", StringComparison.Ordinal))
                .ToArray();
            output.WriteLine($"Env logs ({envLogs.Length}):");
            foreach (var log in envLogs.Take(50))
            {
                output.WriteLine(log);
            }
            foreach (var msg in messages.Take(50))
            {
                output.WriteLine(msg);
            }

            // Check for loop iteration logs
            var iterationLogs = messages.Where(m => m.Contains("Loop iteration")).ToArray();
            output.WriteLine($"Found {iterationLogs.Length} iteration logs:");
            foreach (var log in iterationLogs.Take(20))
            {
                output.WriteLine(log);
            }

            throw new Exception($"Loop timed out. Found {iterationLogs.Length} iteration logs. See output for details.");
        }
        finally
        {
            var finalMessages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
            output.WriteLine($"ReferencePath let loop - {finalMessages.Length} log messages");
            DumpLogs(output, finalMessages);
        }
    }

    private static void DumpLogs(ITestOutputHelper output, string[] messages)
    {
        static string[] Filter(string[] source, string contains) =>
            source.Where(m => m.Contains(contains, StringComparison.Ordinal)).ToArray();

        var slotLogs = Filter(messages, "Identifier slot");
        var iterLogs = Filter(messages, "Loop iteration");
        var envLogs = messages.Where(m =>
                m.Contains("CreatePerIterationEnvironment", StringComparison.Ordinal) ||
                m.Contains("Reset per-iteration env reuse", StringComparison.Ordinal))
            .ToArray();

        var lines = new List<string>();
        lines.Add($"Slot logs ({slotLogs.Length}):");
        lines.AddRange(slotLogs);
        lines.Add($"Iteration logs ({iterLogs.Length}):");
        lines.AddRange(iterLogs);
        lines.Add($"Env logs ({envLogs.Length}):");
        lines.AddRange(envLogs);

        var path = Path.Combine(Path.GetTempPath(), $"ref-loop-logs-{Environment.ProcessId}.txt");
        try
        {
            File.WriteAllLines(path, lines);
            output.WriteLine($"Filtered logs written to {path} (exists: {File.Exists(path)})");
        }
        catch (Exception ex)
        {
            output.WriteLine($"Failed to write logs to {path}: {ex.Message}");
        }
        // Also echo a small subset inline for quick visibility
        foreach (var log in lines.Take(30))
        {
            output.WriteLine(log);
        }
    }

    [Fact]
    public async Task LetLoop_WhileStyle_ReferencePath_ShouldWork()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            EnableFastPaths = false,
            DebugMode = true,
            Logger = logger
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            var result = await engine.Evaluate("""
                let sum = 0;
                let i = 0;
                while (i < 5) {
                    sum = sum + i;
                    i = i + 1;
                }
                sum;
                """, cts.Token);

            Assert.Equal(10.0, result);
        }
        catch (OperationCanceledException)
        {
            var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
            output.WriteLine($"ReferencePath while loop TIMED OUT - {messages.Length} log messages");

            var iterationLogs = messages.Where(m => m.Contains("Loop iteration")).ToArray();
            output.WriteLine($"Found {iterationLogs.Length} iteration logs:");
            foreach (var log in iterationLogs.Take(20))
            {
                output.WriteLine(log);
            }

            throw new Exception($"While loop timed out. Found {iterationLogs.Length} iteration logs.");
        }
    }
}
