using Asynkron.JsEngine.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Debug tests for completion value semantics to understand the actual behavior.
/// </summary>
public class CompletionValueDebugTests
{
    private readonly ITestOutputHelper _output;

    public CompletionValueDebugTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 10000)]
    public async Task ForLoop_EmptyBody_ReturnsUndefined()
    {
        var logger = new TestLogger(_output, "Test1", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // This is cptn-expr-expr-iter.js test case
        var result = await engine.Evaluate("eval('var runA; 1; for (runA = true; runA; runA = false) { }')");
        _output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task ForLoop_Simple_EmptyBody_ReturnsUndefined()
    {
        var logger = new TestLogger(_output, "TestSimple", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // Simplest case - just the for loop
        var result = await engine.Evaluate("eval('for (var i = 0; i < 1; i++) { }')");
        _output.WriteLine($"Simple for result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task ForLoop_ZeroIterations_ReturnsUndefined()
    {
        var logger = new TestLogger(_output, "TestZero", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // Loop that never executes body
        var result = await engine.Evaluate("eval('for (var i = 0; i < 0; i++) { 42; }')");
        _output.WriteLine($"Zero iterations result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task ForLoop_WithPrecedingStatement_EmptyBody()
    {
        var logger = new TestLogger(_output, "TestPrecedingStmt", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // Test 1: Just the for loop
        var r1 = await engine.Evaluate("eval('for (var runA = true; runA; runA = false) { }')");
        _output.WriteLine($"Test1 (just for): {r1?.ToString() ?? "null"} (type: {r1?.GetType().Name ?? "null"})");

        // Test 2: With preceding expression
        var r2 = await engine.Evaluate("eval('1; for (var runA = true; runA; runA = false) { }')");
        _output.WriteLine($"Test2 (1; for): {r2?.ToString() ?? "null"} (type: {r2?.GetType().Name ?? "null"})");

        // Test 3: With var declaration before
        var r3 = await engine.Evaluate("eval('var runA; for (runA = true; runA; runA = false) { }')");
        _output.WriteLine($"Test3 (var; for): {r3?.ToString() ?? "null"} (type: {r3?.GetType().Name ?? "null"})");

        // Test 4: Full failing case
        var r4 = await engine.Evaluate("eval('var runA; 1; for (runA = true; runA; runA = false) { }')");
        _output.WriteLine($"Test4 (var; 1; for): {r4?.ToString() ?? "null"} (type: {r4?.GetType().Name ?? "null"})");

        // All should be undefined
        Assert.True(r1 is null || ReferenceEquals(r1, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Test1 expected undefined, got: {r1}");
        Assert.True(r2 is null || ReferenceEquals(r2, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Test2 expected undefined, got: {r2}");
        Assert.True(r3 is null || ReferenceEquals(r3, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Test3 expected undefined, got: {r3}");
        Assert.True(r4 is null || ReferenceEquals(r4, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Test4 expected undefined, got: {r4}");
    }

    [Fact(Timeout = 10000)]
    public async Task ForLoop_WithBody_ReturnsBodyValue()
    {
        var logger = new TestLogger(_output, "Test2", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('var runB; 2; for (runB = true; runB; runB = false) { 3; }')");
        _output.WriteLine($"Result: {result} (type: {result?.GetType().Name})");

        Assert.Equal(3.0, result);
    }

    [Fact(Timeout = 10000)]
    public async Task Switch_JustBreak_ReturnsUndefined()
    {
        var logger = new TestLogger(_output, "Test3", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-a-abrupt-empty.js
        var result = await engine.Evaluate("eval('1; switch (\"a\") { case \"a\": break; default: }')");
        _output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task Switch_ValueThenBreak_ReturnsValue()
    {
        var logger = new TestLogger(_output, "Test4", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-a-abrupt-empty.js
        var result = await engine.Evaluate("eval('2; switch (\"a\") { case \"a\": { 3; break; } default: }')");
        _output.WriteLine($"Result: {result} (type: {result?.GetType().Name})");

        Assert.Equal(3.0, result);
    }

    [Fact(Timeout = 10000)]
    public async Task Switch_FallThru_NonEmptyValuePreserved()
    {
        var logger = new TestLogger(_output, "Test5", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-a-fall-thru-nrml.js
        var result = await engine.Evaluate("eval('6; switch (\"a\") { case \"a\": 7; default: }')");
        _output.WriteLine($"Result: {result} (type: {result?.GetType().Name})");

        // Per spec, empty default should NOT overwrite the 7 from case "a"
        Assert.Equal(7.0, result);
    }

    [Fact(Timeout = 10000)]
    public async Task If_BreakInBody_ReturnsUndefined()
    {
        var logger = new TestLogger(_output, "Test6", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-else-true-abrupt-empty.js
        var result = await engine.Evaluate("eval('1; do { if (true) { break; } else { } } while (false)')");
        _output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task ForOf_Break_ReturnsUndefined()
    {
        var logger = new TestLogger(_output, "Test7", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-expr-abrupt-empty.js
        var result = await engine.Evaluate("eval('var a; 1; for (a of [0]) { break; }')");
        _output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task Finally_Break_ReturnsUndefined()
    {
        var logger = new TestLogger(_output, "Test8", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-finally-empty-break.js
        var result = await engine.Evaluate(
            "eval('for (var i = 0; i < 2; ++i) { if (i) { try {} finally { break; } } \"bad completion\"; }')");
        _output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task ForOfDecl_Break_ReturnsUndefined()
    {
        var logger = new TestLogger(_output, "Test9", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-decl-abrupt-empty.js - for (let/const)
        var result = await engine.Evaluate("eval('1; for (let a of [0]) { break; }')");
        _output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task DoWhile_Break_ReturnsUndefined()
    {
        var logger = new TestLogger(_output, "Test10", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-abrupt-empty.js (do-while)
        var result = await engine.Evaluate("eval('1; do { break; } while (true)')");
        _output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task While_Break_ReturnsUndefined()
    {
        var logger = new TestLogger(_output, "Test11", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-abrupt-empty.js (while)
        var result = await engine.Evaluate("eval('1; while (true) { break; }')");
        _output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task LabeledBreak_ReturnsUndefined()
    {
        var logger = new TestLogger(_output, "Test12", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-break.js (labeled)
        var result = await engine.Evaluate("eval('1; L: break L;')");
        _output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task Finally_Continue_ReturnsUndefined()
    {
        var logger = new TestLogger(_output, "Test13", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-finally-empty-continue.js
        var result = await engine.Evaluate(
            "eval('var run = true; 1; while (run) { run = false; try {} finally { continue; } }')");
        _output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }
}
