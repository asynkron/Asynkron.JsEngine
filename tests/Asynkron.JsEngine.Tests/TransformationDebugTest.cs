using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests to see the actual S-expression transformation output.
/// </summary>
public class TransformationDebugTest(ITestOutputHelper output)
{
    [Fact(Timeout = 2000)]
    public async Task ShowTransformation_ForOfWithAwait()
    {
        var source = """

                                 async function test() {
                                     let result = "";
                                     for (let item of ["a"]) {
                                         let value = await Promise.resolve(item);
                                         result = result + value;
                                     }
                                 }
                             
                     """;

        var engine = new JsEngine();

        // Parse without transformation
        var originalSexpr = engine.ParseWithoutTransformation(source);
        output.WriteLine("=== ORIGINAL S-EXPRESSION ===");
        output.WriteLine(originalSexpr.ToString());
        output.WriteLine("");

        // Parse with transformation
        var transformedSexpr = engine.Parse(source);
        output.WriteLine("=== TRANSFORMED S-EXPRESSION ===");
        output.WriteLine(transformedSexpr.ToString());
    }

    [Fact(Timeout = 2000)]
    public async Task ShowTransformation_SimpleAsyncAwait()
    {
        // Simpler case that works
        var source = """

                                 async function test() {
                                     let x = await Promise.resolve(5);
                                     return x;
                                 }
                             
                     """;

        var engine = new JsEngine();

        // Parse without transformation
        var originalSexpr = engine.ParseWithoutTransformation(source);
        output.WriteLine("=== ORIGINAL (works) ===");
        output.WriteLine(originalSexpr.ToString());
        output.WriteLine("");

        // Parse with transformation
        var transformedSexpr = engine.Parse(source);
        output.WriteLine("=== TRANSFORMED (works) ===");
        output.WriteLine(transformedSexpr.ToString());
    }
}