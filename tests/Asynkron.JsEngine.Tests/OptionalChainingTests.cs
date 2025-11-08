using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

public class OptionalChainingTests
{
    [Fact]
    public void OptionalPropertyAccessNull()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let obj = null;
            obj?.name;
        ");
        Assert.True(result is Symbol sym && sym.Name == "undefined");
    }

    [Fact]
    public void OptionalPropertyAccessDefined()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let obj = { name: 'Alice' };
            obj?.name;
        ");
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void OptionalPropertyChain()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let obj = { user: { name: 'Bob' } };
            obj?.user?.name;
        ");
        Assert.Equal("Bob", result);
    }

    [Fact]
    public void OptionalPropertyChainNull()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let obj = { user: null };
            obj?.user?.name;
        ");
        Assert.True(result is Symbol sym && sym.Name == "undefined");
    }

    [Fact]
    public void OptionalMethodCallNull()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let obj = null;
            obj?.();
        ");
        Assert.True(result is Symbol sym && sym.Name == "undefined");
    }

    [Fact]
    public void OptionalMethodCallDefined()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let greet = function() { return 'Hello'; };
            greet?.();
        ");
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void OptionalIndexAccessNull()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let arr = null;
            arr?.[0];
        ");
        Assert.True(result is Symbol sym && sym.Name == "undefined");
    }

    [Fact]
    public void OptionalIndexAccessDefined()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let arr = [10, 20, 30];
            arr?.[1];
        ");
        Assert.Equal(20d, result);
    }

    [Fact]
    public void OptionalChainingShortCircuit()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let obj = null;
            let x = 0;
            let result = obj?.prop + (x = 1);
            x;
        ");
        // x should be 1 because the right side of + does evaluate
        Assert.Equal(1d, result);
    }

    [Fact]
    public void OptionalChainingWithUndefined()
    {
        var engine = new JsEngine();
        var result = engine.Evaluate(@"
            let obj = undefined;
            obj?.name;
        ");
        Assert.True(result is Symbol sym && sym.Name == "undefined");
    }
}
