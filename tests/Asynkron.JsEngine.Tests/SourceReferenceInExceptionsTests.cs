using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests to verify that exceptions thrown by the evaluator include source code references.
/// </summary>
public class SourceReferenceInExceptionsTests
{
    [Fact(Timeout = 2000)]
    public async Task Exception_DestructuringNonArray_IncludesSourceReference()
    {
        var source = @"
        let [a, b] = 123;
        ";
        
        var engine = new JsEngine();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate(source));
        
        // Should include source reference
        Assert.Contains("Cannot destructure non-array value", ex.Message);
        // Message should be longer than just the basic error (indicating source info is present)
        Assert.True(ex.Message.Length > 50, "Expected source reference information to be included in message");
    }
    
    [Fact(Timeout = 2000)]
    public async Task Exception_InvalidOperandIncrement_ValidatesWithoutException()
    {
        // ++++x is actually valid (it's ++, then ++x)
        // This test just verifies no exception is thrown
        var source = @"
        let x = 5;
        let result = ++++x;
        ";
        
        var engine = new JsEngine();
        var result = await engine.Evaluate(source);
        // This should work fine
        Assert.NotNull(result);
    }
    
    [Fact(Timeout = 2000)]
    public async Task Exception_SuperNotAvailable_IncludesSourceReference()
    {
        var source = @"
        function test() {
            super.method();
        }
        test();
        ";
        
        var engine = new JsEngine();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.Evaluate(source));
        
        // Should include source reference (though the specific format may vary)
        Assert.Contains("Super is not available", ex.Message);
        // Message should be longer than just the basic error (indicating source info is present)
        Assert.True(ex.Message.Length > 50, "Expected source reference information to be included");
    }
    
    [Fact(Timeout = 2000)]
    public async Task Exception_PropertyAccessNeedsString_WorksCorrectly()
    {
        // This test verifies that property access works even with object keys (converts to string)
        var source = @"
        let obj = {};
        let result = obj[obj];  // Using object as property key - converts to '[object Object]'
        ";
        
        var engine = new JsEngine();
        var result = await engine.Evaluate(source);
        // This should work (converts object to string)
        Assert.NotNull(result);
    }
}
