using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for constant expression folding transformation.
/// </summary>
public class ConstantFoldingTests
{
    private readonly ITestOutputHelper _output;

    public ConstantFoldingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_ArithmeticExpression_FoldsToResult()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 1 + 2 * 7; x;");
        
        // 1 + 2 * 7 = 1 + 14 = 15
        Assert.Equal(15d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_SimpleAddition_FoldsToResult()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 1 + 2; x;");
        
        Assert.Equal(3d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_StringConcatenation_FoldsToResult()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = \"hello\" + \" \" + \"world\"; x;");
        
        Assert.Equal("hello world", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_BooleanLogic_FoldsToResult()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = true && false; x;");
        
        Assert.Equal(false, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_Comparison_FoldsToResult()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let x = 5 > 3; x;");
        
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_WithVariables_DoesNotFold()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("let y = 5; let x = y + 2; x;");
        
        Assert.Equal(7d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ConstantFolding_OriginChain_DebugTest()
    {
        var engine = new JsEngine();
        var source = @"async function test() { return 42; }";
        var (original, constantFolded, cpsTransformed) = engine.ParseWithTransformationSteps(source);
        
        _output.WriteLine("=== PROGRAM LEVEL ===");
        _output.WriteLine($"Original program has SourceReference: {original.SourceReference != null}");
        _output.WriteLine($"ConstantFolded program has SourceReference: {constantFolded.SourceReference != null}");
        _output.WriteLine($"CPS program has SourceReference: {cpsTransformed.SourceReference != null}");
        _output.WriteLine($"ConstantFolded program == Original program: {ReferenceEquals(constantFolded, original)}");
        
        _output.WriteLine("\n=== FUNCTION LEVEL ===");
        var originalFunc = original.Rest.Head as Cons;
        var constantFunc = constantFolded.Rest.Head as Cons;
        var cpsFunc = cpsTransformed.Rest.Head as Cons;
        
        _output.WriteLine($"Original func has SourceReference: {originalFunc?.SourceReference != null}");
        _output.WriteLine($"ConstantFolded func has SourceReference: {constantFunc?.SourceReference != null}");
        _output.WriteLine($"ConstantFolded func has Origin: {constantFunc?.Origin != null}");
        _output.WriteLine($"ConstantFolded func == Original func: {ReferenceEquals(constantFunc, originalFunc)}");
        _output.WriteLine($"CPS func has SourceReference: {cpsFunc?.SourceReference != null}");
        _output.WriteLine($"CPS func has Origin: {cpsFunc?.Origin != null}");
        
        if (cpsFunc?.Origin != null)
        {
            _output.WriteLine($"\n=== ORIGIN CHAIN ===");
            _output.WriteLine($"CPS.Origin has SourceReference: {cpsFunc.Origin.SourceReference != null}");
            _output.WriteLine($"CPS.Origin == ConstantFunc: {ReferenceEquals(cpsFunc.Origin, constantFunc)}");
            _output.WriteLine($"CPS.Origin has Origin: {cpsFunc.Origin.Origin != null}");
            
            if (cpsFunc.Origin.Origin != null)
            {
                _output.WriteLine($"CPS.Origin.Origin has SourceReference: {cpsFunc.Origin.Origin.SourceReference != null}");
                _output.WriteLine($"CPS.Origin.Origin == OriginalFunc: {ReferenceEquals(cpsFunc.Origin.Origin, originalFunc)}");
            }
        }
    }
}
