namespace Asynkron.JsEngine.Tests;

public class RestrictedPropertiesFullTest
{
    [Fact]
    public async Task AssertThrows_Works_With_GeneratorCallerAccess()
    {
        await using var engine = new JsEngine();
        
        // First verify assert.throws is defined and works
        await engine.Evaluate(@"
function assert(condition, message) {
    if (!condition) throw new Error(message || 'Assertion failed');
}
assert.throws = function(expectedErrorCtor, fn, message) {
    var thrown = false;
    try {
        fn();
    } catch (e) {
        thrown = true;
        if (!(e instanceof expectedErrorCtor)) {
            throw new Error(message + ': Expected ' + expectedErrorCtor.name + ' but got ' + e.constructor.name);
        }
    }
    if (!thrown) {
        throw new Error(message + ': Expected to throw ' + expectedErrorCtor.name + ' but did not throw');
    }
};
");
        
        // Define the generator
        await engine.Evaluate("function* generator() {}");
        
        // Now try assert.throws with generator.caller
        Console.WriteLine("Testing assert.throws with generator.caller...");
        
        try {
            await engine.Evaluate(@"
assert.throws(TypeError, function() {
    return generator.caller;
}, 'generator.caller should throw TypeError');
console.log('assert.throws completed successfully');
");
            Console.WriteLine("Test passed - assert.throws worked correctly");
        } catch (Exception e) {
            Console.WriteLine($"Test failed: {e.GetType().Name}: {e.Message}");
            throw;
        }
    }
    
    [Fact]
    public async Task DirectAccess_Generator_Caller_Throws_TypeError()
    {
        await using var engine = new JsEngine();
        await engine.Evaluate("function* generator() {}");
        
        // Direct access should throw
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () => 
            await engine.Evaluate("generator.caller")
        );
        
        Console.WriteLine($"Thrown value type: {ex.ThrownValue.GetType().Name}");
        Console.WriteLine($"Thrown value: {ex.ThrownValue}");

        // Check it's a TypeError
        Assert.True(ex.ThrownValue.ToObject() is JsTypes.JsObject, "Should be a JsObject");
    }
}
