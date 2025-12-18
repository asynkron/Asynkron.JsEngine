using System;
using System.Linq;
using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging.Testing;

namespace Asynkron.JsEngine.Tests;

public class IdentifierSlotLoggingTests
{
    [Fact]
    public async Task ForLoop_UsesSlotFastPathWithoutMisses()
    {
        var logger = new FakeLogger();
        await using var engine = new JsEngine(new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            'use strict';
            function run() {
                let s = 0;
                for (let i = 0; i < 10; i++) {
                    s += i;
                }
                return s;
            }
            run();
            """);

        Assert.Equal(45d, JsOps.ToNumber(result));
        var messages = logger.Collector.Snapshot().Select(r => r.Message).ToArray();
        var readMisses = messages.Where(m => m.Contains("Identifier slot read miss", StringComparison.Ordinal)).ToArray();
        var writeMisses = messages.Where(m => m.Contains("Identifier slot write miss", StringComparison.Ordinal)).ToArray();

        Assert.DoesNotContain(readMisses, m => m.Contains("name=s", StringComparison.Ordinal));
        Assert.DoesNotContain(readMisses, m => m.Contains("name=i", StringComparison.Ordinal));
        Assert.DoesNotContain(writeMisses, m => m.Contains("name=s", StringComparison.Ordinal));
        Assert.DoesNotContain(writeMisses, m => m.Contains("name=i", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Identifier slot read hit name=s", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Identifier slot write hit name=s", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Identifier slot read hit name=i", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Identifier slot write hit name=i", StringComparison.Ordinal));
    }
}
