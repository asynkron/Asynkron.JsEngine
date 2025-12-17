using System.Diagnostics;
using Asynkron.JsEngine;
using Jint;

// Usage: dotnet run -- --jint    # Run with Jint
//        dotnet run -- --asynkron # Run with Asynkron (default)
var useJint = args.Contains("--jint", StringComparer.OrdinalIgnoreCase);

var script = """
    'use strict';
    function fib(n) {
        if (n <= 1) return n;
        return fib(n - 1) + fib(n - 2);
    }
    fib(25);
    """;

if (useJint)
{
    RunWithJint(script);
}
else
{
    await RunWithAsynkron(script);
}

void RunWithJint(string code)
{
    var engine = new Engine();

    // Warm up once.
    engine.Execute(code);

    var sw = Stopwatch.StartNew();
    // Run multiple times to accumulate allocation samples.
    for (var iter = 0; iter < 10; iter++)
    {
        engine.Execute(code);
        Console.Write(".");
    }
    sw.Stop();
    Console.WriteLine();
    Console.WriteLine($"Jint: Done in {sw.ElapsedMilliseconds}ms (avg {sw.ElapsedMilliseconds / 10}ms per iteration)");
}

async Task RunWithAsynkron(string code)
{
    var engine = new JsEngine();

    // Parse once so we time execution, not parse.
    var parsed = engine.ParseProgram(code);

    // Warm up once.
    await engine.Evaluate(parsed);

    var sw = Stopwatch.StartNew();
    // Run multiple times to accumulate allocation samples.
    for (var iter = 0; iter < 10; iter++)
    {
        await engine.Evaluate(parsed);
        Console.Write(".");
    }
    sw.Stop();
    Console.WriteLine();
    Console.WriteLine($"Asynkron: Done in {sw.ElapsedMilliseconds}ms (avg {sw.ElapsedMilliseconds / 10}ms per iteration)");
}
