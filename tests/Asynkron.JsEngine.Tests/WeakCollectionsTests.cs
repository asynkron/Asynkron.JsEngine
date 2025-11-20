using System.Threading.Tasks;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class WeakCollectionsTests
{
    [Fact(Timeout = 2000)]
    public async Task WeakMap_CoreMethods_BehaveLikeNode()
    {
        await using var engine = new JsEngine();

        await engine.Evaluate("""
            const key1 = {};
            const key2 = {};
            const wm = new WeakMap();
            wm.set(key1, 42);
            wm.set(key2, "value");
        """);

        var hasKey1 = await engine.Evaluate("wm.has(key1);");
        var hasKey2 = await engine.Evaluate("wm.has(key2);");
        var hasKey3 = await engine.Evaluate("wm.has({});");
        var value1 = await engine.Evaluate("wm.get(key1);");
        var value2 = await engine.Evaluate("wm.get(key2);");
        var value3 = await engine.Evaluate("wm.get({});");

        var deleted = await engine.Evaluate("wm.delete(key1);");
        var hasKey1AfterDelete = await engine.Evaluate("wm.has(key1);");

        Assert.True((bool)hasKey1!);
        Assert.True((bool)hasKey2!);
        Assert.False((bool)hasKey3!);
        Assert.Equal(42.0, value1);
        Assert.Equal("value", value2);
        Assert.True(value3 is Asynkron.JsEngine.Ast.Symbol); // undefined sentinel
        Assert.True((bool)deleted!);
        Assert.False((bool)hasKey1AfterDelete!);
    }

    [Fact(Timeout = 2000)]
    public async Task WeakSet_CoreMethods_BehaveLikeNode()
    {
        await using var engine = new JsEngine();

        await engine.Evaluate("""
            const value1 = {};
            const value2 = {};
            const ws = new WeakSet();
            ws.add(value1);
            ws.add(value2);
        """);

        var hasValue1 = await engine.Evaluate("ws.has(value1);");
        var hasValue2 = await engine.Evaluate("ws.has(value2);");
        var hasValue3 = await engine.Evaluate("ws.has({});");

        var deleted = await engine.Evaluate("ws.delete(value1);");
        var hasValue1AfterDelete = await engine.Evaluate("ws.has(value1);");

        Assert.True((bool)hasValue1!);
        Assert.True((bool)hasValue2!);
        Assert.False((bool)hasValue3!);
        Assert.True((bool)deleted!);
        Assert.False((bool)hasValue1AfterDelete!);
    }
}

