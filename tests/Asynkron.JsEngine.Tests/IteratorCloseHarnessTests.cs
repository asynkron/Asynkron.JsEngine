using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

public class IteratorCloseHarnessTests
{
    [Fact(Timeout = 2000)]
    public async Task ForOfDestructuring_IteratorCloseNonObjectCaught_Sloppy()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(HarnessForOfScript(useStrict: false));

        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(true, obj["caught"]);
        Assert.Equal("TypeError", obj["errName"]);
        Assert.True((double)obj["nextCount"] >= 1d);
        Assert.True((double)obj["returnCount"] >= 1d);
        Assert.Equal(0d, obj["unreachable"]);
    }

    [Fact(Timeout = 2000)]
    public async Task ForOfDestructuring_IteratorCloseNonObjectCaught_Strict()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(HarnessForOfScript(useStrict: true));

        var obj = Assert.IsType<JsObject>(result);
        Assert.Equal(true, obj["caught"]);
        Assert.Equal("TypeError", obj["errName"]);
        Assert.True((double)obj["nextCount"] >= 1d);
        Assert.True((double)obj["returnCount"] >= 1d);
        Assert.Equal(0d, obj["unreachable"]);
    }

    private static string HarnessForOfScript(bool useStrict)
    {
        var strictPrefix = useStrict ? "\"use strict\";\n" : string.Empty;
        return $$"""
            {{strictPrefix}}
            var assert = {
              _isSameValue(a, b) {
                if (a === b) {
                  return a !== 0 || 1 / a === 1 / b;
                }
                return a !== a && b !== b;
              },
              sameValue(actual, expected, message) {
                if (this._isSameValue(actual, expected)) return;
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
                }
                if (!threw) {
                  throw new Error(message || ("Expected " + expected.name + " but no exception was thrown"));
                }
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
              for ([ {} = yield ] of [iterable]) {
                unreachable++;
              }
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
            """;
    }
}
