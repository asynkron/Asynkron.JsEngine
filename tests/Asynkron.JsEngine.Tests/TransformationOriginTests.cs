using Asynkron.JsEngine;
using Asynkron.JsEngine.Lisp;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for transformation origin tracking on s-expressions.
/// </summary>
public class TransformationOriginTests
{
    [Fact(Timeout = 2000)]
    public void Origin_AsyncFunction_TracksBackToOriginal()
    {
        var source = """

                     async function test() {
                         return 42;
                     }
                     """;

        var (original, cpsTransformed) = ParseAndTransform(source);

        var originalFunc = original.Rest.Head as Cons;
        Assert.NotNull(originalFunc);

        Assert.NotNull(cpsTransformed);
        var transformedFunc = cpsTransformed!.Rest.Head as Cons;
        Assert.NotNull(transformedFunc);
        Assert.NotNull(transformedFunc!.Origin);
    }

    [Fact(Timeout = 2000)]
    public void Origin_UntransformedCode_HasNullOrigin()
    {
        var source = """

                     function test() {
                         return 42;
                     }
                     """;

        var parsed = JsEngine.ParseWithoutTransformation(source);

        // Regular function should not be transformed
        var func = parsed.Rest.Head as Cons;
        Assert.NotNull(func);

        // Origin should be null for untransformed code
        Assert.Null(func!.Origin);
    }

    [Fact(Timeout = 2000)]
    public void Origin_ChainedTransformations_CanTraceBack()
    {
        var source = """

                     async function test() {
                         let x = await Promise.resolve(5);
                         return x;
                     }
                     """;

        var (_, cpsTransformed) = ParseAndTransform(source);

        Assert.NotNull(cpsTransformed);
        var transformedFunc = cpsTransformed!.Rest.Head as Cons;
        Assert.NotNull(transformedFunc);
        Assert.NotNull(transformedFunc!.Origin);
    }

    [Fact(Timeout = 2000)]
    public void Origin_WithSourceReference_BothPropertiesWork()
    {
        var source = @"async function test() { return 42; }";

        var (original, cpsTransformed) = ParseAndTransform(source);

        var originalFunc = original.Rest.Head as Cons;
        Assert.NotNull(originalFunc);
        Assert.NotNull(originalFunc!.SourceReference);

        Assert.NotNull(cpsTransformed);
        var transformedFunc = cpsTransformed!.Rest.Head as Cons;
        Assert.NotNull(transformedFunc);
        Assert.NotNull(transformedFunc!.Origin);

        var current = transformedFunc.Origin;
        SourceReference? sourceRef = null;
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
    public void Origin_OnlyTransformedNodes_HaveOriginSet()
    {
        var source = """

                     let x = 1;
                     async function test() {
                         return x;
                     }
                     """;

        var (_, cpsTransformed) = ParseAndTransform(source);

        Assert.NotNull(cpsTransformed);
        var transformedProgram = cpsTransformed!;

        var letStatement = transformedProgram.Rest.Head as Cons;
        Assert.NotNull(letStatement);
        Assert.Null(letStatement!.Origin);

        var asyncFunc = transformedProgram.Rest.Rest.Head as Cons;
        Assert.NotNull(asyncFunc);
        Assert.NotNull(asyncFunc!.Origin);
    }

    private static (Cons original, Cons? transformed) ParseAndTransform(string source)
    {
        var original = JsEngine.ParseWithoutTransformation(source);
        var cpsTransformer = new CpsTransformer();
        var transformed = CpsTransformer.NeedsTransformation(original)
            ? cpsTransformer.Transform(original)
            : null;
        return (original, transformed);
    }
}
