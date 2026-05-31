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
    public async Task OptionalChainingShortCircuit_DoesNotSkipUnrelatedBinaryOperand()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let obj = null;
            let right = 0;
            let total = obj?.nested?.value + (right += 3);
            right === 3 && Number.isNaN(total);
            """);

        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalChaining_NestedOptionalCallAndPropertyStayShortCircuited()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let obj = {
                child: null,
                getChild() {
                    return this.child;
                }
            };

            let calls = 0;
            let value = obj?.getChild?.()?.[(calls++, 'prop')]?.deep;
            value === undefined && calls === 0;
            """);

        Assert.Equal(true, result);
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

    [Fact(Timeout = 2000)]
    public async Task OptionalChainPlainCall_NullBase_YieldsUndefined()
    {
        // gh2806 AC-2: a?.b.c() — null base short-circuits to undefined.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(a) { return a?.box.read(); }
            invoke(null);
            """);
        Assert.True(result is Symbol { Name: "undefined" });
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalChainPlainCall_RealBase_CallsCorrectly()
    {
        // gh2806 AC-2: a?.b.c() — real base resolves and calls c on a.b with correct receiver.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(a, value) { return a?.box.read(value); }
            let obj = { box: { value: 0, read(v) { this.value = v; return this.value; } } };
            invoke(obj, 42);
            """);
        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalChainPlainCall_BaseEvaluatedOnce()
    {
        // gh2806 AC-2: base expression must be evaluated exactly once.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let calls = 0;
            function getA() { calls++; return { box: { read() { return 7; } } }; }
            function invoke(fn) { return fn()?.box.read(); }
            invoke(getA);
            calls;
            """);
        Assert.Equal(1d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalChainReceiverOptionalCall_NullBase_YieldsUndefined()
    {
        // gh2806 AC-3: a?.b?.c() — null a short-circuits to undefined.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(a) { return a?.box?.read(); }
            invoke(null);
            """);
        Assert.True(result is Symbol { Name: "undefined" });
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalChainReceiverOptionalCall_NullIntermediate_YieldsUndefined()
    {
        // gh2806 AC-3: a?.b?.c() — null a.b short-circuits to undefined.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(a) { return a?.box?.read(); }
            invoke({ box: null });
            """);
        Assert.True(result is Symbol { Name: "undefined" });
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalChainReceiverOptionalCall_RealChain_CallsCorrectly()
    {
        // gh2806 AC-3: a?.b?.c() — real chain calls c on a.b with correct receiver.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(a, value) { return a?.box?.read(value); }
            let obj = { box: { value: 0, read(v) { this.value = v; return this.value; } } };
            invoke(obj, 99);
            """);
        Assert.Equal(99d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task OptionalChainReceiverOptionalCall_BaseEvaluatedOnce()
    {
        // gh2806 AC-3: base expression must be evaluated exactly once.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let calls = 0;
            function getA() { calls++; return { box: { read() { return 5; } } }; }
            function invoke(fn) { return fn()?.box?.read(); }
            invoke(getA);
            calls;
            """);
        Assert.Equal(1d, result);
    }
}
