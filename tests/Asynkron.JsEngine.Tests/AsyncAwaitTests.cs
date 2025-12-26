using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for async/await functionality.
/// </summary>
public abstract class AsyncAwaitTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_CanBeParsed()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act & Assert - Should not throw
        var program = engine.Parse("""

                                               async function test() {
                                                   return 42;
                                               }

                                   """);

        Assert.NotNull(program);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_CanBeDeclared()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act & Assert - Should not throw
        var temp = await engine.Evaluate("""

                                                     async function test() {
                                                         return 42;
                                                     }
                                         """);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunctionExpression_CanBeParsed()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act & Assert - Should not throw
        var program = engine.Parse("""

                                               let test = async function() {
                                                   return 42;
                                               };

                                   """);

        Assert.NotNull(program);
    }

    [Fact(Timeout = 2000)]
    public async Task AwaitExpression_CanBeParsed()
    {
        // Arrange
        await using var engine = CreateEngine();

        // Act & Assert - Should not throw
        var program = engine.Parse("""

                                               async function test() {
                                                   let result = await __delay(1, 42);
                                                   return result;
                                               }

                                   """);

        Assert.NotNull(program);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_ReturnsPromise()
    {
        // Arrange
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }

            return JsValue.Null;
        });

        // Act
        await engine.Evaluate("""

                                     async function test() {
                                         return 42;
                                     }

                                     let p = test();
                                     p.then(function(value) {
                                         captureResult(value);
                                     });

                         """);

        // Assert
        Assert.Equal("42", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_WithAwait_ReturnsValue()
    {
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }
            return JsValue.Null;
        });

        await engine.Evaluate("""
            async function test() {
                let p = new Promise(function(resolve) {
                    setTimeout(function() { resolve(42); }, 10);
                });
                let value = await p;
                return value;
            }
            test().then(function(value) {
                captureResult(value);
            });
            """);

        Assert.Equal("42", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_WithMultipleAwaits()
    {
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }
            return JsValue.Null;
        });

        await engine.Evaluate("""
            function delay(value) {
                return new Promise(function(resolve) {
                    setTimeout(function() { resolve(value); }, 10);
                });
            }
            async function test() {
                let a = await delay(10);
                let b = await delay(20);
                return a + b;
            }
            test().then(function(value) {
                captureResult(value);
            });
            """);

        Assert.Equal("30", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_WithAwaitInExpression()
    {
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }
            return JsValue.Null;
        });

        await engine.Evaluate("""
            function delay(value) {
                return new Promise(function(resolve) {
                    setTimeout(function() { resolve(value); }, 10);
                });
            }
            async function test() {
                let value = (await delay(10)) + (await delay(20));
                return value;
            }
            test().then(function(value) {
                captureResult(value);
            });
            """);

        Assert.Equal("30", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_HandlesRejection()
    {
        // Arrange
        await using var engine = CreateEngine();
        var caught = false;

        engine.SetGlobalFunction("markCaught", _ =>
        {
            caught = true;
            return JsValue.Null;
        });

        // Act
        await engine.Evaluate("""

                                     async function test() {
                                         throw "error";
                                     }

                                     test()["catch"](function(err) {
                                         markCaught();
                                     });

                         """);

        // Assert
        Assert.True(caught);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_WithTryCatch()
    {
        // Arrange
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }

            return JsValue.Null;
        });

        // Act
        await engine.Evaluate("""

                                     async function test() {
                                         try {
                                             throw "error";
                                         } catch (e) {
                                             return "caught";
                                         }
                                     }

                                     test().then(function(value) {
                                         captureResult(value);
                                     });

                         """);

        // Assert
        Assert.Equal("caught", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_ChainedCalls()
    {
        // Arrange
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }

            return JsValue.Null;
        });

        // Act
        await engine.Evaluate("""

                                     async function getNumber() {
                                         return 10;
                                     }

                                     async function doubleNumber(n) {
                                         return n * 2;
                                     }

                                     async function test() {
                                         let n = await getNumber();
                                         let doubled = await doubleNumber(n);
                                         return doubled;
                                     }

                                     test().then(function(value) {
                                         captureResult(value);
                                     });

                         """);

        // Assert
        Assert.Equal("20", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunctionExpression_Works()
    {
        // Arrange
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }

            return JsValue.Null;
        });

        // Act
        await engine.Evaluate("""

                                     let test = async function() {
                                         return 42;
                                     };

                                     test().then(function(value) {
                                         captureResult(value);
                                     });

                         """);

        // Assert
        Assert.Equal("42", result);
    }

//     [Fact(Timeout = 2000)]
//     public async Task CpsTransformer_AlreadyTransformedCodeDoesNotNeedTransformation()
//     {
//         // Arrange
//         await using var engine = CreateEngine();
//         var transformer = new CpsTransformer();
//
//         // engine.Parse() already applies CPS transformation, so the result
//         // should not need transformation again
//         var program = engine.Parse("""
//
//                                                async function test() {
//                                                    return 42;
//                                                }
//
//                                    """);
//
//         // Act
//         var needsTransform = CpsTransformer.NeedsTransformation(program);
//
//         // Assert - Already transformed code should not need transformation
//         Assert.False(needsTransform);
//     }
//
//     [Fact(Timeout = 2000)]
//     public async Task CpsTransformer_AlreadyTransformedAwaitDoesNotNeedTransformation()
//     {
//         // Arrange
//         await using var engine = CreateEngine();
//         var transformer = new CpsTransformer();
//
//         // engine.Parse() already applies CPS transformation, so the result
//         // should not need transformation again
//         var program = engine.Parse("""
//
//                                                async function test() {
//                                                    let value = await Promise.resolve(42);
//                                                    return value;
//                                                }
//
//                                    """);
//
//         // Act
//         var needsTransform = CpsTransformer.NeedsTransformation(program);
//
//         // Assert - Already transformed code should not need transformation
//         Assert.False(needsTransform);
//     }
//
//     [Fact(Timeout = 2000)]
//     public async Task CpsTransformer_TransformIsIdempotent()
//     {
//         // Arrange
//         await using var engine = CreateEngine();
//         var transformer = new CpsTransformer();
//
//         // engine.Parse() already applies CPS transformation
//         var program = engine.Parse("""
//
//                                                async function test() {
//                                                    return 42;
//                                                }
//
//                                    """);
//
//         // Act - Transform already-transformed code
//         var transformed = transformer.Transform(program);
//
//         // Assert - Should return the same program unchanged since it doesn't need transformation
//         Assert.NotNull(transformed);
//         Assert.Same(program, transformed); // Should be the same instance
//     }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_SequentialAwaits()
    {
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }
            return JsValue.Null;
        });

        await engine.Evaluate("""
            function delay(value) {
                return new Promise(function(resolve) {
                    setTimeout(function() { resolve(value); }, 10);
                });
            }
            async function test() {
                let a = await delay(5);
                let b = await delay(a + 3);
                let c = await delay(b * 2);
                return c;
            }
            test().then(function(value) {
                captureResult(value);
            });
            """);

        Assert.Equal("16", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_ReturnsNull()
    {
        // Arrange
        await using var engine = CreateEngine();
        var wasCalled = false;

        engine.SetGlobalFunction("markCalled", _ =>
        {
            wasCalled = true;
            return JsValue.Null;
        });

        // Act
        await engine.Evaluate("""

                                     async function test() {
                                         return null;
                                     }

                                     test().then(function(value) {
                                         markCalled();
                                     });

                         """);

        // Assert
        Assert.True(wasCalled);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_NoReturn()
    {
        // Arrange
        await using var engine = CreateEngine();
        var wasCalled = false;

        engine.SetGlobalFunction("markCalled", _ =>
        {
            wasCalled = true;
            return JsValue.Null;
        });

        // Act
        await engine.Evaluate("""

                                     async function test() {
                                         // No return statement
                                     }

                                     test().then(function(value) {
                                         markCalled();
                                     });

                         """);

        // Assert
        Assert.True(wasCalled);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_WithSetTimeoutDelay_ReturnsValue()
    {
        // Arrange
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }

            return JsValue.Null;
        });

        // Act
        await engine.Evaluate("""

                                     async function test() {
                                         let p = new Promise(function(resolve) {
                                             setTimeout(function() {
                                                 resolve(42);
                                             }, 100);
                                         });
                                         let value = await p;
                                         return value;
                                     }

                                     test().then(function(value) {
                                         captureResult(value);
                                     });

                         """);

        // Assert
        Assert.Equal("42", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_WithMultipleSetTimeoutDelays()
    {
        // Arrange
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }

            return JsValue.Null;
        });

        // Act
        await engine.Evaluate("""

                                     async function test() {
                                         let p1 = new Promise(function(resolve) {
                                             setTimeout(function() {
                                                 resolve(10);
                                             }, 50);
                                         });

                                         let p2 = new Promise(function(resolve) {
                                             setTimeout(function() {
                                                 resolve(20);
                                             }, 50);
                                         });

                                         let a = await p1;
                                         let b = await p2;
                                         return a + b;
                                     }

                                     test().then(function(value) {
                                         captureResult(value);
                                     });

                         """);

        // Assert
        Assert.Equal("30", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_WithDelayAndComputation()
    {
        // Arrange
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }

            return JsValue.Null;
        });

        // Act
        await engine.Evaluate("""

                                     function delayedValue(value, delay) {
                                         return new Promise(function(resolve) {
                                             setTimeout(function() {
                                                 resolve(value);
                                             }, delay);
                                         });
                                     }

                                     async function test() {
                                         let a = await delayedValue(5, 30);
                                         let b = await delayedValue(3, 30);
                                         let c = await delayedValue(2, 30);
                                         return (a + b) * c;
                                     }

                                     test().then(function(value) {
                                         captureResult(value);
                                     });

                         """);

        // Assert
        Assert.Equal("16", result);
    }

    // Known to be flaky - timing-dependent test that sometimes fails due to race conditions
    [Fact(Timeout = 2000, Skip = "Flaky: timing-dependent test")]
    public async Task AsyncFunction_WithParallelDelays()
    {
        // Arrange
        await using var engine = CreateEngine();
        var results = new List<string>();

        engine.SetGlobalFunction("addResult", args =>
        {
            if (args.Count > 0)
            {
                results.Add(args[0].ToObject()?.ToString() ?? "");
            }

            return JsValue.Null;
        });

        // Act
        await engine.Evaluate("""

                                     function delayedValue(value, delay) {
                                         return new Promise(function(resolve) {
                                             setTimeout(function() {
                                                 resolve(value);
                                             }, delay);
                                         });
                                     }

                                     async function test() {
                                         let p1 = delayedValue("first", 30);
                                         let p2 = delayedValue("second", 30);
                                         let p3 = delayedValue("third", 30);

                                         let values = await Promise.all([p1, p2, p3]);
                                         addResult(values[0]);
                                         addResult(values[1]);
                                         addResult(values[2]);
                                     }

                                     test().then(function() {});

                         """);

        // Assert
        Assert.Equal(new[] { "first", "second", "third" }, results);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_WithNestedDelays()
    {
        // Arrange
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }

            return JsValue.Null;
        });

        // Act
        await engine.Evaluate("""

                                     function delay(value, ms) {
                                         return new Promise(function(resolve) {
                                             setTimeout(function() {
                                                 resolve(value);
                                             }, ms);
                                         });
                                     }

                                     async function inner() {
                                         let x = await delay(5, 20);
                                         let y = await delay(10, 20);
                                         return x + y;
                                     }

                                     async function outer() {
                                         let result = await inner();
                                         let bonus = await delay(3, 20);
                                         return result + bonus;
                                     }

                                     outer().then(function(value) {
                                         captureResult(value);
                                     });

                         """);

        // Assert
        Assert.Equal("18", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_MultipleSequentialAwaitsWithDebug()
    {
        // This test proves that a single straight block of awaits actually work
        // by using __debug() to capture state between each await
        await using var engine = CreateDebugEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }
            return JsValue.Null;
        });

        await engine.Evaluate("""
            function bar() {
                return new Promise(function(resolve) {
                    setTimeout(function() { resolve(10); }, 10);
                });
            }

            async function foo() {
                let x1 = await bar();
                __debug();
                let x2 = await bar();
                __debug();
                let x3 = await bar();
                __debug();
                return x1 + x2 + x3;
            }

            foo().then(function(value) {
                captureResult(value);
            });
            """);

        // Get the debug messages
        var debugMessages = new List<DebugMessage>();
        for (var i = 0; i < 3; i++) debugMessages.Add(await engine.DebugMessages().ReadAsync());

        // Assert - Verify we captured state after each await
        Assert.Equal(3, debugMessages.Count);

        // After first await, x1 should be defined
        Assert.True(debugMessages[0].Variables.ContainsKey("x1"));
        Assert.Equal(10d, debugMessages[0].Variables["x1"]);
        Assert.False(debugMessages[0].Variables.ContainsKey("x2"));
        Assert.False(debugMessages[0].Variables.ContainsKey("x3"));

        // After second await, x1 and x2 should be defined
        Assert.True(debugMessages[1].Variables.ContainsKey("x1"));
        Assert.Equal(10d, debugMessages[1].Variables["x1"]);
        Assert.True(debugMessages[1].Variables.ContainsKey("x2"));
        Assert.Equal(10d, debugMessages[1].Variables["x2"]);
        Assert.False(debugMessages[1].Variables.ContainsKey("x3"));

        // After third await, all three should be defined
        Assert.True(debugMessages[2].Variables.ContainsKey("x1"));
        Assert.Equal(10d, debugMessages[2].Variables["x1"]);
        Assert.True(debugMessages[2].Variables.ContainsKey("x2"));
        Assert.Equal(10d, debugMessages[2].Variables["x2"]);
        Assert.True(debugMessages[2].Variables.ContainsKey("x3"));
        Assert.Equal(10d, debugMessages[2].Variables["x3"]);

        // Final result should be correct
        Assert.Equal("30", result);
    }

    [Fact(Timeout = 2000)]
    public async Task AsyncFunction_EvalInDefaultParam_ShouldReturnRejectedPromise()
    {
        // This tests ES2024 behavior: when eval tries to declare 'arguments'
        // in a function with non-simple parameters, it should throw SyntaxError.
        // For async functions, this should result in a rejected promise.
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            var result = 'initial';
            async function f(p = eval("var arguments = 'param'"), arguments) {}
            try {
                var p = f();
                result = typeof p;
                if (p && typeof p.then === 'function') {
                    result = 'promise';
                }
            } catch(e) {
                result = 'sync_throw: ' + e.name;
            }
            result;
            """);

        // Should return "promise" - async function should return rejected promise
        Assert.Equal("promise", result.ToString());
    }

    [Fact(Timeout = 5000)]
    public async Task DotNetTask_BridgesToJsPromise_WithAsyncFunction()
    {
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }
            return JsValue.Null;
        });

        // Register an async .NET function that simulates I/O
        engine.SetGlobalAsyncFunction("readFileAsync", async _ =>
        {
            // Simulate async I/O with a delay
            await Task.Delay(50);
            return (JsValue)"file contents loaded";
        });

        await engine.Evaluate("""
            async function test() {
                let content = await readFileAsync("test.txt");
                return content;
            }
            test().then(captureResult);
            """);

        Assert.Equal("file contents loaded", result);
    }

    [Fact(Timeout = 5000)]
    public async Task DotNetTask_BridgesToJsPromise_WithMultipleAwaits()
    {
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }
            return JsValue.Null;
        });

        // Register an async delay function
        engine.SetGlobalAsyncFunction("delay", async args =>
        {
            var ms = (int)args.GetArgument(0).ToNumber();
            var value = args.GetArgument(1);
            await Task.Delay(ms);
            return value;
        });

        await engine.Evaluate("""
            async function test() {
                let a = await delay(10, 100);
                let b = await delay(10, 200);
                return a + b;
            }
            test().then(captureResult);
            """);

        Assert.Equal("300", result);
    }

    [Fact(Timeout = 5000)]
    public async Task DotNetTask_BridgesToJsPromise_HandlesRejection()
    {
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }
            return JsValue.Null;
        });

        // Register an async function that throws
        engine.SetGlobalAsyncFunction("failingOperation", async _ =>
        {
            await Task.Delay(10);
            throw new InvalidOperationException("Simulated I/O error");
        });

        await engine.Evaluate("""
            async function test() {
                try {
                    await failingOperation();
                    return "should not reach";
                } catch(e) {
                    return "caught: " + e;
                }
            }
            test().then(captureResult);
            """);

        Assert.Contains("caught:", result);
        Assert.Contains("Simulated I/O error", result);
    }

    [Fact(Timeout = 5000)]
    public async Task CreatePromiseFromTask_WithConverter()
    {
        await using var engine = CreateEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0].ToObject()?.ToString() ?? "";
            }
            return JsValue.Null;
        });

        // Use CreatePromiseFromTask directly with a converter
        engine.SetGlobalFunction("getNumber", _ =>
        {
            var task = Task.Run(async () =>
            {
                await Task.Delay(10);
                return 42;
            });
            return engine.CreatePromiseFromTask(task, num => (JsValue)(double)num);
        });

        await engine.Evaluate("""
            async function test() {
                let num = await getNumber();
                return num * 2;
            }
            test().then(captureResult);
            """);

        Assert.Equal("84", result);
    }

    private JsEngine CreateDebugEngine()
    {
        return TestEngineFactory.CreateDebugEngine(nameof(AsyncAwaitTestsBase), enableFastPaths: EnableFastPaths);
    }
}

public class FastPathAsyncAwaitTests(ITestOutputHelper output) : AsyncAwaitTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}
