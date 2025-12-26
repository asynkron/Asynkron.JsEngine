using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class IteratorCloseDestructuringTestsBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task DestructuringAssignment_IteratorCloseNonObjectThrows()
    {
        await using var engine = CreateEngine();
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate(
            """
            var nextCount = 0;
            var returnCount = 0;
            var unreachable = 0;
            const iterator = {
              next() { nextCount++; return { done: false, value: undefined }; },
              return() { returnCount++; return null; }
            };
            const iterable = { [Symbol.iterator]() { return iterator; } };

            function* g() {
              let vals = iterable;
              [ {} = yield ] = vals;
              unreachable++;
              return vals;
            }

            const iter = g();
            iter.next();
            iter.return();
            """));

        Assert.Contains("TypeError", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1d, engine.GlobalObject["nextCount"]);
        Assert.Equal(1d, engine.GlobalObject["returnCount"]);
        Assert.Equal(0d, engine.GlobalObject["unreachable"]);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOfDestructuring_IteratorCloseNonObjectThrows()
    {
        await using var engine = CreateEngine();
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate(
            """
            var nextCount = 0;
            var returnCount = 0;
            var loopCount = 0;
            const iterator = {
              next() { nextCount++; return { done: false, value: undefined }; },
              return() { returnCount++; return null; }
            };
            const iterable = { [Symbol.iterator]() { return iterator; } };

            function* g() {
              for ([ {} = yield ] of [iterable]) {
                loopCount++;
              }
              loopCount = -1;
            }

            const iter = g();
            iter.next();
            iter.return();
            """));

        Assert.Contains("TypeError", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1d, engine.GlobalObject["nextCount"]);
        Assert.Equal(1d, engine.GlobalObject["returnCount"]);
        Assert.Equal(0d, engine.GlobalObject["loopCount"]);
    }

    [Fact(Timeout = 2000)]
    public async Task DestructuringAssignment_TypeErrorIsCatchableInJs()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            """
            var nextCount = 0;
            var returnCount = 0;
            var unreachable = 0;
            var iterator = {
              next() { nextCount++; return { done: false, value: undefined }; },
              return() { returnCount++; return null; }
            };
            var iterable = { [Symbol.iterator]() { return iterator; } };
            function* g() {
              var vals = iterable;
              [ {} = yield ] = vals;
              unreachable++;
            }

            var caught = false;
            var errName = null;
            try {
              var iter = g();
              iter.next();
              iter.return();
            } catch (err) {
              caught = err instanceof TypeError;
              errName = err && err.constructor ? err.constructor.name : null;
            }
            ({ caught, errName, nextCount, returnCount, unreachable });
            """);

        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(true, obj["caught"]);
        Assert.Equal("TypeError", obj["errName"]);
        Assert.Equal(1d, obj["nextCount"]);
        Assert.Equal(1d, obj["returnCount"]);
        Assert.Equal(0d, obj["unreachable"]);

        var strictResult = await engine.Evaluate(
            """
            "use strict";
            var nextCount = 0;
            var returnCount = 0;
            var unreachable = 0;
            var iterator = {
              next() { nextCount++; return { done: false, value: undefined }; },
              return() { returnCount++; return null; }
            };
            var iterable = { [Symbol.iterator]() { return iterator; } };
            function* g() {
              var vals = iterable;
              [ {} = yield ] = vals;
              unreachable++;
            }

            var caught = false;
            var errName = null;
            try {
              var iter = g();
              iter.next();
              iter.return();
            } catch (err) {
              caught = err instanceof TypeError;
              errName = err && err.constructor ? err.constructor.name : null;
            }
            ({ caught, errName, nextCount, returnCount, unreachable });
            """);

        var strictObj = Assert.IsType<JsObject>(strictResult);
        Assert.Equal(true, strictObj["caught"]);
        Assert.Equal("TypeError", strictObj["errName"]);
        Assert.Equal(1d, strictObj["nextCount"]);
        Assert.Equal(1d, strictObj["returnCount"]);
        Assert.Equal(0d, strictObj["unreachable"]);
    }

    [Fact(Timeout = 2000)]
    public async Task Test262StyleHarness_CatchesIteratorCloseTypeError()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate(
            """
            var assert = {
              _isSameValue(a, b) {
                if (a === b) {
                  return a !== 0 || 1 / a === 1 / b;
                }
                return a !== a && b !== b;
              },
              sameValue(actual, expected, message) {
                if (this._isSameValue(actual, expected)) {
                  return;
                }
                throw new Error(message || "Expected SameValue check to pass");
              },
              throws(expected, func, message) {
                var threw = false;
                try {
                  func();
                } catch (err) {
                  threw = true;
                  if (!(err instanceof expected)) {
                    throw new Error(message || ("Expected " + expected.name + " but got " + (err && err.constructor ? err.constructor.name : "unknown")));
                  }
                  return;
                }
                throw new Error(message || ("Expected " + expected.name + " but no exception was thrown"));
              }
            };

            var nextCount = 0;
            var returnCount = 0;
            var unreachable = 0;
            var iterator = {
              next() { nextCount++; return { done: false, value: undefined }; },
              return() { returnCount++; return null; }
            };
            var iterable = { [Symbol.iterator]() { return iterator; } };
            function* g() {
              var vals = iterable;
              [ {} = yield ] = vals;
              unreachable++;
            }

            var iter = g();
            iter.next();
            assert.sameValue(nextCount, 1);
            assert.sameValue(returnCount, 0);
            assert.throws(TypeError, function() {
              iter.return();
            });
            assert.sameValue(nextCount, 1);
            assert.sameValue(returnCount, 1);
            assert.sameValue(unreachable, 0);
            """);
    }
}

public class FastPathIteratorCloseDestructuringTests(ITestOutputHelper output) : IteratorCloseDestructuringTestsBase(output)
{
    protected override bool EnableFastPaths => true;
}

public class ReferenceIteratorCloseDestructuringTests(ITestOutputHelper output) : IteratorCloseDestructuringTestsBase(output)
{
    protected override bool EnableFastPaths => false;
}
