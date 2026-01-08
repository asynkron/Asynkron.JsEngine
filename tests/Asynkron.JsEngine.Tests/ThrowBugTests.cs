using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.Regression)]
[Category(TestCategories.RuntimeSemantics)]
public sealed class ThrowBugTests(ITestOutputHelper output)
{
    /// <summary>
    /// This replicates the Test262 assert.throws pattern that is failing.
    /// The key issue is that when assert.throws catches an error, the
    /// typeof the caught value is returning undefined instead of object.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task AssertThrowsPattern_ShouldCatchErrorObject()
    {
        var testLogger = new TestLogger(output, "ThrowBug", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        // This mimics how Test262's assert.throws works
        var result = await engine.Evaluate("""
            function assert(condition, message) {
                if (!condition) {
                    throw new Error(message || 'Assertion failed');
                }
            }

            assert.throws = function(expectedErrorConstructor, func, message) {
                var thrown = false;
                var thrownValue = undefined;
                try {
                    func();
                } catch (e) {
                    thrown = true;
                    thrownValue = e;
                }

                if (!thrown) {
                    throw new Error('Expected function to throw, but it did not');
                }

                // This is where the bug manifests - typeof thrownValue should be 'object'
                var typeofThrown = typeof thrownValue;
                var isNotNull = thrownValue !== null;
                var isObject = typeof thrownValue === 'object';
                var isRightType = thrownValue instanceof expectedErrorConstructor;

                return {
                    thrown: thrown,
                    typeofThrown: typeofThrown,
                    isNotNull: isNotNull,
                    isObject: isObject,
                    isRightType: isRightType,
                    thrownValue: thrownValue
                };
            };

            // Test case: eval('arguments = 10') in strict mode should throw SyntaxError
            var testResult = assert.throws(SyntaxError, function() {
                'use strict';
                eval('arguments = 10');
            });

            testResult.typeofThrown + ' | ' + testResult.isNotNull + ' | ' + testResult.isObject + ' | ' + testResult.isRightType;
        """);

        output.WriteLine($"\nResult: {result}");

        // Expected: "object | true | true | true"
        // Bug produces: "undefined | false | false | false" (or similar)
        Assert.Equal("object | true | true | true", result?.ToString());
    }

    /// <summary>
    /// Simpler test - just catch and return the type.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task SimpleCatch_TypeofThrownError()
    {
        var testLogger = new TestLogger(output, "ThrowBug", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        var result = await engine.Evaluate("""
            var thrownType = 'not-set';
            try {
                throw new Error('test');
            } catch (e) {
                thrownType = typeof e;
            }
            thrownType;
        """);

        output.WriteLine($"\nResult: {result}");
        Assert.Equal("object", result?.ToString());
    }

    /// <summary>
    /// Test with nested function call - more similar to Test262 pattern.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task NestedFunctionCatch_TypeofThrownError()
    {
        var testLogger = new TestLogger(output, "ThrowBug", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        var result = await engine.Evaluate("""
            function testCatch(fn) {
                var thrownType = 'not-set';
                try {
                    fn();
                } catch (e) {
                    thrownType = typeof e;
                }
                return thrownType;
            }

            testCatch(function() {
                throw new Error('test');
            });
        """);

        output.WriteLine($"\nResult: {result}");
        Assert.Equal("object", result?.ToString());
    }

    /// <summary>
    /// TEST262: catch-parameter-shadowing-var-variable.js
    /// A catch parameter should properly shadow a var in the outer scope.
    /// Inside the catch: 'a' = thrown value ('stuff3')
    /// Outside the catch: 'a' = original var value (1)
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task CatchParameterShadowsVarVariable()
    {
        var testLogger = new TestLogger(output, "CatchShadow", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        var result = await engine.Evaluate("""
            var results = [];
            function fn() {
                var a = 1;
                try {
                    throw 'stuff3';
                } catch (a) {
                    // Inside catch: 'a' should be the thrown value 'stuff3'
                    results.push('inside: ' + a);
                }
                // Outside catch: 'a' should still be 1
                results.push('outside: ' + a);
            }
            fn();
            results.join(' | ');
        """);

        output.WriteLine($"\nResult: {result}");
        // Expected: "inside: stuff3 | outside: 1"
        Assert.Equal("inside: stuff3 | outside: 1", result?.ToString());
    }

    /// <summary>
    /// TEST262: Verify the simple shadowing works without a function wrapper
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task SimpleCatchParameterShadowsVar()
    {
        var testLogger = new TestLogger(output, "CatchShadow", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        var result = await engine.Evaluate("""
            var a = 1;
            var insideValue = 'not-set';
            try {
                throw 'thrown-value';
            } catch (a) {
                insideValue = a;
            }
            insideValue + ' | ' + a;
        """);

        output.WriteLine($"\nResult: {result}");
        // Inside catch: 'a' = 'thrown-value'
        // Outside catch: 'a' = 1
        Assert.Equal("thrown-value | 1", result?.ToString());
    }

    /// <summary>
    /// TEST262 exact pattern: function call INSIDE catch block reads catch param
    /// This tests whether a function call from inside the catch block can read
    /// the catch parameter correctly (this is what Test262 does with assert.sameValue).
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task CatchParameterPassedToFunctionCall()
    {
        var testLogger = new TestLogger(output, "CatchFunc", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        var result = await engine.Evaluate("""
            function checkValue(actual, expected) {
                return actual + ' === ' + expected + ' -> ' + (actual === expected);
            }

            function fn() {
                var a = 1;
                var insideResult = 'not-called';
                try {
                    throw 'stuff3';
                } catch (a) {
                    // This mimics Test262's assert.sameValue(a, 'stuff3')
                    insideResult = checkValue(a, 'stuff3');
                }
                return insideResult;
            }
            fn();
        """);

        output.WriteLine($"\nResult: {result}");
        // Expected: "stuff3 === stuff3 -> true"
        Assert.Equal("stuff3 === stuff3 -> true", result?.ToString());
    }

    /// <summary>
    /// TEST262 SCRIPT LEVEL: catch at top-level script (not inside function)
    /// Test262 tests often run at script level, not inside a function.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task CatchParameterAtScriptLevel_StrictMode()
    {
        var testLogger = new TestLogger(output, "CatchScript", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        var result = await engine.Evaluate("""
            'use strict';

            function checkValue(actual, expected) {
                return actual + ' === ' + expected + ' -> ' + (actual === expected);
            }

            var a = 1;
            var insideResult = 'not-called';
            try {
                throw 'stuff3';
            } catch (a) {
                // At script level, a should still be 'stuff3' inside catch
                insideResult = checkValue(a, 'stuff3');
            }
            insideResult;
        """);

        output.WriteLine($"\nResult: {result}");
        // Expected: "stuff3 === stuff3 -> true"
        Assert.Equal("stuff3 === stuff3 -> true", result?.ToString());
    }

    /// <summary>
    /// TEST262 STRICT MODE: function call INSIDE catch block reads catch param
    /// This is the STRICT MODE version - Test262 runs in strict mode!
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task CatchParameterPassedToFunctionCall_StrictMode()
    {
        var testLogger = new TestLogger(output, "CatchFuncStrict", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        var result = await engine.Evaluate("""
            'use strict';

            function checkValue(actual, expected) {
                return actual + ' === ' + expected + ' -> ' + (actual === expected);
            }

            function fn() {
                var a = 1;
                var insideResult = 'not-called';
                try {
                    throw 'stuff3';
                } catch (a) {
                    // This mimics Test262's assert.sameValue(a, 'stuff3')
                    insideResult = checkValue(a, 'stuff3');
                }
                return insideResult;
            }
            fn();
        """);

        output.WriteLine($"\nResult: {result}");
        // Expected: "stuff3 === stuff3 -> true"
        Assert.Equal("stuff3 === stuff3 -> true", result?.ToString());
    }

    /// <summary>
    /// TEST262 pattern: arguments.callee throws in strict mode
    /// FIXED: arguments only exists inside functions, not at script level
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task ArgumentsCalleeThrowsInStrictMode()
    {
        var testLogger = new TestLogger(output, "ArgCallee", minLogLevel: LogLevel.Debug);
        var options = new JsEngineOptions { DebugMode = true, Logger = testLogger };
        await using var engine = new JsEngine(options);

        var result = await engine.Evaluate("""
            'use strict';
            var caught = false;
            var errorType = '';
            (function() {
                try {
                    arguments.callee;  // Should throw TypeError in strict mode
                } catch (e) {
                    caught = true;
                    errorType = e.constructor.name;
                }
            })();
            caught + ' | ' + errorType;
        """);

        output.WriteLine($"\nResult: {result}");
        Assert.Equal("true | TypeError", result?.ToString());
    }
}
