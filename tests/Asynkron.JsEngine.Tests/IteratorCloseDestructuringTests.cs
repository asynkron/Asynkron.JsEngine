using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.IteratorRuntime)]
public sealed class IteratorCloseDestructuringTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatchArrayDestructuring_DefaultThrow_ClosesIterator_AndBubblesOutward(bool strict)
    {
        await using var engine = CreateEngine();
        var strictPrefix = strict ? "\"use strict\";\n" : string.Empty;
        var result = await engine.Evaluate(
            $$"""
              {{strictPrefix}}
              var nextCount = 0;
              var returnCount = 0;
              var iterator = {
                next() {
                  nextCount++;
                  return { done: false, value: undefined };
                },
                return() {
                  returnCount++;
                  return { done: true };
                }
              };
              var iterable = { [Symbol.iterator]() { return iterator; } };
              var caught;
              try {
                try {
                  throw iterable;
                } catch ([x = (() => { throw new Error("default boom"); })()]) {
                  caught = "inner catch";
                }
              } catch (err) {
                caught = err && err.message;
              }
              ({ nextCount, returnCount, caught });
              """);

        var summary = Assert.IsType<JsObject>(result);
        Assert.Equal(1d, summary["nextCount"]);
        Assert.Equal(1d, summary["returnCount"]);
        Assert.Equal("default boom", summary["caught"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatchArrayDestructuring_NestedPatternDefault_WorksInBothModes(bool strict)
    {
        await using var engine = CreateEngine();
        var strictPrefix = strict ? "\"use strict\";\n" : string.Empty;
        var result = await engine.Evaluate(
            $$"""
              {{strictPrefix}}
              var value = -1;
              try {
                throw [[undefined]];
              } catch ([[x = 7]]) {
                value = x;
              }
              value;
              """);

        Assert.Equal(7d, result);
    }

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

    [Fact(Timeout = 2000, Skip = "Bug #479: Catch parameter shadowing causes duplicate declaration errors")]
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

    /// <summary>
    /// Reproduces the hang when generator.return() is called while the generator is
    /// suspended at a yield inside a computed property of a destructuring target,
    /// and the iterator's return() method throws.
    /// Pattern: [ {}[yield] ] = vals with iterator.return() that throws.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task DestructuringComputedYield_ReturnWithThrowingIteratorClose_DoesNotHang()
    {
        await using var engine = CreateEngine();
        var ex = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate(
            """
            var iterator = {
              next() { return { done: false, value: undefined }; },
              return() { throw new Error("close error"); }
            };
            var iterable = {};
            iterable[Symbol.iterator] = function() { return iterator; };

            function* g() {
                var result;
                var vals = iterable;
                result = [ {}[yield] ] = vals;
            }
            var iter = g();
            iter.next();
            iter.return();
            """));

        // The error from iterator.return() should propagate
        Assert.Contains("close error", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Same pattern but where iterator.return() does NOT throw -- should complete normally.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task DestructuringComputedYield_ReturnWithNonThrowingIteratorClose_CompletesNormally()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            """
            var returnCount = 0;
            var iterator = {
              next() { return { done: false, value: undefined }; },
              return() { returnCount++; return { done: true, value: undefined }; }
            };
            var iterable = {};
            iterable[Symbol.iterator] = function() { return iterator; };

            function* g() {
                var result;
                var vals = iterable;
                result = [ {}[yield] ] = vals;
            }
            var iter = g();
            iter.next();
            var r = iter.return(42);
            returnCount;
            """);

        Assert.Equal(1d, result);
    }

    /// <summary>
    /// Generator parameter destructuring with elision pattern -- tests class dstr pattern.
    /// This is the pattern found in Test262 Statements_class_dstr tests.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task GeneratorParameterDestructuring_Elision_BindsParametersAndRoutesResumable()
    {
        await using var engine = CreateEngine();

        // The sync-generator method *method([,]) has a non-simple (elided array pattern)
        // parameter list. Eager IteratorBindingInitialization runs at call time: the single
        // elision advances the passed generator once (first += 1) then the pattern is
        // exhausted and the iterator is closed (second stays 0). The body then routes through
        // the resumable VM and increments callCount.
        var result = await engine.Evaluate(
            """
            var first = 0;
            var second = 0;
            function* g() {
              first += 1;
              yield;
              second += 1;
            }

            var callCount = 0;
            class C {
              *method([,]) {
                callCount = callCount + 1;
              }
            }

            new C().method(g()).next();
            ({ callCount, first, second });
            """);

        var summary = Assert.IsType<JsObject>(result);
        Assert.Equal(1d, summary["callCount"]);
        Assert.Equal(1d, summary["first"]);
        Assert.Equal(0d, summary["second"]);
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                "unified-bytecode-resumable-generator-fast-path",
                StringComparison.Ordinal) &&
                record.Message.Contains("argc=1", StringComparison.Ordinal));
    }

    /// <summary>
    /// Generator method with parameter destructuring using a generator as iterable.
    /// This is the pattern used in class/dstr Test262 tests (async generator method with [x] param).
    /// The async generator method destructures its argument, which closes the iterator when done.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorMethod_ParameterDestructuring_BindsParametersAndRoutesResumable()
    {
        await using var engine = CreateEngine();

        // The async-generator method async *method([x]) has a non-simple (array pattern)
        // parameter list. Eager IteratorBindingInitialization binds [x] at call time by
        // stepping the supplied iterable once (x = null), then — because the pattern is
        // exhausted while the iterator is not done — closes it (return() runs, doneCallCount
        // becomes 1). The body then routes through the resumable VM and increments callCount.
        var finalResult = await engine.EvaluateAndAwait(
            """
            var output;
            var doneCallCount = 0;
            var iter = {};
            iter[Symbol.iterator] = function() {
              return {
                next: function() {
                  return { value: null, done: false };
                },
                return: function() {
                  doneCallCount += 1;
                  return {};
                }
              };
            };

            var callCount = 0;
            class C {
              async *method([x]) {
                callCount = callCount + 1;
              }
            }

            new C().method(iter).next().then(function() {
              output = ({ callCount, doneCallCount });
            });
            output;
            """);

        var summary = Assert.IsType<JsObject>(finalResult);
        Assert.Equal(1d, summary["callCount"]);
        Assert.Equal(1d, summary["doneCallCount"]);
    }

    /// <summary>
    /// Tests that for-of loop inside a generator correctly handles return()
    /// during destructuring. This is the core pattern that causes infinite loops.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task ForOfInsideGenerator_ReturnDuringIteration_DoesNotHang()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            """
            var returnCalled = false;
            function* g() {
                for (var x of [1, 2, 3]) {
                    yield x;
                }
            }
            var iter = g();
            iter.next();       // yields 1
            var r = iter.return(42);  // should close iterator and return {value: 42, done: true}
            r.value;
            """);

        Assert.Equal(42d, result);
    }

    /// <summary>
    /// For-of destructuring inside generator with return() called while suspended.
    /// Pattern: for ([x] of iterable) { yield x; }
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task ForOfDestructuringInsideGenerator_ReturnDuringYield_DoesNotHang()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            """
            function* g() {
                for (var [x] of [[1], [2], [3]]) {
                    yield x;
                }
            }
            var iter = g();
            iter.next();       // yields 1
            var r = iter.return(42);  // should return
            r.value;
            """);

        Assert.Equal(42d, result);
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
