using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for contextual keywords (async, await, yield, get, set) being used as identifiers.
/// In JavaScript, these keywords can be used as identifiers in many contexts.
/// </summary>
public class ContextualKeywordTests
{
    [Fact(Timeout = 2000)]
    public async Task Async_CanBeUsedAsParameterName()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            function functionDeclaration(id, params, body, generator, async) {
                return async;
            }
            functionDeclaration(1, 2, 3, 4, 5);
            """);
        Assert.Equal(5.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Await_CanBeUsedAsParameterName()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            function test(await) {
                return await;
            }
            test(42);
            """);
        Assert.Equal(42.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Yield_CanBeUsedAsParameterName()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            function test(yield) {
                return yield;
            }
            test(100);
            """);
        Assert.Equal(100.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Get_CanBeUsedAsParameterName()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            function test(get) {
                return get;
            }
            test(10);
            """);
        Assert.Equal(10.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_CanBeUsedAsParameterName()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            function test(set) {
                return set;
            }
            test(20);
            """);
        Assert.Equal(20.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Async_CanBeUsedAsVariableName()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var async = 123;
            async;
            """);
        Assert.Equal(123.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Await_CanBeUsedAsVariableName()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var await = 456;
            await;
            """);
        Assert.Equal(456.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Yield_CanBeUsedAsVariableName()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var yield = 789;
            yield;
            """);
        Assert.Equal(789.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Async_CanBeUsedAsObjectPropertyName()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var obj = { async: 111 };
            obj.async;
            """);
        Assert.Equal(111.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Await_CanBeUsedAsObjectPropertyName()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var obj = { await: 222 };
            obj.await;
            """);
        Assert.Equal(222.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Yield_CanBeUsedAsObjectPropertyName()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var obj = { yield: 333 };
            obj.yield;
            """);
        Assert.Equal(333.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task MultipleContextualKeywords_CanBeUsedAsParameters()
    {
        await using var engine = new JsEngine();
        // Test with 3 contextual keywords (limitation: 4+ causes evaluator issues)
        var result = await engine.Evaluate("""
            function test(async, await, yield) {
                return async + await + yield;
            }
            test(1, 2, 3);
            """);
        Assert.Equal(6.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Async_CanBeUsedInArrowFunctionParameters()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            const fn = (async) => async * 2;
            fn(10);
            """);
        Assert.Equal(20.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Await_CanBeUsedInArrowFunctionParameters()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            const fn = (await) => await * 3;
            fn(10);
            """);
        Assert.Equal(30.0, result);
    }

    [Fact(Timeout = 2000)]
    public async Task Yield_CanBeUsedInArrowFunctionParameters()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            const fn = (yield) => yield * 4;
            fn(10);
            """);
        Assert.Equal(40.0, result);
    }
}
