using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
[Category(TestCategories.AsyncRuntime)]
[Category(TestCategories.IteratorRuntime)]
public sealed class LanguageCrashRegressionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 5000)]
    public async Task AsyncPrivateGeneratorMethod_ObjectPropertyBinding_UsesBoundIdentifier()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var result = null;

            (async function() {
              var callCount = 0;
              var sawReferenceError = false;
              var sawValue = false;

              class C {
                async * #method({ x: y }) {
                  sawValue = y === 23;
                  try {
                    x;
                  } catch (e) {
                    sawReferenceError = e instanceof ReferenceError;
                  }

                  callCount = callCount + 1;
                }

                get method() {
                  return this.#method;
                }
              }

              await new C().method({ x: 23 }).next();
              return {
                callCount,
                sawValue,
                sawReferenceError
              };
            })().then(function(value) {
              result = value;
            });

            result;
            """);

        var obj = Assert.IsType<JsObject>(result);
        var hasCallCount = obj.TryGetProperty("callCount", out var callCount);
        var hasSawValue = obj.TryGetProperty("sawValue", out var sawValue);
        var hasSawReferenceError = obj.TryGetProperty("sawReferenceError", out var sawReferenceError);
        WriteObjectDiagnostics("async private generator method result", obj);
        Output.WriteLine(
            "callCount={0} sawValue={1} sawReferenceError={2}",
            Describe(callCount),
            Describe(sawValue),
            Describe(sawReferenceError));
        Assert.True(hasCallCount, MissingPropertyMessage("callCount", obj));
        Assert.True(hasSawValue, MissingPropertyMessage("sawValue", obj));
        Assert.True(hasSawReferenceError, MissingPropertyMessage("sawReferenceError", obj));
        Assert.Equal(1.0, callCount.AsDouble());
        Assert.True(sawValue.AsBoolean());
        Assert.True(sawReferenceError.AsBoolean());
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncPrivateGeneratorMethod_ObjectPropertyArrayInitializer_UsesDefaultElements()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var result = null;

            (async function() {
              var callCount = 0;
              var sawValues = false;
              var sawReferenceError = false;

              class C {
                async * #method({ w: [x, y, z] = [4, 5, 6] }) {
                  sawValues = x === 4 && y === 5 && z === 6;
                  try {
                    w;
                  } catch (e) {
                    sawReferenceError = e instanceof ReferenceError;
                  }

                  callCount = callCount + 1;
                }

                get method() {
                  return this.#method;
                }
              }

              await new C().method({}).next();
              return {
                callCount,
                sawValues,
                sawReferenceError
              };
            })().then(function(value) {
              result = value;
            });

            result;
            """);

        var obj = Assert.IsType<JsObject>(result);
        var callCount = RequireProperty(obj, "callCount");
        var sawValues = RequireProperty(obj, "sawValues");
        var sawReferenceError = RequireProperty(obj, "sawReferenceError");
        Assert.Equal(1.0, callCount.AsDouble());
        Assert.True(sawValues.AsBoolean());
        Assert.True(sawReferenceError.AsBoolean());
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncPrivateGeneratorMethod_InClassExpression_ObjectPropertyBinding_UsesBoundIdentifier()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var result = null;

            (async function() {
              var callCount = 0;
              var sawReferenceError = false;
              var sawValue = false;

              var C = class {
                async * #method({ x: y }) {
                  sawValue = y === 23;
                  try {
                    x;
                  } catch (e) {
                    sawReferenceError = e instanceof ReferenceError;
                  }

                  callCount = callCount + 1;
                }

                get method() {
                  return this.#method;
                }
              };

              await new C().method({ x: 23 }).next();
              return {
                callCount,
                sawValue,
                sawReferenceError
              };
            })().then(function(value) {
              result = value;
            });

            result;
            """);

        var obj = Assert.IsType<JsObject>(result);
        var callCount = RequireProperty(obj, "callCount");
        var sawValue = RequireProperty(obj, "sawValue");
        var sawReferenceError = RequireProperty(obj, "sawReferenceError");
        Assert.Equal(1.0, callCount.AsDouble());
        Assert.True(sawValue.AsBoolean());
        Assert.True(sawReferenceError.AsBoolean());
    }

    [Fact(Timeout = 5000)]
    public async Task ForAwaitOf_ArrayRestBinding_CopiesIterationValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var result = null;

            (async function() {
              var values = [1, 2, 3];
              var iterCount = 0;
              var snapshot = null;

              for await (let [...x] of [values]) {
                snapshot = [Array.isArray(x), x.length, x[0], x[1], x[2], x !== values];
                iterCount += 1;
              }

              return {
                iterCount,
                isArray: snapshot[0],
                length: snapshot[1],
                first: snapshot[2],
                second: snapshot[3],
                third: snapshot[4],
                copied: snapshot[5]
              };
            })().then(function(value) {
              result = value;
            });

            result;
            """);

        var obj = Assert.IsType<JsObject>(result);
        var hasIterCount = obj.TryGetProperty("iterCount", out var iterCount);
        var hasIsArray = obj.TryGetProperty("isArray", out var isArray);
        var hasLength = obj.TryGetProperty("length", out var length);
        var hasFirst = obj.TryGetProperty("first", out var first);
        var hasSecond = obj.TryGetProperty("second", out var second);
        var hasThird = obj.TryGetProperty("third", out var third);
        var hasCopied = obj.TryGetProperty("copied", out var copied);
        WriteObjectDiagnostics("for-await-of rest binding result", obj);
        Output.WriteLine(
            "iterCount={0} isArray={1} length={2} first={3} second={4} third={5} copied={6}",
            Describe(iterCount),
            Describe(isArray),
            Describe(length),
            Describe(first),
            Describe(second),
            Describe(third),
            Describe(copied));
        Assert.True(hasIterCount, MissingPropertyMessage("iterCount", obj));
        Assert.True(hasIsArray, MissingPropertyMessage("isArray", obj));
        Assert.True(hasLength, MissingPropertyMessage("length", obj));
        Assert.True(hasFirst, MissingPropertyMessage("first", obj));
        Assert.True(hasSecond, MissingPropertyMessage("second", obj));
        Assert.True(hasThird, MissingPropertyMessage("third", obj));
        Assert.True(hasCopied, MissingPropertyMessage("copied", obj));
        Assert.Equal(1.0, iterCount.AsDouble());
        Assert.True(isArray.AsBoolean());
        Assert.Equal(3.0, length.AsDouble());
        Assert.Equal(1.0, first.AsDouble());
        Assert.Equal(2.0, second.AsDouble());
        Assert.Equal(3.0, third.AsDouble());
        Assert.True(copied.AsBoolean());
    }

    [Fact(Timeout = 5000)]
    public async Task ForAwaitOf_AsyncGenerator_ArrayRestBinding_CopiesIterationValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var result = null;

            (async function() {
              var values = [1, 2, 3];
              var iterCount = 0;
              var snapshot = null;
              var asyncIter = (async function*() {
                yield* [values];
              })();

              async function *fn() {
                for await (const [...x] of asyncIter) {
                  snapshot = [Array.isArray(x), x.length, x[0], x[1], x[2], x !== values];
                  iterCount += 1;
                }
              }

              await fn().next();
              return {
                iterCount,
                isArray: snapshot[0],
                length: snapshot[1],
                first: snapshot[2],
                second: snapshot[3],
                third: snapshot[4],
                copied: snapshot[5]
              };
            })().then(function(value) {
              result = value;
            });

            result;
            """);

        var obj = Assert.IsType<JsObject>(result);
        var iterCount = RequireProperty(obj, "iterCount");
        var isArray = RequireProperty(obj, "isArray");
        var length = RequireProperty(obj, "length");
        var first = RequireProperty(obj, "first");
        var second = RequireProperty(obj, "second");
        var third = RequireProperty(obj, "third");
        var copied = RequireProperty(obj, "copied");
        Assert.Equal(1.0, iterCount.AsDouble());
        Assert.True(isArray.AsBoolean());
        Assert.Equal(3.0, length.AsDouble());
        Assert.Equal(1.0, first.AsDouble());
        Assert.Equal(2.0, second.AsDouble());
        Assert.Equal(3.0, third.AsDouble());
        Assert.True(copied.AsBoolean());
    }

    [Fact(Timeout = 5000)]
    public async Task ClassExpression_AfterSameLineComputedFields_InitializesOwnProperties()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var x = "b";

            var C = class {
              m() { return 42; } [x] = 42; [10] = "meep"; ["not initialized"];
            };

            var c = new C();
            [
              c.m(),
              Object.prototype.hasOwnProperty.call(c, "m"),
              c.b,
              c[10],
              Object.prototype.hasOwnProperty.call(c, "not initialized"),
              c["not initialized"]
            ];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(6, array.Length);
        Assert.Equal(42.0, array.GetElement(0).AsDouble());
        Assert.False(array.GetElement(1).AsBoolean());
        Assert.Equal(42.0, array.GetElement(2).AsDouble());
        Assert.Equal("meep", array.GetElement(3).AsString());
        Assert.True(array.GetElement(4).AsBoolean());
        Assert.True(array.GetElement(5).IsUndefined);
    }

    private JsValue RequireProperty(JsObject obj, string propertyName)
    {
        var found = obj.TryGetProperty(propertyName, out var value);
        Assert.True(found, MissingPropertyMessage(propertyName, obj));
        return value;
    }

    private void WriteObjectDiagnostics(string label, JsObject obj)
    {
        var keys = string.Join(", ", obj.GetOwnPropertyKeysInOrder(includeSymbols: false, includeNonEnumerable: true));
        Output.WriteLine("{0} keys=[{1}]", label, keys);
    }

    private static string MissingPropertyMessage(string propertyName, JsObject obj)
    {
        var keys = string.Join(", ", obj.GetOwnPropertyKeysInOrder(includeSymbols: false, includeNonEnumerable: true));
        return $"Missing property '{propertyName}'. keys=[{keys}]";
    }

    private static string Describe(JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Boolean => $"Boolean({value.AsBoolean()})",
            JsValueKind.Number => $"Number({value.AsDouble()})",
            JsValueKind.String => $"String({value.AsString()})",
            JsValueKind.Object => $"Object({value.ObjectValue?.GetType().Name ?? "null"})",
            JsValueKind.Undefined => "Undefined",
            JsValueKind.Null => "Null",
            _ => value.Kind.ToString()
        };
    }
}
