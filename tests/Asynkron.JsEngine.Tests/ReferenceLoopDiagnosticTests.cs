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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            var result = await engine.Evaluate("""
                let result = 0;
                for (let i = 0; i < 5; i = i + 1) {
                    result = result + i;
                }
                result;
                """, cts.Token);

            Assert.Equal(10.0, result);
        }
        catch (OperationCanceledException)
        {
            // Timeout - the loop is hanging
            var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
            output.WriteLine($"ReferencePath let loop TIMED OUT after 2s - {messages.Length} log messages");
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

        var finalMessages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
        output.WriteLine($"ReferencePath let loop - {finalMessages.Length} log messages");
        foreach (var msg in finalMessages.Take(20))
        {
            output.WriteLine(msg);
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
