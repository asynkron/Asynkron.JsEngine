using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests.PerfTests;

[Category(TestCategories.Performance)]
[Category(TestCategories.Debugging)]
public sealed class PerfDebugging(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task RunForLoop1()
    {
        var script = """
                     'use strict'

                     function run() {
                         let s = 0;
                         for (let i = 0; i < 5; i++) {
                             s += i;
                         }
                         return s;
                     }
                     run();
                     """;
        var sw = Stopwatch.StartNew();
        var engine = CreateEngine(() => new JsEngineOptions()
        {
            Logger = new TestLogger(minLogLevel: LogLevel.Debug, xUnitOutput: output)
        });
        await engine.Evaluate(script);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Execution took too long: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task RunForLoop2()
    {
        var script = """
                     'use strict'

                     function run() {
                         let s = 0;
                         for (let i = 0; i < 9999; i++) {
                             s += i;
                         }
                         return s;
                     }
                     run();
                     """;
        var sw = Stopwatch.StartNew();
        var engine = CreateEngine(() => new JsEngineOptions());
        await engine.Evaluate(script);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Execution took too long: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task RunForLoop3()
    {
        var script = """
                     'use strict'

                     function run() {
                         let s = 0;
                         for (let i = 0; i < 10000; i++) {
                             s += i;
                         }
                         return s;
                     }
                     run();
                     """;
        var sw = Stopwatch.StartNew();
        var engine = CreateEngine(() => new JsEngineOptions());
        await engine.Evaluate(script);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Execution took too long: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task RunForLoop4()
    {
        var script = """
                     'use strict'

                     function run() {
                         let s = 0;
                         for (let i = 100; i < 10000; i++) {
                             s += i;
                         }
                         return s;
                     }
                     run();
                     """;
        var sw = Stopwatch.StartNew();
        var engine = CreateEngine(() => new JsEngineOptions());
        await engine.Evaluate(script);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Execution took too long: {sw.ElapsedMilliseconds} ms");
    }
}
