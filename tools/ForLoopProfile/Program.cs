using System.Diagnostics;
using Asynkron.JsEngine;
using Microsoft.Extensions.Logging;

// Minimal harness to stress the classic for-loop body without BenchmarkDotNet overhead.
var traceRealm = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JSENGINE_TRACE_REALM"));
var runs = traceRealm ? 2 : 20;
var engine = traceRealm
    ? new JsEngine(new JsEngineOptions { Logger = new ConsoleLogger("ForLoopProfile") })
    : new JsEngine();
Console.WriteLine(System.FormattableString.Invariant($"Runs={runs}, iterations=10, traceRealm={traceRealm}"));
// Wrap the loop in a function so the benchmark runs in a function scope, enabling slot-based
// locals instead of dictionary-backed globals.
var script = """
'use strict';
function run() {
    let s = 0;
    for (let i = 0; i < 10000; i++) {
        s += i;
    }
    return s;
}
run();
""";

// Parse once so we time execution + env churn, not parse.
var parsed = engine.ParseProgram(script);

// Warm up once.
await engine.Evaluate(parsed);

// Run multiple times to accumulate allocation samples.
var sw = Stopwatch.StartNew();
for (var iter = 0; iter < 20; iter++)
{
    await engine.Evaluate(parsed);
    Console.Write(".");
}
sw.Stop();

Console.WriteLine(System.FormattableString.Invariant($"Done in {sw.ElapsedMilliseconds}ms (avg {sw.ElapsedMilliseconds / runs}ms per iteration)"));

sealed class ConsoleLogger(string name) : ILogger
{
    private readonly string _name = name;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Console.Error.WriteLine(System.FormattableString.Invariant($"[{_name}] {logLevel}: {formatter(state, exception)}"));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
