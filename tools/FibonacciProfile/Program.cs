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

    // Reset counters after warmup
    Asynkron.JsEngine.Ast.TypedAstEvaluator.PreEvalPathCount = 0;
    Asynkron.JsEngine.Ast.TypedAstEvaluator.NormalPathCount = 0;
    Asynkron.JsEngine.Ast.TypedAstEvaluator.IdentifierSlotPathLocal = 0;
    Asynkron.JsEngine.Ast.TypedAstEvaluator.IdentifierSlotPathClosure = 0;
    Asynkron.JsEngine.Ast.TypedAstEvaluator.IdentifierDictionaryPath = 0;
    JsEnvironment.PathUninitialized = 0;
    JsEnvironment.PathLiveExport = 0;
    JsEnvironment.PathGlobalScope = 0;
    JsEnvironment.PathNormal = 0;

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

    // Debug output
    Console.WriteLine($"PreEval path: {Asynkron.JsEngine.Ast.TypedAstEvaluator.PreEvalPathCount:N0}");
    Console.WriteLine($"Normal path: {Asynkron.JsEngine.Ast.TypedAstEvaluator.NormalPathCount:N0}");

    // Identifier resolution path counters
    Console.WriteLine($"Identifier resolution paths:");
    Console.WriteLine($"  SlotPathLocal: {Asynkron.JsEngine.Ast.TypedAstEvaluator.IdentifierSlotPathLocal:N0}");
    Console.WriteLine($"  SlotPathClosure: {Asynkron.JsEngine.Ast.TypedAstEvaluator.IdentifierSlotPathClosure:N0}");
    Console.WriteLine($"  DictionaryPath: {Asynkron.JsEngine.Ast.TypedAstEvaluator.IdentifierDictionaryPath:N0}");

    // JsEnvironment ReadResolvedBindingJsValue path counters
    Console.WriteLine($"ReadResolvedBindingJsValue paths:");
    Console.WriteLine($"  PathUninitialized: {JsEnvironment.PathUninitialized:N0}");
    Console.WriteLine($"  PathLiveExport: {JsEnvironment.PathLiveExport:N0}");
    Console.WriteLine($"  PathGlobalScope: {JsEnvironment.PathGlobalScope:N0}");
    Console.WriteLine($"  PathNormal: {JsEnvironment.PathNormal:N0}");
}
