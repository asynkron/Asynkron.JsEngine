using Xunit;

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
}
