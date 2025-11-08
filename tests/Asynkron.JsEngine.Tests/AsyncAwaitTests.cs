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
    public void CpsTransformer_DetectsAsyncFunction()
    {
        // Arrange
        var transformer = new CpsTransformer();
        var engine = new JsEngine();
        var program = engine.Parse(@"
            async function test() {
                return 42;
            }
        ");

        // Act
        var needsTransform = transformer.NeedsTransformation(program);

        // Assert
        Assert.True(needsTransform);
    }

    [Fact]
    public void CpsTransformer_DetectsAwaitExpression()
    {
        // Arrange
        var transformer = new CpsTransformer();
        var engine = new JsEngine();
        var program = engine.Parse(@"
            async function test() {
                let value = await Promise.resolve(42);
                return value;
            }
        ");

        // Act
        var needsTransform = transformer.NeedsTransformation(program);

        // Assert
        Assert.True(needsTransform);
    }

    [Fact]
    public void CpsTransformer_TransformsAsyncFunction()
    {
        // Arrange
        var transformer = new CpsTransformer();
        var engine = new JsEngine();
        var program = engine.Parse(@"
            async function test() {
                return 42;
            }
        ");

        // Act
        var transformed = transformer.Transform(program);

        // Assert
        Assert.NotNull(transformed);
        Assert.NotEqual(program, transformed); // Should be a different instance
    }
}
