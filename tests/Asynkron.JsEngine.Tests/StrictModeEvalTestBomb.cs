using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// TEST BOMB: Systematic elimination of suspected causes for the strict mode eval bug.
///
/// The bug: eval('arguments = 10') in strict mode should throw SyntaxError but doesn't.
///
/// Each test targets ONE specific hypothesis. By running all tests, we can identify
/// which component is broken by seeing which tests pass and which fail.
///
/// Methodology:
/// - H1 PASS + H2 FAIL = H2 is the problem
/// - H1 FAIL + H2 PASS = H1 is the problem
/// - Both FAIL = Multiple issues or root cause affects both
/// - Both PASS = Our hypothesis list is incomplete
/// </summary>
[Category(TestCategories.StrictMode)]
[Category(TestCategories.Eval)]
[Category(TestCategories.Regression)]
public sealed class StrictModeEvalTestBomb(ITestOutputHelper output)
{
    /// <summary>
    /// H1: Does 'use strict' work AT ALL in our engine?
    /// Test: Assign to undeclared variable - should throw ReferenceError in strict mode.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H1_UseStrictWorksAtAll()
    {
        var logger = new TestLogger(output, "H1", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            'use strict';
            var caught = false;
            var errorType = '';
            try {
                undeclaredVariable = 10;
            } catch (e) {
                caught = true;
                errorType = e.constructor.name;
            }
            caught + ' | ' + errorType;
        """);

        output.WriteLine($"H1 Result: {result}");
        // Expected: "true | ReferenceError" - strict mode prevents implicit globals
        Assert.Equal("true | ReferenceError", result?.ToString());
    }

    /// <summary>
    /// H2: Does 'use strict' work inside a function?
    /// Test: Function-level strict mode should throw on undeclared variable.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H2_UseStrictWorksInFunction()
    {
        var logger = new TestLogger(output, "H2", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            function strictFunc() {
                'use strict';
                undeclaredVariable = 10;
            }
            var caught = false;
            var errorType = '';
            try {
                strictFunc();
            } catch (e) {
                caught = true;
                errorType = e.constructor.name;
            }
            caught + ' | ' + errorType;
        """);

        output.WriteLine($"H2 Result: {result}");
        Assert.Equal("true | ReferenceError", result?.ToString());
    }

    /// <summary>
    /// H3: Does 'use strict' inside a try block work?
    /// Test: The directive should still apply even inside try.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H3_UseStrictInsideTryBlock()
    {
        var logger = new TestLogger(output, "H3", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // Note: 'use strict' must be at the TOP of script/function, not inside try block
        // This tests if the script-level directive propagates INTO the try block
        var result = await engine.Evaluate("""
            'use strict';
            var caught = false;
            var errorType = '';
            try {
                undeclaredVariable = 10;
            } catch (e) {
                caught = true;
                errorType = e.constructor.name;
            }
            caught + ' | ' + errorType;
        """);

        output.WriteLine($"H3 Result: {result}");
        Assert.Equal("true | ReferenceError", result?.ToString());
    }

    /// <summary>
    /// H4: Does eval() work at all?
    /// Test: Basic eval should execute code and return result.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H4_EvalWorksAtAll()
    {
        var logger = new TestLogger(output, "H4", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            eval('1 + 2');
        """);

        output.WriteLine($"H4 Result: {result}");
        Assert.Equal(3.0, result);
    }

    /// <summary>
    /// H5: Does eval() inherit strict mode from caller? (Direct eval)
    /// Test: eval() called directly should run in strict mode if caller is strict.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H5_DirectEvalInheritsStrictMode()
    {
        var logger = new TestLogger(output, "H5", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            'use strict';
            var caught = false;
            var errorType = '';
            try {
                eval('undeclaredInEval = 10');
            } catch (e) {
                caught = true;
                errorType = e.constructor.name;
            }
            caught + ' | ' + errorType;
        """);

        output.WriteLine($"H5 Result: {result}");
        // Direct eval in strict mode should throw ReferenceError for undeclared assignment
        Assert.Equal("true | ReferenceError", result?.ToString());
    }

    /// <summary>
    /// H6: Does 'use strict' INSIDE eval code work?
    /// Test: eval("'use strict'; ...") should make that code strict.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H6_UseStrictInsideEvalWorks()
    {
        var logger = new TestLogger(output, "H6", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            var caught = false;
            var errorType = '';
            try {
                eval("'use strict'; undeclaredInEval = 10");
            } catch (e) {
                caught = true;
                errorType = e.constructor.name;
            }
            caught + ' | ' + errorType;
        """);

        output.WriteLine($"H6 Result: {result}");
        Assert.Equal("true | ReferenceError", result?.ToString());
    }

    /// <summary>
    /// H7: Is 'arguments' assignment detected as SyntaxError in strict mode?
    /// Test: Direct assignment to 'arguments' in strict function should be SyntaxError.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H7_ArgumentsAssignmentIsSyntaxError()
    {
        var logger = new TestLogger(output, "H7", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // This should be caught at PARSE time, not runtime
        var result = await engine.Evaluate("""
            var caught = false;
            var errorType = '';
            try {
                // This creates a function that tries to assign to arguments
                // The parser should reject this in strict mode
                eval("'use strict'; arguments = 10");
            } catch (e) {
                caught = true;
                errorType = e.constructor.name;
            }
            caught + ' | ' + errorType;
        """);

        output.WriteLine($"H7 Result: {result}");
        // Should be SyntaxError - this is an early error in strict mode
        Assert.Equal("true | SyntaxError", result?.ToString());
    }

    /// <summary>
    /// H8: Is 'eval' assignment detected as SyntaxError in strict mode?
    /// Test: Assignment to 'eval' identifier should be SyntaxError in strict mode.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H8_EvalAssignmentIsSyntaxError()
    {
        var logger = new TestLogger(output, "H8", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            var caught = false;
            var errorType = '';
            try {
                eval("'use strict'; eval = 10");
            } catch (e) {
                caught = true;
                errorType = e.constructor.name;
            }
            caught + ' | ' + errorType;
        """);

        output.WriteLine($"H8 Result: {result}");
        Assert.Equal("true | SyntaxError", result?.ToString());
    }

    /// <summary>
    /// H9: THE ACTUAL BUG CASE - Caller strict, eval code doesn't have directive
    /// Test: If outer code is strict, eval should inherit it WITHOUT needing its own directive.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H9_CallerStrictEvalInheritsIt()
    {
        var logger = new TestLogger(output, "H9", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            'use strict';
            var caught = false;
            var errorType = '';
            try {
                // NO 'use strict' inside eval - it should INHERIT from caller
                eval('arguments = 10');
            } catch (e) {
                caught = true;
                errorType = e.constructor.name;
            }
            caught + ' | ' + errorType;
        """);

        output.WriteLine($"H9 Result: {result}");
        // THIS IS THE BUG: Should be SyntaxError because caller is strict
        Assert.Equal("true | SyntaxError", result?.ToString());
    }

    /// <summary>
    /// H10: Is the issue specific to 'arguments' or all reserved words?
    /// Test: Try other strict mode reserved words.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H10_OtherReservedWordAssignment()
    {
        var logger = new TestLogger(output, "H10", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            'use strict';
            var caught = false;
            var errorType = '';
            try {
                // 'implements' is reserved in strict mode
                eval('var implements = 10');
            } catch (e) {
                caught = true;
                errorType = e.constructor.name;
            }
            caught + ' | ' + errorType;
        """);

        output.WriteLine($"H10 Result: {result}");
        Assert.Equal("true | SyntaxError", result?.ToString());
    }

    /// <summary>
    /// H11: Does indirect eval correctly NOT inherit strict mode?
    /// Test: (0, eval)('code') is indirect and should NOT be strict even if caller is.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H11_IndirectEvalDoesNotInheritStrict()
    {
        var logger = new TestLogger(output, "H11", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            'use strict';
            var caught = false;
            try {
                // Indirect eval - should NOT inherit strict mode
                // This creates a global variable (allowed in sloppy mode)
                (0, eval)('globalFromIndirectEval = 42');
                caught = false;
            } catch (e) {
                caught = true;
            }
            // In indirect eval (sloppy), this should have created a global
            var globalExists = typeof globalFromIndirectEval !== 'undefined';
            caught + ' | ' + globalExists;
        """);

        output.WriteLine($"H11 Result: {result}");
        // Indirect eval should NOT be strict, so no error and global is created
        Assert.Equal("false | true", result?.ToString());
    }

    /// <summary>
    /// H12: Is there a difference between script-level and function-level strict for eval?
    /// Test: Call eval from within a strict function.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H12_EvalFromStrictFunction()
    {
        var logger = new TestLogger(output, "H12", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            function strictCaller() {
                'use strict';
                return eval('arguments = 10');
            }
            var caught = false;
            var errorType = '';
            try {
                strictCaller();
            } catch (e) {
                caught = true;
                errorType = e.constructor.name;
            }
            caught + ' | ' + errorType;
        """);

        output.WriteLine($"H12 Result: {result}");
        Assert.Equal("true | SyntaxError", result?.ToString());
    }

    /// <summary>
    /// H13: THE ACTUAL BUG REPRODUCTION - 'use strict' INSIDE try block
    /// This is NOT a valid directive position! It's just a string expression.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H13_UseStrictInsideTryIsNotDirective()
    {
        var logger = new TestLogger(output, "H13", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        // This is the EXACT pattern from the failing test - 'use strict' is INSIDE try
        // Per ES spec, this is NOT a directive, just a string literal
        var result = await engine.Evaluate("""
            var thrownType = 'not-set';
            try {
                'use strict';  // NOT A DIRECTIVE - just a string!
                eval('arguments = 10');
            } catch (e) {
                thrownType = typeof e + ' | ' + (e !== null) + ' | ' + (e instanceof SyntaxError);
            }
            thrownType;
        """);

        output.WriteLine($"H13 Result: {result}");
        // The script is NOT strict, so eval is sloppy, so no error is thrown
        // This should return "not-set" because catch is never entered
        Assert.Equal("not-set", result?.ToString());
    }

    /// <summary>
    /// H14: The CORRECT way - 'use strict' at TOP of script
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H14_UseStrictAtTopWithTry()
    {
        var logger = new TestLogger(output, "H14", minLogLevel: LogLevel.Debug);
        await using var engine = new JsEngine(new JsEngineOptions { DebugMode = true, Logger = logger });

        var result = await engine.Evaluate("""
            'use strict';  // AT THE TOP - this IS a directive
            var thrownType = 'not-set';
            try {
                eval('arguments = 10');
            } catch (e) {
                thrownType = typeof e + ' | ' + (e !== null) + ' | ' + (e instanceof SyntaxError);
            }
            thrownType;
        """);

        output.WriteLine($"H14 Result: {result}");
        // NOW it's strict, so eval throws SyntaxError
        Assert.Equal("object | true | true", result?.ToString());
    }
}
