using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for generator functions (function*) and the iterator protocol.
/// </summary>
public class GeneratorTests
{
    [Fact]
    public void GeneratorFunction_CanBeDeclared()
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
    public void GeneratorFunction_ReturnsIteratorObject()
    {
        // Arrange
        var engine = new JsEngine();

        // Act
        var result = engine.EvaluateSync(@"
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
    public void Generator_HasNextMethod()
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
        var hasNext = engine.EvaluateSync("g.next;");

        // Assert - next should be callable
        Assert.NotNull(hasNext);
    }

    [Fact]
    public void Generator_YieldsSingleValue()
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
        var value = engine.EvaluateSync("result.value;");
        var done = engine.EvaluateSync("result.done;");

        // Assert
        Assert.Equal(42.0, value);
        Assert.False((bool)done!);
    }

    [Fact]
    public void Generator_YieldsMultipleValues()
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
        
        var r1Value = engine.EvaluateSync("g.next().value;");
        var r2Value = engine.EvaluateSync("g.next().value;");
        var r3Value = engine.EvaluateSync("g.next().value;");

        // Assert
        Assert.Equal(1.0, r1Value);
        Assert.Equal(2.0, r2Value);
        Assert.Equal(3.0, r3Value);
    }

    [Fact]
    public void Generator_ReturnsIteratorResult()
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
        var result = engine.EvaluateSync("result;");
        Assert.NotNull(result);
    }

    [Fact]
    public void Generator_IteratorResultHasValueAndDone()
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
        var value = engine.EvaluateSync("result.value;");
        var done = engine.EvaluateSync("result.done;");
        
        // Assert - both properties should exist
        Assert.NotNull(value);
        Assert.NotNull(done);
    }

    [Fact]
    public void Generator_CompletesWithDoneTrue()
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
        
        var done = engine.EvaluateSync("finalResult.done;");
        var value = engine.EvaluateSync("finalResult.value;");

        // Assert
        Assert.True((bool)done!);
        Assert.Null(value);
    }

    [Fact]
    public void Generator_YieldsExpressions()
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
        
        var r1 = engine.EvaluateSync("g.next().value;");
        var r2 = engine.EvaluateSync("g.next().value;");

        // Assert
        Assert.Equal(2.0, r1);
        Assert.Equal(6.0, r2);
    }

    [Fact]
    public void Generator_YieldsVariables()
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
        
        var r1 = engine.EvaluateSync("g.next().value;");
        var r2 = engine.EvaluateSync("g.next().value;");

        // Assert
        Assert.Equal(10.0, r1);
        Assert.Equal(20.0, r2);
    }

    [Fact]
    public void Generator_WithParameters()
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
        
        var r1 = engine.EvaluateSync("g.next().value;");
        var r2 = engine.EvaluateSync("g.next().value;");

        // Assert
        Assert.Equal(100.0, r1);
        Assert.Equal(101.0, r2);
    }

    [Fact]
    public void Generator_CanBeCalledMultipleTimes()
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
        
        var g1_r1 = engine.EvaluateSync("g1.next().value;");
        var g2_r1 = engine.EvaluateSync("g2.next().value;");
        var g1_r2 = engine.EvaluateSync("g1.next().value;");
        var g2_r2 = engine.EvaluateSync("g2.next().value;");

        // Assert - Each generator maintains independent state
        Assert.Equal(1.0, g1_r1);
        Assert.Equal(1.0, g2_r1);
        Assert.Equal(2.0, g1_r2);
        Assert.Equal(2.0, g2_r2);
    }

    [Fact]
    public void Generator_EmptyGenerator()
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
        
        var done = engine.EvaluateSync("result.done;");

        // Assert
        Assert.True((bool)done!);
    }

    [Fact]
    public void Generator_WithReturn()
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
        
        var r1Value = engine.EvaluateSync("r1.value;");
        var r1Done = engine.EvaluateSync("r1.done;");
        var r2Value = engine.EvaluateSync("r2.value;");
        var r2Done = engine.EvaluateSync("r2.done;");

        // Assert
        Assert.Equal(1.0, r1Value);
        Assert.False((bool)r1Done!);
        Assert.Equal(99.0, r2Value);
        Assert.True((bool)r2Done!);
    }

    [Fact]
    public void GeneratorExpression_CanBeAssigned()
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
        
        var value = engine.EvaluateSync("result.value;");

        // Assert
        Assert.Equal(42.0, value);
    }

    [Fact]
    public void Generator_HasReturnMethod()
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
        var hasReturn = engine.EvaluateSync("g[\"return\"];");

        // Assert - return should be callable
        Assert.NotNull(hasReturn);
    }

    [Fact]
    public void Generator_ReturnMethodCompletesGenerator()
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
        
        var returnValue = engine.EvaluateSync("returnResult.value;");
        var returnDone = engine.EvaluateSync("returnResult.done;");
        var nextDone = engine.EvaluateSync("nextResult.done;");

        // Assert
        Assert.Equal(99.0, returnValue);
        Assert.True((bool)returnDone!);
        Assert.True((bool)nextDone!);
    }

    [Fact]
    public void Generator_HasThrowMethod()
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
        var hasThrow = engine.EvaluateSync("g[\"throw\"];");

        // Assert - throw should be callable
        Assert.NotNull(hasThrow);
    }

    [Fact]
    public void ParseGeneratorSyntax_FunctionStar()
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
    public void ParseYieldExpression()
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
