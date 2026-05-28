using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests.PerfTests;

[Category(TestCategories.Performance)]
[Category(TestCategories.Debugging)]
public sealed class PerfDebugging(ITestOutputHelper output) : InternalTestBase(output)
{
    [Theory]
    [InlineData(0, 5, true)]
    [InlineData(0, 9999, false)]
    [InlineData(0, 10000, false)]
    [InlineData(100, 10000, false)]
    public async Task RunForLoop(int start, int end, bool useDebugLogger)
    {
        var script = CreateForLoopScript(start, end);
        var sw = Stopwatch.StartNew();
        var engine = CreateEngine(
            () => useDebugLogger
                ? new JsEngineOptions { Logger = new TestLogger(minLogLevel: LogLevel.Debug, xUnitOutput: output) }
                : new JsEngineOptions());
        await engine.Evaluate(script);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"Execution took too long: {sw.ElapsedMilliseconds} ms");
    }

    private static string CreateForLoopScript(int start, int end)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""
              'use strict'

              function run() {
                  let s = 0;
                  for (let i = {{start}}; i < {{end}}; i++) {
                      s += i;
                  }
                  return s;
              }
              run();
              """);
    }
}
