using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for constant expression folding transformation.
/// </summary>
public class ConstantFoldingTests(ITestOutputHelper output)
{
    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_ArithmeticExpression_FoldsToResult()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 1 + 2 * 7; x;");

        // 1 + 2 * 7 = 1 + 14 = 15
        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_SimpleAddition_FoldsToResult()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 1 + 2; x;");

        Assert.Equal(3d, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_StringConcatenation_FoldsToResult()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = \"hello\" + \" \" + \"world\"; x;");

        Assert.Equal("hello world", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_BooleanLogic_FoldsToResult()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = true && false; x;");

        Assert.Equal(false, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_Comparison_FoldsToResult()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5 > 3; x;");

        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_WithVariables_DoesNotFold()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("let y = 5; let x = y + 2; x;");

        Assert.Equal(7d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_ShowsTransformation()
    {
        await using var engine = new JsEngine();
        var source = "let x = 1 + 2 * 7;";

        var (original, constantFolded, cpsTransformed) = engine.ParseWithTransformationSteps(source);

        // Original should have the arithmetic operations
        var originalLet = original.Rest.Head as Cons;
        var originalValue = originalLet?.Rest?.Rest?.Head as Cons;
        Assert.NotNull(originalValue);

        // The original should be (+ 1 (* 2 7))
        Assert.Equal(Symbol.Intern("+"), originalValue!.Head);

        // Constant folded should have the result
        var foldedLet = constantFolded.Rest.Head as Cons;
        var foldedValue = foldedLet?.Rest?.Rest?.Head;

        // The folded value should be 15 (1 + 2 * 7 = 1 + 14 = 15)
        Assert.Equal(15d, foldedValue);

        output.WriteLine("Original S-expression:");
        output.WriteLine(original.ToString());
        output.WriteLine("\nAfter constant folding:");
        output.WriteLine(constantFolded.ToString());
    }
}
