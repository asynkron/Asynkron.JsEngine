using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for generator functions (function*) and the iterator protocol.
/// </summary>
public class GeneratorTests
{
    [Fact]
    public async Task GeneratorFunction_CanBeDeclared()
    {
        // Arrange
        var engine = new JsEngine();

        // Act & Assert - Should not throw
        engine.EvaluateSync(@"
            function* simpleGenerator() {
                yield 1;
            }
        ");
    }

    [Fact]
    public async Task GeneratorFunction_ReturnsIteratorObject()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        var result = await engine.Evaluate(@"
            function* gen() {
                yield 1;
            }
            gen();
        ");

        // Assert
        Assert.NotNull(result);
        // The result should be an object (we can't directly check JsObject since it's internal)
    }

    [Fact]
    public async Task Generator_HasNextMethod()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
                yield 1;
            }
            let g = gen();
        ");
        var hasNext = await engine.Evaluate("g.next;");

        // Assert - next should be callable
        Assert.NotNull(hasNext);
    }

    [Fact]
    public async Task Generator_YieldsSingleValue()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
                yield 42;
            }
            let g = gen();
            let result = g.next();
        ");
        var value = await engine.Evaluate("result.value;");
        var done = await engine.Evaluate("result.done;");

        // Assert
        Assert.Equal(42.0, value);
        Assert.False((bool)done!);
    }

    [Fact]
    public async Task Generator_YieldsMultipleValues()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
                yield 1;
                yield 2;
                yield 3;
            }
            let g = gen();
        ");
        
        var r1Value = await engine.Evaluate("g.next().value;");
        var r2Value = await engine.Evaluate("g.next().value;");
        var r3Value = await engine.Evaluate("g.next().value;");

        // Assert
        Assert.Equal(1.0, r1Value);
        Assert.Equal(2.0, r2Value);
        Assert.Equal(3.0, r3Value);
    }

    [Fact]
    public async Task Generator_ReturnsIteratorResult()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
                yield 10;
            }
            let g = gen();
            let result = g.next();
        ");
        
        // Assert - result should be an object with value and done properties
        var result = await engine.Evaluate("result;");
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Generator_IteratorResultHasValueAndDone()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
                yield 5;
            }
            let g = gen();
            let result = g.next();
        ");
        
        // Check the properties exist by accessing them
        var value = await engine.Evaluate("result.value;");
        var done = await engine.Evaluate("result.done;");
        
        // Assert - both properties should exist
        Assert.NotNull(value);
        Assert.NotNull(done);
    }

    [Fact]
    public async Task Generator_CompletesWithDoneTrue()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
                yield 1;
            }
            let g = gen();
            g.next();  // Get the yielded value
            let finalResult = g.next();  // Generator is done
        ");
        
        var done = await engine.Evaluate("finalResult.done;");
        var value = await engine.Evaluate("finalResult.value;");

        // Assert
        Assert.True((bool)done!);
        Assert.Null(value);
    }

    [Fact]
    public async Task Generator_YieldsExpressions()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
                yield 1 + 1;
                yield 2 * 3;
            }
            let g = gen();
        ");
        
        var r1 = await engine.Evaluate("g.next().value;");
        var r2 = await engine.Evaluate("g.next().value;");

        // Assert
        Assert.Equal(2.0, r1);
        Assert.Equal(6.0, r2);
    }

    [Fact]
    public async Task Generator_YieldsVariables()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
                let x = 10;
                yield x;
                let y = 20;
                yield y;
            }
            let g = gen();
        ");
        
        var r1 = await engine.Evaluate("g.next().value;");
        var r2 = await engine.Evaluate("g.next().value;");

        // Assert
        Assert.Equal(10.0, r1);
        Assert.Equal(20.0, r2);
    }

    [Fact]
    public async Task Generator_WithParameters()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen(start) {
                yield start;
                yield start + 1;
            }
            let g = gen(100);
        ");
        
        var r1 = await engine.Evaluate("g.next().value;");
        var r2 = await engine.Evaluate("g.next().value;");

        // Assert
        Assert.Equal(100.0, r1);
        Assert.Equal(101.0, r2);
    }

    [Fact]
    public async Task Generator_CanBeCalledMultipleTimes()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
                yield 1;
                yield 2;
            }
            let g1 = gen();
            let g2 = gen();
        ");
        
        var g1_r1 = await engine.Evaluate("g1.next().value;");
        var g2_r1 = await engine.Evaluate("g2.next().value;");
        var g1_r2 = await engine.Evaluate("g1.next().value;");
        var g2_r2 = await engine.Evaluate("g2.next().value;");

        // Assert - Each generator maintains independent state
        Assert.Equal(1.0, g1_r1);
        Assert.Equal(1.0, g2_r1);
        Assert.Equal(2.0, g1_r2);
        Assert.Equal(2.0, g2_r2);
    }

    [Fact]
    public async Task Generator_EmptyGenerator()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
            }
            let g = gen();
            let result = g.next();
        ");
        
        var done = await engine.Evaluate("result.done;");

        // Assert
        Assert.True((bool)done!);
    }

    [Fact]
    public async Task Generator_WithReturn()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
                yield 1;
                return 99;
            }
            let g = gen();
            let r1 = g.next();
            let r2 = g.next();
        ");
        
        var r1Value = await engine.Evaluate("r1.value;");
        var r1Done = await engine.Evaluate("r1.done;");
        var r2Value = await engine.Evaluate("r2.value;");
        var r2Done = await engine.Evaluate("r2.done;");

        // Assert
        Assert.Equal(1.0, r1Value);
        Assert.False((bool)r1Done!);
        Assert.Equal(99.0, r2Value);
        Assert.True((bool)r2Done!);
    }

    [Fact]
    public async Task GeneratorExpression_CanBeAssigned()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            let gen = function*() {
                yield 42;
            };
            let g = gen();
            let result = g.next();
        ");
        
        var value = await engine.Evaluate("result.value;");

        // Assert
        Assert.Equal(42.0, value);
    }

    [Fact]
    public async Task Generator_HasReturnMethod()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
                yield 1;
                yield 2;
            }
            let g = gen();
        ");
        var hasReturn = await engine.Evaluate("g[\"return\"];");

        // Assert - return should be callable
        Assert.NotNull(hasReturn);
    }

    [Fact]
    public async Task Generator_ReturnMethodCompletesGenerator()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
                yield 1;
                yield 2;
            }
            let g = gen();
            g.next();  // Get first value
            let returnResult = g[""return""](99);
            let nextResult = g.next();  // Should be done
        ");
        
        var returnValue = await engine.Evaluate("returnResult.value;");
        var returnDone = await engine.Evaluate("returnResult.done;");
        var nextDone = await engine.Evaluate("nextResult.done;");

        // Assert
        Assert.Equal(99.0, returnValue);
        Assert.True((bool)returnDone!);
        Assert.True((bool)nextDone!);
    }

    [Fact]
    public async Task Generator_HasThrowMethod()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        engine.EvaluateSync(@"
            function* gen() {
                yield 1;
            }
            let g = gen();
        ");
        var hasThrow = await engine.Evaluate("g[\"throw\"];");

        // Assert - throw should be callable
        Assert.NotNull(hasThrow);
    }

    [Fact]
    public async Task ParseGeneratorSyntax_FunctionStar()
    {
        // Arrange
        var engine = new JsEngine();

        // Act & Assert - Should parse without error
        var program = engine.Parse(@"
            function* myGenerator() {
                yield 1;
            }
        ");
        
        Assert.NotNull(program);
    }

    [Fact]
    public async Task ParseYieldExpression()
    {
        // Arrange
        var engine = new JsEngine();

        // Act & Assert - Should parse without error
        var program = engine.Parse(@"
            function* gen() {
                let x = 5;
                yield x + 1;
            }
        ");
        
        Assert.NotNull(program);
    }
}
