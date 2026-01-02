using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// LAYERED TESTS: Isolate whether the completion value bug is in IR emission or execution.
///
/// Layer 1: Simple blocks - Do empty blocks return undefined?
/// Layer 2: Compound statements - Do try/catch, if/else handle completion correctly?
/// Layer 3: Previous statement - Does a previous statement leak through?
/// Layer 4: Control flow - Do break/continue reset completion?
///
/// The debug logging will show IR instructions being executed.
/// </summary>
public sealed class CompletionValueLayeredTest(ITestOutputHelper output)
{
    // ============================================================================
    // LAYER 1: Simple Blocks - Most basic completion value tests
    // ============================================================================

    /// <summary>
    /// L1_A: Empty block in isolation.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task L1_A_EmptyBlockInIsolation()
    {
        var logger = new TestLogger(output, "L1_A", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('{ }')");
        output.WriteLine($"\nResult: {result}");
        output.WriteLine($"Is undefined: {result is null || ReferenceEquals(result, Symbol.Undefined)}");

        Assert.True(result is null || ReferenceEquals(result, Symbol.Undefined),
            $"Empty block should return undefined, got: {result}");
    }

    /// <summary>
    /// L1_B: Block with value.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task L1_B_BlockWithValue()
    {
        var logger = new TestLogger(output, "L1_B", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('{ 42; }')");
        output.WriteLine($"\nResult: {result}");

        Assert.Equal(42.0, result);
    }

    /// <summary>
    /// L1_C: Stmt THEN empty block - does empty block preserve previous value?
    /// Per ES spec 13.2.13, Block: {} returns NormalCompletion(empty).
    /// Per 13.2.6, UpdateEmpty(empty, previousValue) returns previousValue.
    /// So plain empty blocks PRESERVE the previous completion value.
    /// This is DIFFERENT from compound statements (try/catch, if/else, for, switch)
    /// which have explicit UpdateEmpty(result, undefined) at the end.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task L1_C_StmtThenEmptyBlock()
    {
        var logger = new TestLogger(output, "L1_C", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('1; { }')");
        output.WriteLine($"\nResult: {result} (expected: 1 - plain blocks preserve previous value)");

        // Per ECMAScript: Plain empty block completion is "empty" which preserves previous value
        // Compound statements (try/catch, if/else, for, switch) convert empty to undefined
        Assert.Equal(1.0, result);
    }

    /// <summary>
    /// L1_D: Stmt then block with value.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task L1_D_StmtThenBlockWithValue()
    {
        var logger = new TestLogger(output, "L1_D", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('1; { 2; }')");
        output.WriteLine($"\nResult: {result} (expected: 2)");

        Assert.Equal(2.0, result);
    }

    // ============================================================================
    // LAYER 2: Empty statements and semicolons
    // ============================================================================

    /// <summary>
    /// L2_A: Empty statement (just semicolon).
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task L2_A_EmptyStatement()
    {
        var logger = new TestLogger(output, "L2_A", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval(';')");
        output.WriteLine($"\nResult: {result}");

        Assert.True(result is null || ReferenceEquals(result, Symbol.Undefined),
            $"Empty statement should return undefined, got: {result}");
    }

    /// <summary>
    /// L2_B: Stmt then empty statement - does it reset?
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task L2_B_StmtThenEmptyStatement()
    {
        var logger = new TestLogger(output, "L2_B", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('1; ;')");
        output.WriteLine($"\nResult: {result}");

        // Per ECMAScript: Empty statement completion is "empty" which means
        // the previous completion value is preserved (this is actually correct!)
        // But blocks are different - they have their own completion semantics
        output.WriteLine("Note: Empty statement (;) preserves previous value per spec");
    }

    // ============================================================================
    // LAYER 3: If/Else - compound statement completion
    // ============================================================================

    /// <summary>
    /// L3_A: If with empty else (no previous stmt).
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task L3_A_IfWithEmptyElse()
    {
        var logger = new TestLogger(output, "L3_A", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('if (false) { 1; } else { }')");
        output.WriteLine($"\nResult: {result} (expected: undefined)");

        Assert.True(result is null || ReferenceEquals(result, Symbol.Undefined),
            $"Empty else should return undefined, got: {result}");
    }

    /// <summary>
    /// L3_B: Stmt THEN if with empty else.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task L3_B_StmtThenIfWithEmptyElse()
    {
        var logger = new TestLogger(output, "L3_B", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('5; if (false) { 1; } else { }')");
        output.WriteLine($"\nResult: {result} (expected: undefined)");

        // The if/else statement has its own completion value
        // The '5;' before should be overwritten by the if statement's completion
        Assert.True(result is null || ReferenceEquals(result, Symbol.Undefined),
            $"Previous stmt should not affect empty else, got: {result}");
    }

    // ============================================================================
    // LAYER 4: Try/Catch - the main suspect
    // ============================================================================

    /// <summary>
    /// L4_A: Empty catch (no previous stmt) - PASSES per test bomb.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task L4_A_EmptyCatchNoPrevStmt()
    {
        var logger = new TestLogger(output, "L4_A", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('try { throw 1; } catch (e) { }')");
        output.WriteLine($"\nResult: {result} (expected: undefined)");

        Assert.True(result is null || ReferenceEquals(result, Symbol.Undefined),
            $"Empty catch should return undefined, got: {result}");
    }

    /// <summary>
    /// L4_B: Stmt THEN empty catch - FAILS per test bomb.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task L4_B_StmtThenEmptyCatch()
    {
        var logger = new TestLogger(output, "L4_B", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('1; try { throw 2; } catch (e) { }')");
        output.WriteLine($"\nResult: {result} (expected: undefined)");

        // This is the failing case!
        Assert.True(result is null || ReferenceEquals(result, Symbol.Undefined),
            $"Previous stmt should not affect empty catch, got: {result}");
    }

    /// <summary>
    /// L4_C: Empty try (no exception, no previous stmt).
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task L4_C_EmptyTryNoPrevStmt()
    {
        var logger = new TestLogger(output, "L4_A", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('try { } catch (e) { }')");
        output.WriteLine($"\nResult: {result} (expected: undefined)");

        Assert.True(result is null || ReferenceEquals(result, Symbol.Undefined),
            $"Empty try should return undefined, got: {result}");
    }

    /// <summary>
    /// L4_D: Stmt THEN empty try - FAILS per test bomb.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task L4_D_StmtThenEmptyTry()
    {
        var logger = new TestLogger(output, "L4_D", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('1; try { } catch (e) { }')");
        output.WriteLine($"\nResult: {result} (expected: undefined)");

        Assert.True(result is null || ReferenceEquals(result, Symbol.Undefined),
            $"Previous stmt should not affect empty try, got: {result}");
    }

    // ============================================================================
    // LAYER 5: Direct comparison - what's different?
    // ============================================================================

    /// <summary>
    /// L5_A: Compare the IR trace for passing vs failing case.
    /// Run both and compare the logged instructions.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task L5_A_ComparePassingVsFailing()
    {
        output.WriteLine("=== PASSING CASE (no previous stmt) ===");
        {
            var logger = new TestLogger(output, "PASS", minLogLevel: LogLevel.Debug);
            await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });
            var result = await engine.Evaluate("eval('try { throw 1; } catch (e) { }')");
            output.WriteLine($"Result: {result}");
        }

        output.WriteLine("\n=== FAILING CASE (with previous stmt) ===");
        {
            var logger = new TestLogger(output, "FAIL", minLogLevel: LogLevel.Debug);
            await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });
            var result = await engine.Evaluate("eval('1; try { throw 2; } catch (e) { }')");
            output.WriteLine($"Result: {result}");
        }

        output.WriteLine("\nCompare the IR traces above to see the difference!");
    }
}
