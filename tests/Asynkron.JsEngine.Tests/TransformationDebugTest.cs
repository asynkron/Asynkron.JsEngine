using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests to see the actual S-expression transformation output.
/// </summary>
public class TransformationDebugTest
{
    private readonly ITestOutputHelper _output;

    public TransformationDebugTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ShowTransformation_ForOfWithAwait()
    {
        var source = @"
            async function test() {
                let result = """";
                for (let item of [""a""]) {
                    let value = await Promise.resolve(item);
                    result = result + value;
                }
            }
        ";

        var engine = new JsEngine();
        
        // Parse without transformation
        var originalSexpr = engine.ParseWithoutTransformation(source);
        _output.WriteLine("=== ORIGINAL S-EXPRESSION ===");
        _output.WriteLine(originalSexpr.ToString());
        _output.WriteLine("");

        // Parse with transformation
        var transformedSexpr = engine.Parse(source);
        _output.WriteLine("=== TRANSFORMED S-EXPRESSION ===");
        _output.WriteLine(transformedSexpr.ToString());
    }

    [Fact]
    public async Task ShowTransformation_SimpleAsyncAwait()
    {
        // Simpler case that works
        var source = @"
            async function test() {
                let x = await Promise.resolve(5);
                return x;
            }
        ";

        var engine = new JsEngine();
        
        // Parse without transformation
        var originalSexpr = engine.ParseWithoutTransformation(source);
        _output.WriteLine("=== ORIGINAL (works) ===");
        _output.WriteLine(originalSexpr.ToString());
        _output.WriteLine("");

        // Parse with transformation
        var transformedSexpr = engine.Parse(source);
        _output.WriteLine("=== TRANSFORMED (works) ===");
        _output.WriteLine(transformedSexpr.ToString());
    }
}
