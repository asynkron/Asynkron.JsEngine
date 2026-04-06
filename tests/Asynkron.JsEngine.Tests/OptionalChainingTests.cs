using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class OptionalChainingTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task OptionalPropertyAccessNull()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = null;
                                                       obj?.name;

                                           """);
        Assert.True(result is Symbol { Name: "undefined" });
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalPropertyAccessDefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { name: 'Alice' };
                                                       obj?.name;

                                           """);
        Assert.Equal("Alice", result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalPropertyChain()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { user: { name: 'Bob' } };
                                                       obj?.user?.name;

                                           """);
        Assert.Equal("Bob", result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalPropertyChainNull()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { user: null };
                                                       obj?.user?.name;

                                           """);
        Assert.True(result is Symbol { Name: "undefined" });
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalMethodCallNull()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = null;
                                                       obj?.();

                                           """);
        Assert.True(result is Symbol { Name: "undefined" });
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalMethodCallDefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let greet = function() { return 'Hello'; };
                                                       greet?.();

                                           """);
        Assert.Equal("Hello", result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalComputedMethodCallPreservesThis()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let key = 'read';
                                                       let box = {
                                                           value: 42,
                                                           read() { return this.value; }
                                                       };
                                                       box[key]?.();

                                           """);
        Assert.Equal(42d, result);
    }

    // NOTE: This test may timeout when run in parallel with other tests due to event queue processing delays.
    // The feature is implemented correctly and the test passes when run individually.
    [Fact(Timeout = 2000)]
    public async Task OptionalIndexAccessNull()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = null;
                                                       arr?.[0];

                                           """);
        Assert.True(result is Symbol { Name: "undefined" });
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalIndexAccessDefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [10, 20, 30];
                                                       arr?.[1];

                                           """);
        Assert.Equal(20d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalDeleteNamedProperty()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { name: 'Alice' };
                                                       let removed = delete obj?.name;
                                                       removed && !('name' in obj);

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalDeleteComputedPropertyShortCircuitsPropertyEvaluation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = null;
                                                       let keyCalls = 0;
                                                       let removed = delete obj?.[(keyCalls++, 'name')];
                                                       removed && keyCalls === 0;

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalChainingShortCircuit()
    {
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = undefined;
                                                       obj?.name;

                                           """);
        Assert.True(result is Symbol { Name: "undefined" });
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalChainingOnFunctionExpression()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("(function foo() {}?.name)");
        Assert.Equal("foo", result);
    }
}
