using System;
using System.Linq;
using System.Threading.Tasks;
using Asynkron.JsEngine;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class SetIterationDebugTest(ITestOutputHelper output)
{
    [Fact]
    public async Task Set_Constructor_WithArray_LogsIteratorLookup()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            EnableFastPaths = true,
            DebugMode = true,
            Logger = logger
        });

        try
        {
            var result = await engine.Evaluate(@"
                const arr = [1, 2, 3];
                const set = new Set(arr);
                set.size;
            ");
            output.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            output.WriteLine($"Exception: {ex.Message}");
        }

        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
        foreach (var msg in messages.Where(m => m.Contains("GetIterator", StringComparison.Ordinal)))
        {
            output.WriteLine(msg);
        }

        // Assert that we found Symbol.iterator
        var foundIterator = messages.Any(m => m.Contains("found Symbol.iterator", StringComparison.Ordinal));
        Assert.True(foundIterator, "Should have found Symbol.iterator on the array");
    }
}
