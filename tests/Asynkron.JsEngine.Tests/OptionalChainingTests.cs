using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class OptionalChainingTests
{
    [Fact(Timeout = 2000)]
    public async Task OptionalPropertyAccessNull()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj = null;
                                                       obj?.name;
                                                   
                                           """);
        Assert.True(result is Symbol sym && sym.Name == "undefined");
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalPropertyAccessDefined()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { name: 'Alice' };
                                                       obj?.name;
                                                   
                                           """);
        Assert.Equal("Alice", result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalPropertyChain()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { user: { name: 'Bob' } };
                                                       obj?.user?.name;
                                                   
                                           """);
        Assert.Equal("Bob", result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalPropertyChainNull()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { user: null };
                                                       obj?.user?.name;
                                                   
                                           """);
        Assert.True(result is Symbol sym && sym.Name == "undefined");
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalMethodCallNull()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj = null;
                                                       obj?.();
                                                   
                                           """);
        Assert.True(result is Symbol sym && sym.Name == "undefined");
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalMethodCallDefined()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let greet = function() { return 'Hello'; };
                                                       greet?.();
                                                   
                                           """);
        Assert.Equal("Hello", result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task OptionalIndexAccessNull()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = null;
                                                       arr?.[0];
                                                   
                                           """);
        Assert.True(result is Symbol sym && sym.Name == "undefined");
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalIndexAccessDefined()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [10, 20, 30];
                                                       arr?.[1];
                                                   
                                           """);
        Assert.Equal(20d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalChainingShortCircuit()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj = null;
                                                       let x = 0;
                                                       let result = obj?.prop + (x = 1);
                                                       x;
                                                   
                                           """);
        // x should be 1 because the right side of + does evaluate
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalChainingWithUndefined()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let obj = undefined;
                                                       obj?.name;
                                                   
                                           """);
        Assert.True(result is Symbol sym && sym.Name == "undefined");
    }
}