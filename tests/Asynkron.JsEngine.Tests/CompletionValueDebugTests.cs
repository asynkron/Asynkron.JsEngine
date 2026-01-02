using Asynkron.JsEngine.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Debug tests for completion value semantics to understand the actual behavior.
/// </summary>
public sealed class CompletionValueDebugTests(ITestOutputHelper output)
{
    [Fact(Timeout = 10000)]
    public async Task ForLoop_EmptyBody_ReturnsUndefined()
    {
        var logger = new TestLogger(output, "Test1", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // This is cptn-expr-expr-iter.js test case
        var result = await engine.Evaluate("eval('var runA; 1; for (runA = true; runA; runA = false) { }')");
        output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task ForLoop_Simple_EmptyBody_ReturnsUndefined()
    {
        var logger = new TestLogger(output, "TestSimple", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // Simplest case - just the for loop
        var result = await engine.Evaluate("eval('for (var i = 0; i < 1; i++) { }')");
        output.WriteLine($"Simple for result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task ForLoop_ZeroIterations_ReturnsUndefined()
    {
        var logger = new TestLogger(output, "TestZero", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // Loop that never executes body
        var result = await engine.Evaluate("eval('for (var i = 0; i < 0; i++) { 42; }')");
        output.WriteLine($"Zero iterations result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task ForLoop_WithPrecedingStatement_EmptyBody()
    {
        var logger = new TestLogger(output, "TestPrecedingStmt", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // Test 1: Just the for loop
        var r1 = await engine.Evaluate("eval('for (var runA = true; runA; runA = false) { }')");
        output.WriteLine($"Test1 (just for): {r1?.ToString() ?? "null"} (type: {r1?.GetType().Name ?? "null"})");

        // Test 2: With preceding expression
        var r2 = await engine.Evaluate("eval('1; for (var runA = true; runA; runA = false) { }')");
        output.WriteLine($"Test2 (1; for): {r2?.ToString() ?? "null"} (type: {r2?.GetType().Name ?? "null"})");

        // Test 3: With var declaration before
        var r3 = await engine.Evaluate("eval('var runA; for (runA = true; runA; runA = false) { }')");
        output.WriteLine($"Test3 (var; for): {r3?.ToString() ?? "null"} (type: {r3?.GetType().Name ?? "null"})");

        // Test 4: Full failing case
        var r4 = await engine.Evaluate("eval('var runA; 1; for (runA = true; runA; runA = false) { }')");
        output.WriteLine($"Test4 (var; 1; for): {r4?.ToString() ?? "null"} (type: {r4?.GetType().Name ?? "null"})");

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
        var logger = new TestLogger(output, "Test2", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('var runB; 2; for (runB = true; runB; runB = false) { 3; }')");
        output.WriteLine($"Result: {result} (type: {result?.GetType().Name})");

        Assert.Equal(3.0, result);
    }

    [Fact(Timeout = 10000)]
    public async Task Switch_JustBreak_ReturnsUndefined()
    {
        var logger = new TestLogger(output, "Test3", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-a-abrupt-empty.js
        var result = await engine.Evaluate("eval('1; switch (\"a\") { case \"a\": break; default: }')");
        output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task Switch_ValueThenBreak_ReturnsValue()
    {
        var logger = new TestLogger(output, "Test4", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-a-abrupt-empty.js
        var result = await engine.Evaluate("eval('2; switch (\"a\") { case \"a\": { 3; break; } default: }')");
        output.WriteLine($"Result: {result} (type: {result?.GetType().Name})");

        Assert.Equal(3.0, result);
    }

    [Fact(Timeout = 10000)]
    public async Task Switch_FallThru_NonEmptyValuePreserved()
    {
        var logger = new TestLogger(output, "Test5", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-a-fall-thru-nrml.js
        var result = await engine.Evaluate("eval('6; switch (\"a\") { case \"a\": 7; default: }')");
        output.WriteLine($"Result: {result} (type: {result?.GetType().Name})");

        // Per spec, empty default should NOT overwrite the 7 from case "a"
        Assert.Equal(7.0, result);
    }

    [Fact(Timeout = 10000)]
    public async Task If_BreakInBody_ReturnsUndefined()
    {
        var logger = new TestLogger(output, "Test6", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-else-true-abrupt-empty.js
        var result = await engine.Evaluate("eval('1; do { if (true) { break; } else { } } while (false)')");
        output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task ForOf_Break_ReturnsUndefined()
    {
        var logger = new TestLogger(output, "Test7", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-expr-abrupt-empty.js
        var result = await engine.Evaluate("eval('var a; 1; for (a of [0]) { break; }')");
        output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task Finally_Break_ReturnsUndefined()
    {
        var logger = new TestLogger(output, "Test8", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-finally-empty-break.js
        var result = await engine.Evaluate(
            "eval('for (var i = 0; i < 2; ++i) { if (i) { try {} finally { break; } } \"bad completion\"; }')");
        output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000, Skip = "Blocked by separate bug: for (let ... of ...) inside eval() doesn't create binding")]
    public async Task ForOfDecl_Break_ReturnsUndefined()
    {
        var logger = new TestLogger(output, "Test9", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-decl-abrupt-empty.js - for (let/const)
        // Note: This test is blocked by a separate bug where for (let ... of ...) inside eval()
        // fails with "a is not defined" because the loop binding isn't created properly.
        var result = await engine.Evaluate("eval('1; for (let a of [0]) { break; }')");
        output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task DoWhile_Break_ReturnsUndefined()
    {
        var logger = new TestLogger(output, "Test10", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-abrupt-empty.js (do-while)
        var result = await engine.Evaluate("eval('1; do { break; } while (true)')");
        output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task While_Break_ReturnsUndefined()
    {
        var logger = new TestLogger(output, "Test11", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-abrupt-empty.js (while)
        var result = await engine.Evaluate("eval('1; while (true) { break; }')");
        output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task LabeledBreak_ReturnsUndefined()
    {
        var logger = new TestLogger(output, "Test12", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-break.js (labeled)
        var result = await engine.Evaluate("eval('1; L: break L;')");
        output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task Finally_Continue_ReturnsUndefined()
    {
        var logger = new TestLogger(output, "Test13", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // cptn-finally-empty-continue.js
        var result = await engine.Evaluate(
            "eval('var run = true; 1; while (run) { run = false; try {} finally { continue; } }')");
        output.WriteLine($"Result: {result?.ToString() ?? "null"} (type: {result?.GetType().Name ?? "null"})");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Expected undefined, got: {result}");
    }

    [Fact(Timeout = 10000)]
    public async Task Debug_SwitchFallThrough_ValueThenBreak()
    {
        var logger = new TestLogger(output, "SwitchFallThrough", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // Test: switch with case "a": 7; falling through to case "b": break;
        // Expected: 7 (break has empty completion, UpdateEmpty fills from previous value)
        output.WriteLine("=== Testing: eval('6; switch (\"a\") { case \"a\": 7; case \"b\": break; }') ===");
        var result = await engine.Evaluate("eval('6; switch (\"a\") { case \"a\": 7; case \"b\": break; }')");
        output.WriteLine($"Result: {result?.ToString() ?? "null"} (expected: 7)");
    }
}
