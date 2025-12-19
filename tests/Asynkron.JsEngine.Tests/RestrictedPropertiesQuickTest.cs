namespace Asynkron.JsEngine.Tests;

public class RestrictedPropertiesQuickTest
{
    [Fact]
    public async Task Generator_HasNoOwnCallerProperty()
    {
        await using var engine = new JsEngine();
        
        var hasOwn = await engine.Evaluate(@"
            function* generator() {}
            generator.hasOwnProperty('caller');
        ");
        Console.WriteLine($"generator.hasOwnProperty('caller') = {hasOwn}");
        Assert.False((bool)hasOwn!);
    }
    
    [Fact]
    public async Task Generator_HasNoOwnArgumentsProperty()
    {
        await using var engine = new JsEngine();
        
        var hasOwn = await engine.Evaluate(@"
            function* generator() {}
            generator.hasOwnProperty('arguments');
        ");
        Console.WriteLine($"generator.hasOwnProperty('arguments') = {hasOwn}");
        Assert.False((bool)hasOwn!);
    }
    
    [Fact]
    public async Task Generator_CallerAccessThrowsTypeError()
    {
        await using var engine = new JsEngine();
        
        await engine.Evaluate("function* generator() {}");
        
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () => 
            await engine.Evaluate("generator.caller;")
        );
        Console.WriteLine($"Thrown: {ex.ThrownValue}");
    }
}
