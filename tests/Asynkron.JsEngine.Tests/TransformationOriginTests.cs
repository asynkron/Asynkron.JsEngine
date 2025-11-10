using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for transformation origin tracking on s-expressions.
/// </summary>
public class TransformationOriginTests
{
    [Fact]
    public async Task Origin_AsyncFunction_TracksBackToOriginal()
    {
        var source = @"
async function test() {
    return 42;
}";

        var engine = new JsEngine();
        
        // Get both original and transformed
        var (original, transformed) = engine.ParseWithTransformationSteps(source);
        
        // The original should be an async function
        var originalFunc = original.Rest.Head as Cons;
        Assert.NotNull(originalFunc);
        
        // The transformed should be a regular function
        var transformedFunc = transformed.Rest.Head as Cons;
        Assert.NotNull(transformedFunc);
        
        // The transformed function should have an Origin pointing back to the original
        Assert.NotNull(transformedFunc!.Origin);
        Assert.Same(originalFunc, transformedFunc.Origin);
    }

    [Fact]
    public async Task Origin_UntransformedCode_HasNullOrigin()
    {
        var source = @"
function test() {
    return 42;
}";

        var engine = new JsEngine();
        var parsed = engine.Parse(source);
        
        // Regular function should not be transformed
        var func = parsed.Rest.Head as Cons;
        Assert.NotNull(func);
        
        // Origin should be null for untransformed code
        Assert.Null(func!.Origin);
    }

    [Fact]
    public async Task Origin_ChainedTransformations_CanTraceBack()
    {
        var source = @"
async function test() {
    let x = await Promise.resolve(5);
    return x;
}";

        var engine = new JsEngine();
        var (original, transformed) = engine.ParseWithTransformationSteps(source);
        
        // The transformed tree should have nodes with Origin set
        var transformedFunc = transformed.Rest.Head as Cons;
        Assert.NotNull(transformedFunc);
        
        // Verify we can trace back
        Assert.NotNull(transformedFunc!.Origin);
        
        // The origin should be the original async function
        var originalFunc = original.Rest.Head as Cons;
        Assert.Same(originalFunc, transformedFunc.Origin);
    }

    [Fact]
    public async Task Origin_WithSourceReference_BothPropertiesWork()
    {
        var source = @"async function test() { return 42; }";

        var engine = new JsEngine();
        var (original, transformed) = engine.ParseWithTransformationSteps(source);
        
        var originalFunc = original.Rest.Head as Cons;
        var transformedFunc = transformed.Rest.Head as Cons;
        
        Assert.NotNull(originalFunc);
        Assert.NotNull(transformedFunc);
        
        // Original should have a source reference
        Assert.NotNull(originalFunc!.SourceReference);
        
        // Transformed should point back to original
        Assert.NotNull(transformedFunc!.Origin);
        Assert.Same(originalFunc, transformedFunc.Origin);
        
        // We can trace from transformed back to source via origin
        var sourceText = transformedFunc.Origin!.SourceReference?.GetText();
        Assert.NotNull(sourceText);
        Assert.Contains("async", sourceText);
    }

    [Fact]
    public async Task Origin_OnlyTransformedNodes_HaveOriginSet()
    {
        var source = @"
let x = 1;
async function test() {
    return x;
}";

        var engine = new JsEngine();
        var (original, transformed) = engine.ParseWithTransformationSteps(source);
        
        // The let statement should not be transformed (Origin = null)
        var letStatement = transformed.Rest.Head as Cons;
        Assert.NotNull(letStatement);
        Assert.Null(letStatement!.Origin);
        
        // The async function should be transformed (Origin != null)
        var asyncFunc = transformed.Rest.Rest.Head as Cons;
        Assert.NotNull(asyncFunc);
        Assert.NotNull(asyncFunc!.Origin);
    }
}
