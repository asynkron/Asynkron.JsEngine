using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for the CpsTransformer class that handles Continuation-Passing Style transformations.
/// </summary>
public class CpsTransformerTests
{
    [Fact(Timeout = 2000)]
    public async Task NeedsTransformation_WithRegularCode_ReturnsFalse()
    {
        // Arrange
        var transformer = new CpsTransformer();
        await using var engine = new JsEngine();
        var program = engine.Parse("let x = 1 + 2;");

        // Act
        var needsTransform = transformer.NeedsTransformation(program);

        // Assert
        Assert.False(needsTransform);
    }

    [Fact(Timeout = 2000)]
    public Task NeedsTransformation_WithNullProgram_ReturnsFalse()
    {
        // Arrange
        var transformer = new CpsTransformer();

        // Act
        var needsTransform = transformer.NeedsTransformation(null!);

        // Assert
        Assert.False(needsTransform);
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public Task NeedsTransformation_WithEmptyProgram_ReturnsFalse()
    {
        // Arrange
        var transformer = new CpsTransformer();
        var emptyProgram = Cons.Empty;

        // Act
        var needsTransform = transformer.NeedsTransformation(emptyProgram);

        // Assert
        Assert.False(needsTransform);
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public async Task Transform_ReturnsInputUnchanged()
    {
        // Arrange
        var transformer = new CpsTransformer();
        await using var engine = new JsEngine();
        var program = engine.Parse("let x = 1 + 2;");

        // Act
        var transformed = transformer.Transform(program);

        // Assert
        // For now, the transformer returns the input unchanged
        Assert.Same(program, transformed);
    }

    [Fact(Timeout = 2000)]
    public Task TransformerCanBeInstantiated()
    {
        // Arrange & Act
        var transformer = new CpsTransformer();

        // Assert
        Assert.NotNull(transformer);
        return Task.CompletedTask;
    }

    [Fact(Timeout = 2000)]
    public async Task NeedsTransformation_WithFunctionDeclaration_ReturnsFalse()
    {
        // Arrange
        var transformer = new CpsTransformer();
        await using var engine = new JsEngine();
        var program = engine.Parse("function test() { return 42; }");

        // Act
        var needsTransform = transformer.NeedsTransformation(program);

        // Assert
        // Regular functions don't need CPS transformation
        Assert.False(needsTransform);
    }

    [Fact(Timeout = 2000)]
    public async Task NeedsTransformation_WithComplexCode_ReturnsFalse()
    {
        // Arrange
        var transformer = new CpsTransformer();
        await using var engine = new JsEngine();
        var program = engine.Parse("""

                                               function fibonacci(n) {
                                                   if (n <= 1) return n;
                                                   return fibonacci(n - 1) + fibonacci(n - 2);
                                               }
                                               let result = fibonacci(10);

                                   """);

        // Act
        var needsTransform = transformer.NeedsTransformation(program);

        // Assert
        // Regular recursive functions don't need CPS transformation
        Assert.False(needsTransform);
    }
}
