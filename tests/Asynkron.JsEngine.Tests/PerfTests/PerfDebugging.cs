using System.Diagnostics;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests.PerfTests;

public class PerfDebugging(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task RunForLoop()
    {
        var script = """
                     'use strict';
                     function run() {
                         let s = 0;
                         for (let i = 0; i < 10_000; i++) {
                             s += i;
                         }
                         return s;
                     }
                     run();
                     """;
        var sw = Stopwatch.StartNew();
        var engine = CreateEngineWithOptions(() => new JsEngineOptions());
        var result = await engine.Evaluate(script);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Execution took too long: {sw.ElapsedMilliseconds} ms");

    }

}
