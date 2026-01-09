using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.IteratorRuntime)]
public sealed class DestructuringIteratorTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task ArrayPatternIteratorThrowsOriginalError()
    {
        await using var engine = CreateEngine();
        // This mirrors Test262's ary-init-iter-get-err case: the iterator getter throws.
        var ex = await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("""
            var iter = {};
            iter[Symbol.iterator] = function() {
              throw new Error("boom");
            };
        var f = ([x]) => {};
        f(iter);
        """));

        string? thrownName = null;
        if (ex.ThrownValue.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("name", out var name))
        {
            thrownName = JsOps.ToJsString(JsValue.FromObjectUnsafe(name.ToObject()), null);
        }
        Assert.Equal("Error", thrownName);
    }

    [Fact]
    public async Task StrictArrayPatternIteratorThrowsOriginalError()
    {
        await using var engine = CreateEngine();
        var ex = await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("""
            "use strict";
            var iter = {};
            iter[Symbol.iterator] = function() {
              throw new Error("boom-strict");
            };

            var f = ([x]) => {};
            f(iter);
            """));

        string? thrownName = null;
        if (ex.ThrownValue.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("name", out var name))
        {
            thrownName = JsOps.ToJsString(JsValue.FromObjectUnsafe(name.ToObject()), null);
        }
        Assert.Equal("Error", thrownName);
    }

    [Fact(Skip = "Bug #480: Missing Array prototype causes ReferenceError instead of TypeError")]
    public async Task DeletedArrayIteratorThrowsTypeError()
    {
        // This mimics Test262's ary-init-iter-get-err-array-prototype.js test
        await using var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("""
            delete Array.prototype[Symbol.iterator];
            var [x, y, z] = [1, 2, 3];
            """));

        // Check that it's a TypeError with proper properties
        string? thrownName = null;
        string? message = null;
        if (ex.ThrownValue.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty("name", out var nameVal))
            {
                thrownName = JsOps.ToJsString(JsValue.FromObjectUnsafe(nameVal.ToObject()), null);
            }
            if (obj.TryGetProperty("message", out var msgVal))
            {
                message = JsOps.ToJsString(JsValue.FromObjectUnsafe(msgVal.ToObject()), null);
            }
        }

        Output.WriteLine($"Error name: {thrownName}");
        Output.WriteLine($"Error message: {message}");
        Output.WriteLine($"ThrownValue kind: {ex.ThrownValue.Kind}");
        Output.WriteLine($"ThrownValue.ObjectValue type: {ex.ThrownValue.ObjectValue?.GetType().Name ?? "null"}");

        Assert.Equal("TypeError", thrownName);
    }

    [Fact]
    public async Task ElisionDestructuringConsumesIterator()
    {
        // This mimics Test262's ary-ptrn-elision-iter-close.js test
        await using var engine = CreateEngine();

        var result = await engine.Evaluate("""
            const iter = (function* () {
              yield 1;
              yield 2;
            })();

            function fn() {
              for (var [,] = iter; ; ) {
                return;
              }
            }

            fn();

            iter.next().done;
            """);

        Output.WriteLine($"Result: {result}");
        Output.WriteLine($"Result kind: {result?.GetType().Name}");

        // After destructuring with [,] (one elision), iter.next() should be done
        Assert.True(JsOps.ToBoolean(JsValue.FromObjectUnsafe(result)),
            $"Expected iter.next().done to be true, got {result}");
    }

    [Fact(Skip = "Bug #480: Missing Array prototype causes ReferenceError instead of TypeError")]
    public async Task DeletedArrayIteratorInForLoopThrowsTypeError()
    {
        // This mimics Test262's for loop version
        await using var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("""
            delete Array.prototype[Symbol.iterator];
            for (var [x, y, z] = [1, 2, 3]; false; ) { }
            """));

        // Check that it's a TypeError with proper properties
        string? thrownName = null;
        string? message = null;
        if (ex.ThrownValue.TryGetObject<JsObject>(out var obj))
        {
            if (obj.TryGetProperty("name", out var nameVal))
            {
                thrownName = JsOps.ToJsString(JsValue.FromObjectUnsafe(nameVal.ToObject()), null);
            }
            if (obj.TryGetProperty("message", out var msgVal))
            {
                message = JsOps.ToJsString(JsValue.FromObjectUnsafe(msgVal.ToObject()), null);
            }
        }

        Output.WriteLine($"Error name: {thrownName}");
        Output.WriteLine($"Error message: {message}");
        Output.WriteLine($"ThrownValue kind: {ex.ThrownValue.Kind}");
        Output.WriteLine($"ThrownValue.ObjectValue type: {ex.ThrownValue.ObjectValue?.GetType().Name ?? "null"}");

        Assert.Equal("TypeError", thrownName);
    }

    [Theory]
    [InlineData("0, [ x ] = iterable;", 1, 0)]
    [InlineData("0, [...x] = iterable;", 1, 0)]
    public async Task ArrayAssignmentDoesNotInvokeReturnOnIteratorThrow(string assignment, int expectedNext,
        int expectedReturn)
    {
        await using var engine = CreateEngine();
        var logger = new TestLogger();
        engine.RealmState.Logger = logger;
        var result = await engine.Evaluate($$"""
            var nextCount = 0;
            var returnCount = 0;
            var iterator = {
              next: function() {
                nextCount += 1;
                throw new Error("boom");
              },
              return: function() {
                returnCount += 1;
              }
            };
            var iterable = {};
            iterable[Symbol.iterator] = function() {
              return iterator;
            };

            try {
              {{assignment}}
            } catch (e) {
              // Swallow so we can observe the counters.
            }

            ({ next: nextCount, ret: returnCount });
            """);

        var obj = Assert.IsType<JsObject>(result);
        Assert.True(obj.TryGetProperty("next", out var next), "Missing next property");
        Assert.True(obj.TryGetProperty("ret", out var ret), "Missing ret property");
        Assert.Equal(expectedNext, JsOps.ToNumber(next));
        var iteratorCloseLogs = logger.Collector.Snapshot()
            .Where(r => r.Message.Contains("IteratorClose", StringComparison.Ordinal)).ToArray();
        var logSummary = string.Join(" | ", iteratorCloseLogs.Select(r => r.Message));
        Assert.True(JsOps.ToNumber(ret) == expectedReturn,
            $"returnCount={JsOps.ToNumber(ret)}; logs={logSummary}");
        Assert.True(
            iteratorCloseLogs.Length == expectedReturn,
            logSummary);
    }
}
