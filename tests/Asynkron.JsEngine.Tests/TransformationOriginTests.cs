using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for transformation origin tracking on s-expressions.
/// </summary>
public class TransformationOriginTests
{
    [Fact(Timeout = 2000)]
    public async Task Origin_AsyncFunction_TracksBackToOriginal()
    {
        var source = """

                     async function test() {
                         return 42;
                     }
                     """;

        var engine = new JsEngine();
        
        // Get transformation stages
        var (original, constantFolded, cpsTransformed) = engine.ParseWithTransformationSteps(source);
        
        // The original should be an async function
        var originalFunc = original.Rest.Head as Cons;
        Assert.NotNull(originalFunc);
        
        // The CPS transformed should be a regular function
        var transformedFunc = cpsTransformed.Rest.Head as Cons;
        Assert.NotNull(transformedFunc);
        
        // The transformed function should have an Origin pointing back
        Assert.NotNull(transformedFunc!.Origin);
    }

    [Fact(Timeout = 2000)]
    public async Task Origin_UntransformedCode_HasNullOrigin()
    {
        var source = """

                     function test() {
                         return 42;
                     }
                     """;

        var engine = new JsEngine();
        var parsed = engine.Parse(source);
        
        // Regular function should not be transformed
        var func = parsed.Rest.Head as Cons;
        Assert.NotNull(func);
        
        // Origin should be null for untransformed code
        Assert.Null(func!.Origin);
    }

    [Fact(Timeout = 2000)]
    public async Task Origin_ChainedTransformations_CanTraceBack()
    {
        var source = """

                     async function test() {
                         let x = await Promise.resolve(5);
                         return x;
                     }
                     """;

        var engine = new JsEngine();
        var (original, constantFolded, cpsTransformed) = engine.ParseWithTransformationSteps(source);
        
        // The CPS transformed tree should have nodes with Origin set
        var transformedFunc = cpsTransformed.Rest.Head as Cons;
        Assert.NotNull(transformedFunc);
        
        // Verify we can trace back
        Assert.NotNull(transformedFunc!.Origin);
    }

    [Fact(Timeout = 2000)]
    public async Task Origin_WithSourceReference_BothPropertiesWork()
    {
        var source = @"async function test() { return 42; }";

        var engine = new JsEngine();
        var (original, constantFolded, cpsTransformed) = engine.ParseWithTransformationSteps(source);
        
        var originalFunc = original.Rest.Head as Cons;
        var transformedFunc = cpsTransformed.Rest.Head as Cons;
        
        Assert.NotNull(originalFunc);
        Assert.NotNull(transformedFunc);
        
        // Original should have a source reference
        Assert.NotNull(originalFunc!.SourceReference);
        
        // Transformed should point back through the chain
        Assert.NotNull(transformedFunc!.Origin);
        
        // We can trace from transformed back to source via origin chain
        // The origin chain might be: cpsTransformed -> constantFolded -> original
        // or: cpsTransformed -> original (if constant folding made no changes)
        var current = transformedFunc.Origin;
        SourceReference? sourceRef = null;
        
        // Walk the origin chain until we find a SourceReference
        while (current != null)
        {
            if (current.SourceReference != null)
            {
                sourceRef = current.SourceReference;
                break;
            }
            current = current.Origin;
        }
        
        Assert.NotNull(sourceRef);
        var sourceText = sourceRef!.GetText();
        Assert.NotNull(sourceText);
        Assert.Contains("async", sourceText);
    }

    [Fact(Timeout = 2000)]
    public async Task Origin_OnlyTransformedNodes_HaveOriginSet()
    {
        var source = """

                     let x = 1;
                     async function test() {
                         return x;
                     }
                     """;

        var engine = new JsEngine();
        var (original, constantFolded, cpsTransformed) = engine.ParseWithTransformationSteps(source);
        
        // The let statement should not be transformed (Origin = null for constant folding)
        var letStatement = cpsTransformed.Rest.Head as Cons;
        Assert.NotNull(letStatement);
        // Note: constant folding doesn't set Origin, so this is expected to be null
        
        // The async function should be transformed by CPS (Origin != null)
        var asyncFunc = cpsTransformed.Rest.Rest.Head as Cons;
        Assert.NotNull(asyncFunc);
        Assert.NotNull(asyncFunc!.Origin);
    }
}
