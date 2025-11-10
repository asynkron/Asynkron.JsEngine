using Xunit;
using System.Collections.Generic;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for async/await functionality.
/// </summary>
public class AsyncAwaitTests
{
    [Fact]
    public void AsyncFunction_CanBeParsed()
    {
        // Arrange
        var engine = new JsEngine();

        // Act & Assert - Should not throw
        var program = engine.Parse(@"
            async function test() {
                return 42;
            }
        ");
        
        Assert.NotNull(program);
    }

    [Fact]
    public void AsyncFunction_CanBeDeclared()
    {
        // Arrange
        var engine = new JsEngine();

        // Act & Assert - Should not throw
        engine.Evaluate(@"
            async function test() {
                return 42;
            }
        ");
    }

    [Fact]
    public void AsyncFunctionExpression_CanBeParsed()
    {
        // Arrange
        var engine = new JsEngine();

        // Act & Assert - Should not throw
        var program = engine.Parse(@"
            let test = async function() {
                return 42;
            };
        ");
        
        Assert.NotNull(program);
    }

    [Fact]
    public void AwaitExpression_CanBeParsed()
    {
        // Arrange
        var engine = new JsEngine();

        // Act & Assert - Should not throw
        var program = engine.Parse(@"
            async function test() {
                let result = await Promise.resolve(42);
                return result;
            }
        ");
        
        Assert.NotNull(program);
    }

    [Fact]
    public async Task AsyncFunction_ReturnsPromise()
    {
        // Arrange
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0]?.ToString() ?? "";
            }
            return null;
        });

        // Act
        await engine.Run(@"
            async function test() {
                return 42;
            }
            
            let p = test();
            p.then(function(value) {
                captureResult(value);
            });
        ");

        // Assert
        Assert.Equal("42", result);
    }

    [Fact]
    public async Task AsyncFunction_WithAwait_ReturnsValue()
    {
        // Arrange
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0]?.ToString() ?? "";
            }
            return null;
        });

        // Act
        await engine.Run(@"
            async function test() {
                let value = await Promise.resolve(42);
                return value;
            }
            
            test().then(function(value) {
                captureResult(value);
            });
        ");

        // Assert
        Assert.Equal("42", result);
    }

    [Fact]
    public async Task AsyncFunction_WithMultipleAwaits()
    {
        // Arrange
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0]?.ToString() ?? "";
            }
            return null;
        });

        // Act
        await engine.Run(@"
            async function test() {
                let a = await Promise.resolve(10);
                let b = await Promise.resolve(20);
                return a + b;
            }
            
            test().then(function(value) {
                captureResult(value);
            });
        ");

        // Assert
        Assert.Equal("30", result);
    }

    [Fact]
    public async Task AsyncFunction_WithAwaitInExpression()
    {
        // Arrange
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0]?.ToString() ?? "";
            }
            return null;
        });

        // Act
        await engine.Run(@"
            async function test() {
                let value = (await Promise.resolve(10)) + (await Promise.resolve(20));
                return value;
            }
            
            test().then(function(value) {
                captureResult(value);
            });
        ");

        // Assert
        Assert.Equal("30", result);
    }

    [Fact]
    public async Task AsyncFunction_HandlesRejection()
    {
        // Arrange
        var engine = new JsEngine();
        var caught = false;

        engine.SetGlobalFunction("markCaught", args =>
        {
            caught = true;
            return null;
        });

        // Act
        await engine.Run(@"
            async function test() {
                throw ""error"";
            }
            
            test()[""catch""](function(err) {
                markCaught();
            });
        ");

        // Assert
        Assert.True(caught);
    }

    [Fact]
    public async Task AsyncFunction_WithTryCatch()
    {
        // Arrange
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0]?.ToString() ?? "";
            }
            return null;
        });

        // Act
        await engine.Run(@"
            async function test() {
                try {
                    throw ""error"";
                } catch (e) {
                    return ""caught"";
                }
            }
            
            test().then(function(value) {
                captureResult(value);
            });
        ");

        // Assert
        Assert.Equal("caught", result);
    }

    [Fact]
    public async Task AsyncFunction_ChainedCalls()
    {
        // Arrange
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0]?.ToString() ?? "";
            }
            return null;
        });

        // Act
        await engine.Run(@"
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
        ");

        // Assert
        Assert.Equal("20", result);
    }

    [Fact]
    public async Task AsyncFunctionExpression_Works()
    {
        // Arrange
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0]?.ToString() ?? "";
            }
            return null;
        });

        // Act
        await engine.Run(@"
            let test = async function() {
                return 42;
            };
            
            test().then(function(value) {
                captureResult(value);
            });
        ");

        // Assert
        Assert.Equal("42", result);
    }

    [Fact]
    public void CpsTransformer_AlreadyTransformedCodeDoesNotNeedTransformation()
    {
        // Arrange
        var engine = new JsEngine();
        var transformer = new CpsTransformer();
        
        // engine.Parse() already applies CPS transformation, so the result
        // should not need transformation again
        var program = engine.Parse(@"
            async function test() {
                return 42;
            }
        ");

        // Act
        var needsTransform = transformer.NeedsTransformation(program);

        // Assert - Already transformed code should not need transformation
        Assert.False(needsTransform);
    }

    [Fact]
    public void CpsTransformer_AlreadyTransformedAwaitDoesNotNeedTransformation()
    {
        // Arrange
        var engine = new JsEngine();
        var transformer = new CpsTransformer();
        
        // engine.Parse() already applies CPS transformation, so the result
        // should not need transformation again
        var program = engine.Parse(@"
            async function test() {
                let value = await Promise.resolve(42);
                return value;
            }
        ");

        // Act
        var needsTransform = transformer.NeedsTransformation(program);

        // Assert - Already transformed code should not need transformation
        Assert.False(needsTransform);
    }

    [Fact]
    public void CpsTransformer_TransformIsIdempotent()
    {
        // Arrange
        var engine = new JsEngine();
        var transformer = new CpsTransformer();
        
        // engine.Parse() already applies CPS transformation
        var program = engine.Parse(@"
            async function test() {
                return 42;
            }
        ");

        // Act - Transform already-transformed code
        var transformed = transformer.Transform(program);

        // Assert - Should return the same program unchanged since it doesn't need transformation
        Assert.NotNull(transformed);
        Assert.Same(program, transformed); // Should be the same instance
    }

    [Fact]
    public async Task AsyncFunction_SequentialAwaits()
    {
        // Arrange
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0]?.ToString() ?? "";
            }
            return null;
        });

        // Act
        await engine.Run(@"
            async function test() {
                let a = await Promise.resolve(5);
                let b = await Promise.resolve(a + 3);
                let c = await Promise.resolve(b * 2);
                return c;
            }
            
            test().then(function(value) {
                captureResult(value);
            });
        ");

        // Assert
        Assert.Equal("16", result);
    }

    [Fact]
    public async Task AsyncFunction_ReturnsNull()
    {
        // Arrange
        var engine = new JsEngine();
        var wasCalled = false;

        engine.SetGlobalFunction("markCalled", args =>
        {
            wasCalled = true;
            return null;
        });

        // Act
        await engine.Run(@"
            async function test() {
                return null;
            }
            
            test().then(function(value) {
                markCalled();
            });
        ");

        // Assert
        Assert.True(wasCalled);
    }

    [Fact]
    public async Task AsyncFunction_NoReturn()
    {
        // Arrange
        var engine = new JsEngine();
        var wasCalled = false;

        engine.SetGlobalFunction("markCalled", args =>
        {
            wasCalled = true;
            return null;
        });

        // Act
        await engine.Run(@"
            async function test() {
                // No return statement
            }
            
            test().then(function(value) {
                markCalled();
            });
        ");

        // Assert
        Assert.True(wasCalled);
    }

    [Fact]
    public async Task AsyncFunction_WithSetTimeoutDelay_ReturnsValue()
    {
        // Arrange
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0]?.ToString() ?? "";
            }
            return null;
        });

        // Act
        await engine.Run(@"
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
        ");

        // Assert
        Assert.Equal("42", result);
    }

    [Fact]
    public async Task AsyncFunction_WithMultipleSetTimeoutDelays()
    {
        // Arrange
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0]?.ToString() ?? "";
            }
            return null;
        });

        // Act
        await engine.Run(@"
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
        ");

        // Assert
        Assert.Equal("30", result);
    }

    [Fact]
    public async Task AsyncFunction_WithDelayAndComputation()
    {
        // Arrange
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0]?.ToString() ?? "";
            }
            return null;
        });

        // Act
        await engine.Run(@"
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
        ");

        // Assert
        Assert.Equal("16", result);
    }

    [Fact]
    public async Task AsyncFunction_WithParallelDelays()
    {
        // Arrange
        var engine = new JsEngine();
        var results = new List<string>();

        engine.SetGlobalFunction("addResult", args =>
        {
            if (args.Count > 0)
            {
                results.Add(args[0]?.ToString() ?? "");
            }
            return null;
        });

        // Act
        await engine.Run(@"
            function delayedValue(value, delay) {
                return new Promise(function(resolve) {
                    setTimeout(function() {
                        resolve(value);
                    }, delay);
                });
            }
            
            async function test() {
                let p1 = delayedValue(""first"", 30);
                let p2 = delayedValue(""second"", 30);
                let p3 = delayedValue(""third"", 30);
                
                let values = await Promise.all([p1, p2, p3]);
                addResult(values[0]);
                addResult(values[1]);
                addResult(values[2]);
            }
            
            test();
        ");

        // Assert
        Assert.Equal(new[] { "first", "second", "third" }, results);
    }

    [Fact]
    public async Task AsyncFunction_WithNestedDelays()
    {
        // Arrange
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0]?.ToString() ?? "";
            }
            return null;
        });

        // Act
        await engine.Run(@"
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
        ");

        // Assert
        Assert.Equal("18", result);
    }

    [Fact]
    public async Task AsyncFunction_MultipleSequentialAwaitsWithDebug()
    {
        // This test proves that a single straight block of awaits actually work
        // by using __debug() to capture state between each await
        var engine = new JsEngine();
        var result = "";

        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                result = args[0]?.ToString() ?? "";
            }
            return null;
        });

        // Act
        await engine.Run(@"
            function bar() {
                return Promise.resolve(10);
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
        ");

        // Get the debug messages
        var debugMessages = new List<DebugMessage>();
        for (int i = 0; i < 3; i++)
        {
            debugMessages.Add(await engine.DebugMessages().ReadAsync());
        }

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
}
