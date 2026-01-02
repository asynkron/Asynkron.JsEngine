using Asynkron.JsEngine.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// TEST BOMB: Systematic elimination of suspected causes for catch block completion value bugs.
///
/// Based on Test262 failures, many tests related to completion values are failing.
/// This test bomb targets specific hypotheses about what might be broken.
///
/// Methodology:
/// - Each test targets ONE specific hypothesis
/// - The pattern of pass/fail reveals the root cause
/// - Tests are named H1_, H2_, etc. for easy tracking
///
/// Known issues from replication:
/// - eval('1; try { throw null; } catch (err) { }') returns 1 instead of undefined
/// - break-from-finally causes infinite loop
/// </summary>
public sealed class CatchBlockTestBomb(ITestOutputHelper output)
{
    // ============================================================================
    // SECTION 1: TRY/CATCH COMPLETION VALUES
    // ============================================================================

    /// <summary>
    /// H1: Does an empty try block return undefined?
    /// Expected: eval('try { } catch (e) { }') === undefined
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H1_EmptyTryBlockReturnsUndefined()
    {
        var logger = new TestLogger(output, "H1", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('try { } catch (e) { }')");
        output.WriteLine($"H1 Result: {result} (expected: undefined)");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Empty try block should return undefined, got: {result}");
    }

    /// <summary>
    /// H2: Does a try block with a value return that value (no exception)?
    /// Expected: eval('try { 42; } catch (e) { }') === 42
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H2_TryBlockWithValueReturnsValue()
    {
        var logger = new TestLogger(output, "H2", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('try { 42; } catch (e) { }')");
        output.WriteLine($"H2 Result: {result} (expected: 42)");

        Assert.Equal(42.0, result);
    }

    /// <summary>
    /// H3: Does an empty catch block return undefined when executed?
    /// Expected: eval('try { throw null; } catch (e) { }') === undefined
    /// THIS IS THE MAIN BUG - currently returns the previous completion value
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H3_EmptyCatchBlockReturnsUndefined()
    {
        var logger = new TestLogger(output, "H3", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('try { throw null; } catch (e) { }')");
        output.WriteLine($"H3 Result: {result} (expected: undefined)");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Empty catch block should return undefined, got: {result}");
    }

    /// <summary>
    /// H4: Does a catch block with a value return that value?
    /// Expected: eval('try { throw null; } catch (e) { 99; }') === 99
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H4_CatchBlockWithValueReturnsValue()
    {
        var logger = new TestLogger(output, "H4", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('try { throw null; } catch (e) { 99; }')");
        output.WriteLine($"H4 Result: {result} (expected: 99)");

        Assert.Equal(99.0, result);
    }

    /// <summary>
    /// H5: Does the previous statement's value affect try/catch completion?
    /// Expected: eval('1; try { throw null; } catch (e) { }') === undefined
    /// The '1;' before should NOT affect the completion value
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H5_PreviousStatementDoesNotAffectEmptyCatch()
    {
        var logger = new TestLogger(output, "H5", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('1; try { throw null; } catch (e) { }')");
        output.WriteLine($"H5 Result: {result} (expected: undefined)");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Previous statement should not affect empty catch, got: {result}");
    }

    /// <summary>
    /// H6: Does the previous statement's value affect try block completion (no exception)?
    /// Expected: eval('1; try { } catch (e) { }') === undefined
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H6_PreviousStatementDoesNotAffectEmptyTry()
    {
        var logger = new TestLogger(output, "H6", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('1; try { } catch (e) { }')");
        output.WriteLine($"H6 Result: {result} (expected: undefined)");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Previous statement should not affect empty try, got: {result}");
    }

    // ============================================================================
    // SECTION 2: FINALLY COMPLETION VALUES
    // ============================================================================

    /// <summary>
    /// H7: Does finally preserve try's completion value (no exception)?
    /// Expected: eval('try { 7; } finally { }') === 7
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H7_FinallyPreservesTryCompletionValue()
    {
        var logger = new TestLogger(output, "H7", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('try { 7; } finally { }')");
        output.WriteLine($"H7 Result: {result} (expected: 7)");

        Assert.Equal(7.0, result);
    }

    /// <summary>
    /// H8: Does finally preserve catch's completion value (exception thrown)?
    /// Expected: eval('try { throw 1; } catch (e) { 8; } finally { }') === 8
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H8_FinallyPreservesCatchCompletionValue()
    {
        var logger = new TestLogger(output, "H8", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('try { throw 1; } catch (e) { 8; } finally { }')");
        output.WriteLine($"H8 Result: {result} (expected: 8)");

        Assert.Equal(8.0, result);
    }

    /// <summary>
    /// H9: Does finally with value override try's completion?
    /// Expected: eval('try { 1; } finally { 9; }') === 9
    /// Per spec, finally's normal completion value replaces the previous.
    /// ACTUALLY: Per ES spec, finally's completion is only used if it's abrupt!
    /// Normal completion from finally should preserve the try/catch value.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H9_FinallyNormalCompletionPreservesTryValue()
    {
        var logger = new TestLogger(output, "H9", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // Per ECMAScript spec 13.15.8:
        // "If F.[[Type]] is normal, set F to C" - finally's normal completion
        // preserves the previous completion value from try/catch
        var result = await engine.Evaluate("eval('try { 1; } finally { 9; }')");
        output.WriteLine($"H9 Result: {result} (expected: 1, because finally is normal completion)");

        Assert.Equal(1.0, result);
    }

    // ============================================================================
    // SECTION 3: IF/ELSE COMPLETION VALUES
    // ============================================================================

    /// <summary>
    /// H10: Does if(true) with value return that value?
    /// Expected: eval('if (true) { 10; }') === 10
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H10_IfTrueReturnsValue()
    {
        var logger = new TestLogger(output, "H10", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('if (true) { 10; }')");
        output.WriteLine($"H10 Result: {result} (expected: 10)");

        Assert.Equal(10.0, result);
    }

    /// <summary>
    /// H11: Does if(false) with else return else's value?
    /// Expected: eval('if (false) { 1; } else { 11; }') === 11
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H11_IfFalseElseReturnsValue()
    {
        var logger = new TestLogger(output, "H11", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('if (false) { 1; } else { 11; }')");
        output.WriteLine($"H11 Result: {result} (expected: 11)");

        Assert.Equal(11.0, result);
    }

    /// <summary>
    /// H12: Does empty else return undefined?
    /// Expected: eval('if (false) { 1; } else { }') === undefined
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H12_EmptyElseReturnsUndefined()
    {
        var logger = new TestLogger(output, "H12", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('if (false) { 1; } else { }')");
        output.WriteLine($"H12 Result: {result} (expected: undefined)");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Empty else should return undefined, got: {result}");
    }

    /// <summary>
    /// H13: Does previous statement affect empty else?
    /// Expected: eval('5; if (false) { 1; } else { }') === undefined
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H13_PreviousStatementDoesNotAffectEmptyElse()
    {
        var logger = new TestLogger(output, "H13", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('5; if (false) { 1; } else { }')");
        output.WriteLine($"H13 Result: {result} (expected: undefined)");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Previous statement should not affect empty else, got: {result}");
    }

    // ============================================================================
    // SECTION 4: FOR LOOP COMPLETION VALUES
    // ============================================================================

    /// <summary>
    /// H14: Does for loop return last iteration's value?
    /// Expected: eval('for (var i = 0; i < 3; i++) { i; }') === 2
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H14_ForLoopReturnsLastIterationValue()
    {
        var logger = new TestLogger(output, "H14", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('for (var i = 0; i < 3; i++) { i; }')");
        output.WriteLine($"H14 Result: {result} (expected: 2)");

        Assert.Equal(2.0, result);
    }

    /// <summary>
    /// H15: Does empty for loop body preserve previous value?
    /// Per spec, empty loop body should return undefined for that iteration.
    /// Expected: eval('1; for (var i = 0; i < 1; i++) { }') === undefined
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H15_EmptyForLoopBodyReturnsUndefined()
    {
        var logger = new TestLogger(output, "H15", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('1; for (var i = 0; i < 1; i++) { }')");
        output.WriteLine($"H15 Result: {result} (expected: undefined)");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Empty for loop body should return undefined, got: {result}");
    }

    // ============================================================================
    // SECTION 5: SWITCH COMPLETION VALUES
    // ============================================================================

    /// <summary>
    /// H16: Does switch case return its value?
    /// Expected: eval('switch (1) { case 1: 16; }') === 16
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H16_SwitchCaseReturnsValue()
    {
        var logger = new TestLogger(output, "H16", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('switch (1) { case 1: 16; }')");
        output.WriteLine($"H16 Result: {result} (expected: 16)");

        Assert.Equal(16.0, result);
    }

    /// <summary>
    /// H17: Does empty switch case with break return undefined?
    /// Expected: eval('1; switch (1) { case 1: break; }') === undefined
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H17_EmptySwitchCaseWithBreakReturnsUndefined()
    {
        var logger = new TestLogger(output, "H17", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('1; switch (1) { case 1: break; }')");
        output.WriteLine($"H17 Result: {result} (expected: undefined)");

        Assert.True(result is null || ReferenceEquals(result, Asynkron.JsEngine.Ast.Symbol.Undefined),
            $"Empty switch case with break should return undefined, got: {result}");
    }

    // ============================================================================
    // SECTION 6: CATCH BLOCK SCOPE AND PARAMETER
    // ============================================================================

    /// <summary>
    /// H18: Is catch parameter accessible in catch block?
    /// Expected: The catch parameter should be bound correctly.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H18_CatchParameterIsAccessible()
    {
        var logger = new TestLogger(output, "H18", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            var caught = 'not-caught';
            try {
                throw 'hello';
            } catch (e) {
                caught = e;
            }
            caught;
        """);
        output.WriteLine($"H18 Result: {result} (expected: 'hello')");

        Assert.Equal("hello", result);
    }

    /// <summary>
    /// H19: Is catch parameter correctly scoped (doesn't leak)?
    /// Expected: Outer 'e' should not be affected by catch(e).
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H19_CatchParameterDoesNotLeak()
    {
        var logger = new TestLogger(output, "H19", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            var e = 'outer';
            try {
                throw 'inner';
            } catch (e) {
                // e is 'inner' here
            }
            e; // Should still be 'outer'
        """);
        output.WriteLine($"H19 Result: {result} (expected: 'outer')");

        Assert.Equal("outer", result);
    }

    /// <summary>
    /// H20: Does catch with destructuring work?
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H20_CatchWithDestructuringWorks()
    {
        var logger = new TestLogger(output, "H20", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            var a, b;
            try {
                throw { x: 1, y: 2 };
            } catch ({ x, y }) {
                a = x;
                b = y;
            }
            a + b;
        """);
        output.WriteLine($"H20 Result: {result} (expected: 3)");

        Assert.Equal(3.0, result);
    }

    // ============================================================================
    // SECTION 7: CONTROL FLOW FROM CATCH/FINALLY (SIMPLIFIED)
    // ============================================================================

    /// <summary>
    /// H21: Does continue work inside catch block?
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H21_ContinueFromCatch()
    {
        var logger = new TestLogger(output, "H21", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            var count = 0;
            for (var i = 0; i < 3; i++) {
                try {
                    throw 'e';
                } catch (e) {
                    count++;
                    continue;
                }
                count = -1000;
            }
            count;
        """);
        output.WriteLine($"H21 Result: {result} (expected: 3)");

        Assert.Equal(3.0, result);
    }

    /// <summary>
    /// H22: Does return work inside catch block?
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H22_ReturnFromCatch()
    {
        var logger = new TestLogger(output, "H22", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            function test() {
                try {
                    throw 'e';
                } catch (e) {
                    return 22;
                }
                return -1;
            }
            test();
        """);
        output.WriteLine($"H22 Result: {result} (expected: 22)");

        Assert.Equal(22.0, result);
    }

    /// <summary>
    /// H23: Does break work inside catch block (in a loop)?
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H23_BreakFromCatch()
    {
        var logger = new TestLogger(output, "H23", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            var count = 0;
            for (var i = 0; i < 5; i++) {
                try {
                    throw 'e';
                } catch (e) {
                    count++;
                    break;
                }
                count = -1000;
            }
            count;
        """);
        output.WriteLine($"H23 Result: {result} (expected: 1)");

        Assert.Equal(1.0, result);
    }

    // ============================================================================
    // SECTION 8: NESTED TRY/CATCH
    // ============================================================================

    /// <summary>
    /// H24: Does nested try/catch work correctly?
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H24_NestedTryCatch()
    {
        var logger = new TestLogger(output, "H24", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            var result = '';
            try {
                try {
                    throw 'inner';
                } catch (e) {
                    result += e;
                    throw 'outer';
                }
            } catch (e) {
                result += '|' + e;
            }
            result;
        """);
        output.WriteLine($"H24 Result: {result} (expected: 'inner|outer')");

        Assert.Equal("inner|outer", result);
    }

    /// <summary>
    /// H25: Does try with only finally (no catch) work?
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H25_TryWithOnlyFinally()
    {
        var logger = new TestLogger(output, "H25", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            var ran = false;
            try {
                // no throw
            } finally {
                ran = true;
            }
            ran;
        """);
        output.WriteLine($"H25 Result: {result} (expected: true)");

        Assert.Equal(true, result);
    }

    // ============================================================================
    // SUMMARY HELPERS
    // ============================================================================

    /// <summary>
    /// H26: Simple baseline - does eval work at all?
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H26_EvalWorksAtAll()
    {
        var logger = new TestLogger(output, "H26", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('1 + 2')");
        output.WriteLine($"H26 Result: {result} (expected: 3)");

        Assert.Equal(3.0, result);
    }

    /// <summary>
    /// H27: Does eval return the last expression value?
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H27_EvalReturnsLastExpression()
    {
        var logger = new TestLogger(output, "H27", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("eval('1; 2; 3')");
        output.WriteLine($"H27 Result: {result} (expected: 3)");

        Assert.Equal(3.0, result);
    }
}
