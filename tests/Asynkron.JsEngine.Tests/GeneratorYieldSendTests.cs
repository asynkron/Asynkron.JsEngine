using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.IteratorRuntime)]
public sealed class GeneratorYieldSendTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task YieldYieldForwardsSentValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            function* g(){ yield yield; }
            var iter = g();
            var first = iter.next();
            var second = iter.next(123);
            var third = iter.next(456);
            [first, second, third];
        ");

        if (result is string error)
        {
            throw new InvalidOperationException($"script error: {error}");
        }

        var steps = Assert.IsType<JsArray>(result);
        var first = Assert.IsType<IJsObjectLike>(steps.Items[0].ToObject(), exactMatch: false);
        var second = Assert.IsType<IJsObjectLike>(steps.Items[1].ToObject(), exactMatch: false);
        var third = Assert.IsType<IJsObjectLike>(steps.Items[2].ToObject(), exactMatch: false);

        Assert.True(first.TryGetProperty("done", out var firstDone));
        Assert.False(firstDone.AsBoolean());

        Assert.True(second.TryGetProperty("value", out var secondValue));
        Assert.Equal(123d, secondValue.AsDouble());
        Assert.True(second.TryGetProperty("done", out var secondDone));
        Assert.False(secondDone.AsBoolean());

        Assert.True(third.TryGetProperty("done", out var thirdDone));
        Assert.True(thirdDone.AsBoolean());
        Assert.True(third.TryGetProperty("value", out _));
    }
    private static readonly string[] expected = new[] { "a", "b", "c" };
    private static readonly string[] expectedArray = new[] { "i", "g", "n", "o", "r", "e", "d" };

    [Fact]
    public async Task YieldYieldSpreadSuspendsAcrossOperandAndOuterYield()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let steps;
            try {
              function* g(){ yield [...yield yield]; }
              const iter = g();
              steps = [iter.next(false), iter.next(['a','b','c']), iter.next('ignored'), iter.next()];
            } catch (e) {
              steps = 'err:' + e?.message;
            }
            steps;
        ");

        var steps = Assert.IsType<JsArray>(result);
        Assert.Equal(4, steps.Items.Count);

        var first = Assert.IsType<IJsObjectLike>(steps.Items[0].ToObject(), exactMatch: false);
        var second = Assert.IsType<IJsObjectLike>(steps.Items[1].ToObject(), exactMatch: false);
        var third = Assert.IsType<IJsObjectLike>(steps.Items[2].ToObject(), exactMatch: false);
        var fourth = Assert.IsType<IJsObjectLike>(steps.Items[3].ToObject(), exactMatch: false);

        Assert.True(first.TryGetProperty("done", out var firstDone));
        Assert.False(firstDone.AsBoolean());
        Assert.True(first.TryGetProperty("value", out var firstValue) && firstValue.IsUndefined);

        if (!second.TryGetProperty("value", out var secondValue))
        {
            throw new InvalidOperationException($"second keys: {string.Join(",", second.Keys)}");
        }
        var secondArr = Assert.IsType<JsArray>(secondValue.ToObject());
        Assert.Equal(expected, secondArr.Items.Select(v => v.ToObject()).ToArray());
        if (!second.TryGetProperty("done", out var secondDone))
        {
            throw new InvalidOperationException($"second keys: {string.Join(",", second.Keys)}");
        }
        Assert.False(secondDone.AsBoolean());

        Assert.True(third.TryGetProperty("value", out var thirdValue));
        var thirdArr = Assert.IsType<JsArray>(thirdValue.ToObject());
        Assert.Equal(expectedArray, thirdArr.Items.Select(v => v.ToObject()).ToArray());
        Assert.True(third.TryGetProperty("done", out var thirdDone));
        Assert.False(thirdDone.AsBoolean());

        if (!fourth.TryGetProperty("done", out var fourthDone))
        {
            throw new InvalidOperationException($"fourth keys: {string.Join(",", fourth.Keys)} value={(fourth.TryGetProperty("value", out var v) ? v : JsValue.Null)}");
        }
        Assert.True(fourthDone.AsBoolean());
    }

    [Fact]
    public async Task YieldInDestructuringDefaultReceivesSentValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var x;
            var vals = {};
            var err;

            var iterator = {
              next: function() {
                return { done: false, value: undefined };
              }
            };
            var iterable = {};
            iterable[Symbol.iterator] = function() {
              return iterator;
            };

            function* g() {
              try {
                [ x = yield ] = iterable;
              } catch (e) {
                err = 'caught: ' + e;
              }
            }

            var iter = g();
            var first = iter.next();
            var second = iter.next(86);
            ({ first, second, x, err });
        ");

        if (result is string error)
        {
            throw new InvalidOperationException($"script error: {error}");
        }

        var obj = Assert.IsType<IJsObjectLike>(result, exactMatch: false);
        if (obj.TryGetProperty("err", out var errProp) && !errProp.IsUndefined)
        {
            throw new InvalidOperationException($"JS error: {errProp}");
        }
        Assert.True(obj.TryGetProperty("first", out var firstResult));
        var first = Assert.IsType<IJsObjectLike>(firstResult.ToObject(), exactMatch: false);
        Assert.True(first.TryGetProperty("done", out var firstDone));
        Assert.False(firstDone.AsBoolean());

        Assert.True(obj.TryGetProperty("second", out var secondResult));
        var second = Assert.IsType<IJsObjectLike>(secondResult.ToObject(), exactMatch: false);
        Assert.True(second.TryGetProperty("done", out var secondDone));
        Assert.True(secondDone.AsBoolean());

        Assert.True(obj.TryGetProperty("x", out var xValue));
        Assert.Equal(86d, xValue.AsDouble());
    }

    /// <summary>
    /// Test for #434: Multiple yields in array destructuring defaults.
    /// Verifies that the lowering correctly handles:
    ///   let [a = yield 1, b = yield 2] = [];
    /// </summary>
    [Fact]
    public async Task YieldInMultipleArrayDestructuringDefaults()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let results = [];

            function* gen() {
                let [a = yield 1, b = yield 2] = [];
                results.push(a, b);
            }

            let iter = gen();
            let r1 = iter.next();     // should yield 1
            let r2 = iter.next(10);   // a = 10, should yield 2
            let r3 = iter.next(20);   // b = 20, generator completes

            [
                r1.value,     // 1
                r1.done,      // false
                r2.value,     // 2
                r2.done,      // false
                r3.done,      // true
                results       // [10, 20]
            ];
        ");

        var steps = Assert.IsType<JsArray>(result);
        Assert.Equal(1d, steps.Items[0].AsDouble());     // r1.value = 1
        Assert.False(steps.Items[1].AsBoolean());        // r1.done = false
        Assert.Equal(2d, steps.Items[2].AsDouble());     // r2.value = 2
        Assert.False(steps.Items[3].AsBoolean());        // r2.done = false
        Assert.True(steps.Items[4].AsBoolean());         // r3.done = true

        var results = Assert.IsType<JsArray>(steps.Items[5].ToObject());
        Assert.Equal(10d, results.Items[0].AsDouble());  // a = 10
        Assert.Equal(20d, results.Items[1].AsDouble());  // b = 20
    }

    /// <summary>
    /// Test for #434: Yield in object destructuring defaults.
    /// Verifies that: let { x = yield 1 } = {};
    /// </summary>
    [Fact]
    public async Task YieldInObjectDestructuringDefault()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let captured;

            function* gen() {
                let { x = yield 'waiting' } = {};
                captured = x;
            }

            let iter = gen();
            let r1 = iter.next();       // should yield 'waiting'
            let r2 = iter.next('hello'); // x = 'hello', generator completes

            [r1.value, r1.done, r2.done, captured];
        ");

        var steps = Assert.IsType<JsArray>(result);
        Assert.Equal("waiting", steps.Items[0].AsString());  // r1.value
        Assert.False(steps.Items[1].AsBoolean());            // r1.done
        Assert.True(steps.Items[2].AsBoolean());             // r2.done
        Assert.Equal("hello", steps.Items[3].AsString());    // captured = 'hello'
    }

    /// <summary>
    /// Test for #434: Yield default NOT used when value is present.
    /// Verifies that: let [a = yield 1] = [42] does NOT yield (value is present).
    /// </summary>
    [Fact]
    public async Task YieldInDestructuringDefaultSkippedWhenValuePresent()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let captured;

            function* gen() {
                let [a = yield 'should not happen'] = [42];
                captured = a;
            }

            let iter = gen();
            let r1 = iter.next();  // should complete immediately without yielding

            [r1.done, captured];
        ");

        var steps = Assert.IsType<JsArray>(result);
        Assert.True(steps.Items[0].AsBoolean());         // r1.done = true (no yield)
        Assert.Equal(42d, steps.Items[1].AsDouble());    // captured = 42
    }

    /// <summary>
    /// Test for #434: Mixed yields and non-yields in array destructuring.
    /// Verifies: let [a = yield 1, b, c = yield 2] = [, 'present'];
    /// </summary>
    [Fact]
    public async Task YieldInMixedArrayDestructuringDefaults()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let captured;

            function* gen() {
                let [a = yield 'first', b, c = yield 'third'] = [, 'middle'];
                captured = [a, b, c];
            }

            let iter = gen();
            let r1 = iter.next();        // should yield 'first' (first element undefined)
            let r2 = iter.next('AAA');   // a = 'AAA', should yield 'third' (third element undefined)
            let r3 = iter.next('CCC');   // c = 'CCC', generator completes

            [r1.value, r2.value, r3.done, captured];
        ");

        var steps = Assert.IsType<JsArray>(result);
        Assert.Equal("first", steps.Items[0].AsString());    // r1.value
        Assert.Equal("third", steps.Items[1].AsString());    // r2.value
        Assert.True(steps.Items[2].AsBoolean());             // r3.done

        var captured = Assert.IsType<JsArray>(steps.Items[3].ToObject());
        Assert.Equal("AAA", captured.Items[0].AsString());    // a
        Assert.Equal("middle", captured.Items[1].AsString()); // b
        Assert.Equal("CCC", captured.Items[2].AsString());    // c
    }

    /// <summary>
    /// Test for #410: Basic yield in for-of array binding default.
    /// Verifies: for (let [x = yield 1] of [[]])
    /// </summary>
    [Fact]
    public async Task YieldInForOfArrayBindingDefault()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let captured = [];

            function* gen() {
                for (let [x = yield 'waiting'] of [[]]) {
                    captured.push(x);
                }
            }

            let iter = gen();
            let r1 = iter.next();        // should yield 'waiting' (element is undefined)
            let r2 = iter.next('hello'); // x = 'hello', loop body runs

            [r1.value, r1.done, r2.done, captured];
        ");

        var steps = Assert.IsType<JsArray>(result);
        Assert.Equal("waiting", steps.Items[0].AsString());  // r1.value
        Assert.False(steps.Items[1].AsBoolean());            // r1.done
        Assert.True(steps.Items[2].AsBoolean());             // r2.done

        var captured = Assert.IsType<JsArray>(steps.Items[3].ToObject());
        Assert.Single(captured.Items);
        Assert.Equal("hello", captured.Items[0].AsString()); // x = 'hello'
    }

    /// <summary>
    /// Test for #410: Multiple yields in for-of array binding defaults.
    /// Verifies: for (let [a = yield 1, b = yield 2] of [[]])
    /// </summary>
    [Fact]
    public async Task YieldInForOfMultipleArrayBindingDefaults()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let captured = [];

            function* gen() {
                for (let [a = yield 1, b = yield 2] of [[]]) {
                    captured.push(a, b);
                }
            }

            let iter = gen();
            let r1 = iter.next();     // should yield 1
            let r2 = iter.next(10);   // a = 10, should yield 2
            let r3 = iter.next(20);   // b = 20, loop body runs

            [
                r1.value,     // 1
                r1.done,      // false
                r2.value,     // 2
                r2.done,      // false
                r3.done,      // true
                captured      // [10, 20]
            ];
        ");

        var steps = Assert.IsType<JsArray>(result);
        Assert.Equal(1d, steps.Items[0].AsDouble());     // r1.value = 1
        Assert.False(steps.Items[1].AsBoolean());        // r1.done = false
        Assert.Equal(2d, steps.Items[2].AsDouble());     // r2.value = 2
        Assert.False(steps.Items[3].AsBoolean());        // r2.done = false
        Assert.True(steps.Items[4].AsBoolean());         // r3.done = true

        var captured = Assert.IsType<JsArray>(steps.Items[5].ToObject());
        Assert.Equal(10d, captured.Items[0].AsDouble()); // a = 10
        Assert.Equal(20d, captured.Items[1].AsDouble()); // b = 20
    }

    /// <summary>
    /// Test for #410: Yield in for-of object binding default.
    /// Verifies: for (let { x = yield 1 } of [{}])
    /// </summary>
    [Fact]
    public async Task YieldInForOfObjectBindingDefault()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let captured = [];

            function* gen() {
                for (let { x = yield 'obj-wait' } of [{}]) {
                    captured.push(x);
                }
            }

            let iter = gen();
            let r1 = iter.next();       // should yield 'obj-wait'
            let r2 = iter.next('value'); // x = 'value', loop body runs

            [r1.value, r1.done, r2.done, captured];
        ");

        var steps = Assert.IsType<JsArray>(result);
        Assert.Equal("obj-wait", steps.Items[0].AsString());  // r1.value
        Assert.False(steps.Items[1].AsBoolean());             // r1.done
        Assert.True(steps.Items[2].AsBoolean());              // r2.done

        var captured = Assert.IsType<JsArray>(steps.Items[3].ToObject());
        Assert.Single(captured.Items);
        Assert.Equal("value", captured.Items[0].AsString());  // x = 'value'
    }

    /// <summary>
    /// Test for #410: Yield default in for-of NOT used when value is present.
    /// Verifies: for (let [x = yield 1] of [[42]]) does NOT yield
    /// </summary>
    [Fact]
    public async Task YieldInForOfBindingDefaultSkippedWhenValuePresent()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let captured = [];

            function* gen() {
                for (let [x = yield 'should not happen'] of [[42]]) {
                    captured.push(x);
                }
            }

            let iter = gen();
            let r1 = iter.next();  // should complete immediately without yielding

            [r1.done, captured];
        ");

        var steps = Assert.IsType<JsArray>(result);
        Assert.True(steps.Items[0].AsBoolean());           // r1.done = true (no yield)

        var captured = Assert.IsType<JsArray>(steps.Items[1].ToObject());
        Assert.Single(captured.Items);
        Assert.Equal(42d, captured.Items[0].AsDouble());   // x = 42
    }

    /// <summary>
    /// Test for #410: Multiple iterations with yield in for-of binding defaults.
    /// Verifies yield/resume semantics work across loop iterations.
    /// </summary>
    [Fact]
    public async Task YieldInForOfBindingDefaultMultipleIterations()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            let captured = [];

            function* gen() {
                // First iteration has undefined -> yields
                // Second iteration has 99 -> doesn't yield
                // Third iteration has undefined -> yields
                for (let [x = yield 'need-value'] of [[undefined], [99], [undefined]]) {
                    captured.push(x);
                }
            }

            let iter = gen();
            let r1 = iter.next();       // iter 1: yield 'need-value'
            let r2 = iter.next('A');    // iter 1: x = 'A', body runs; iter 2: x = 99, body runs; iter 3: yield 'need-value'
            let r3 = iter.next('C');    // iter 3: x = 'C', body runs; done

            [r1.value, r1.done, r2.value, r2.done, r3.done, captured];
        ");

        var steps = Assert.IsType<JsArray>(result);
        Assert.Equal("need-value", steps.Items[0].AsString());  // r1.value
        Assert.False(steps.Items[1].AsBoolean());               // r1.done = false
        Assert.Equal("need-value", steps.Items[2].AsString());  // r2.value (second yield)
        Assert.False(steps.Items[3].AsBoolean());               // r2.done = false
        Assert.True(steps.Items[4].AsBoolean());                // r3.done = true

        var captured = Assert.IsType<JsArray>(steps.Items[5].ToObject());
        Assert.Equal(3, captured.Items.Count);
        Assert.Equal("A", captured.Items[0].AsString());        // first iteration
        Assert.Equal(99d, captured.Items[1].AsDouble());        // second iteration (no yield)
        Assert.Equal("C", captured.Items[2].AsString());        // third iteration
    }

}
