using Asynkron.JsEngine.JsTypes;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.IteratorRuntime)]
public sealed class YieldStarTryTests(ITestOutputHelper output)
{
    [Fact(Timeout = 10000)]
    public async Task SimpleYieldInTryBlock()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate(@"
function* gen() {
    try {
        yield 1;
        yield 2;
    } catch (e) {
        yield 'error';
    }
}

var it = gen();
var results = [];
results.push(it.next().value);
results.push(it.next().value);
results.push(it.next().value);
results;
");

        var array = Assert.IsType<JsArray>(result);
        output.WriteLine($"Results: {array.GetElement(0)}, {array.GetElement(1)}, {array.GetElement(2)}");
        
        Assert.Equal(1.0, array.GetElement(0).AsDouble());
        Assert.Equal(2.0, array.GetElement(1).AsDouble());
        Assert.True(array.GetElement(2).IsUndefined);
    }

    [Fact(Timeout = 10000)]
    public async Task YieldStarInTryBlock()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate(@"
function* inner() {
    yield 1;
    yield 2;
}

function* outer() {
    try {
        yield* inner();
    } catch (e) {
        yield 'error';
    }
}

var it = outer();
var results = [];
results.push(it.next().value);
results.push(it.next().value);
results.push(it.next().value);
results;
");

        var array = Assert.IsType<JsArray>(result);
        output.WriteLine($"Results: {array.GetElement(0)}, {array.GetElement(1)}, {array.GetElement(2)}");
        
        Assert.Equal(1.0, array.GetElement(0).AsDouble());
        Assert.Equal(2.0, array.GetElement(1).AsDouble());
        Assert.True(array.GetElement(2).IsUndefined);
    }

    [Fact(Timeout = 10000)]
    public async Task YieldStarInForOfInTryBlock()
    {
        await using var engine = new JsEngine();

        var result = await engine.Evaluate(@"
function* values() {
    yield 1;
    yield 2;
}

var i = 0;
var j = 0;

var controlIterator = (function*() {
    for (var x of values()) {
        try {
            i++;
            yield* values();
            j++;
        } catch (err) {}
    }
})();

controlIterator.next();
[i, j];
");

        var array = Assert.IsType<JsArray>(result);
        output.WriteLine($"i={array.GetElement(0)}, j={array.GetElement(1)}");
        
        // After first .next(), we should have:
        // - entered for-of once (x=1)
        // - incremented i (i=1)
        // - started yield* which yields 1
        Assert.Equal(1.0, array.GetElement(0).AsDouble());
        Assert.Equal(0.0, array.GetElement(1).AsDouble());
    }
}
