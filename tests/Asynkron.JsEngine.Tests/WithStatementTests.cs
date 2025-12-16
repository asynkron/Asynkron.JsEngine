using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

public class WithStatementTests
{
    [Fact(Timeout = 2000)]
    public async Task With_UnscopablesGetterCalledOnceForIncrement()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            let toggled = true;
            const env = {
                x: 1,
                get [Symbol.unscopables]() {
                    toggled = !toggled;
                    return { x: toggled };
                }
            };

            with (env) {
                x++;
            }

            toggled === false && env.x === 2;
            """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task With_UnscopablesGetterSkippedWhenPropertyAbsent()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            let getterCount = 0;
            const env = {};
            Object.defineProperty(env, Symbol.unscopables, {
                get() {
                    getterCount++;
                    return { x: true };
                }
            });

            var x = 42;
            let value;

            with (env) {
                value = x;
            }

            getterCount === 0 && value === 42;
            """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task With_EvalUsesSingleUnscopablesLookup()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            let getterCount = 0;
            const env = {
                x: 3,
                get [Symbol.unscopables]() {
                    getterCount++;
                    return {};
                }
            };

            with (env) {
                eval("x += 4;");
            }

            [getterCount, env.x];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(1d, array.GetElement(0).ToObject());
        Assert.Equal(7d, array.GetElement(1).ToObject());
    }

    [Theory]
    [InlineData("undefined")]
    [InlineData("null")]
    public async Task With_NullishValueThrowsTypeError(string expression)
    {
        await using var engine = new JsEngine();
        var script = $@"
            let caught = false;
            try {{
                with ({expression}) {{
                    x = 1;
                }}
            }} catch (e) {{
                caught = e instanceof TypeError;
            }}
            caught;";
        var result = await engine.Evaluate(script);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task With_TypedArrayBindingUsesOriginalObject()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            const view = new Int32Array([1, 2, 3]);
            Object.defineProperty(view, "marker", { value: 42, configurable: true });
            let observed;
            with (view) {
                observed = marker;
            }
            observed === 42;
            """);

        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task With_VarInitializerUpdatesBindingObjectWhenPropertyExists()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            const env = { value: 1 };

            with (env) {
                var value = 2;
            }

            [env.value, typeof value, value];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(2d, array.GetElement(0).ToObject());
        Assert.Equal("undefined", array.GetElement(1).ToObject());
        Assert.Same(Symbol.Undefined, array.GetElement(2).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task With_VarInitializerFallsBackToFunctionScopeWhenPropertyMissing()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            with ({}) {
                var created = 7;
            }

            [typeof created, created];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal("number", array.GetElement(0).ToObject());
        Assert.Equal(7d, array.GetElement(1).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task With_EmptyBodyProducesUndefinedCompletion()
    {
        await using var engine = new JsEngine();
        var emptyResult = await engine.Evaluate("1; with ({}) { }");
        Assert.Same(Symbol.Undefined, emptyResult);

        var valueResult = await engine.Evaluate("2; with ({}) { 3; }");
        Assert.Equal(3d, valueResult);
    }

    [Fact(Timeout = 2000)]
    public async Task With_ProxyBindingTracksHasAndGet()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            const log = [];
            const target = { value: 1 };
            const proxy = new Proxy(target, {
                has(obj, key) {
                    log.push(`has:${String(key)}`);
                    return Reflect.has(obj, key);
                },
                get(obj, key, receiver) {
                    log.push(`get:${String(key)}`);
                    return Reflect.get(obj, key, receiver);
                }
            });

            let observed;
            with (proxy) {
                observed = value;
            }

            [observed, log];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(1d, array.GetElement(0).ToObject());

        var logArray = Assert.IsType<JsArray>(array.GetElement(1).ToObject());
        AssertLogContainsInOrder(logArray, "has:value", "get:value");
    }

    [Fact(Timeout = 2000)]
    public async Task With_ProxyBindingTracksHasGetSetForAssignments()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            const log = [];
            const target = { p: 0 };
            const proxy = new Proxy(target, {
                has(obj, key) {
                    log.push(`has:${String(key)}`);
                    return Reflect.has(obj, key);
                },
                get(obj, key, receiver) {
                    log.push(`get:${String(key)}`);
                    return Reflect.get(obj, key, receiver);
                },
                set(obj, key, value, receiver) {
                    log.push(`set:${String(key)}`);
                    return Reflect.set(obj, key, value, receiver);
                }
            });

            with (proxy) {
                p += 2;
            }

            [target.p, log];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(2d, array.GetElement(0).ToObject());

        var logArray = Assert.IsType<JsArray>(array.GetElement(1).ToObject());
        AssertLogContainsInOrder(logArray, "has:p", "get:p", "set:p");
    }

    private static void AssertLogContainsInOrder(JsArray logArray, params string[] expected)
    {
        var matches = 0;
        var length = (int)logArray.Length;
        for (var i = 0; i < length && matches < expected.Length; i++)
        {
            var entry = logArray.GetElement(i).ToObject()?.ToString();
            if (string.Equals(entry, expected[matches], StringComparison.Ordinal))
            {
                matches++;
            }
        }

        Assert.Equal(expected.Length, matches);
    }

    [Fact(Timeout = 2000)]
    public async Task With_PropertyShadowsGlobalVariable()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var myObj = {
                parseInt: 'obj_parseInt'
            };

            var st_parseInt;

            with(myObj) {
                st_parseInt = parseInt;
            }

            st_parseInt;
            """);

        Assert.Equal("obj_parseInt", result);
    }

    [Fact(Timeout = 2000)]
    public async Task With_Test262_S12_10_A1_7_T1_Debug()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate("""
            var myObj = {
                p1: 'a',
                p2: 'b',
                p3: 'c',
                p4: 'd',
                p5: 'e',
                p6: 'f',
                p7: 'g',
                p8: 'h',
                p9: 'i',
                p10: 'j',
                parseInt: 'obj_parseInt',
                NaN: 'obj_NaN',
                Infinity: 'obj_Infinity'
            };

            var st_parseInt;
            var st_NaN;
            var st_Infinity;

            with(myObj) {
                st_parseInt = parseInt;
                st_NaN = NaN;
                st_Infinity = Infinity;
            }

            var results = [];
            results.push(["st_parseInt", st_parseInt, st_parseInt === 'obj_parseInt']);
            results.push(["st_NaN", st_NaN, st_NaN === 'obj_NaN']);
            results.push(["st_Infinity", st_Infinity, st_Infinity === 'obj_Infinity']);
            results.push(["globalParseInt", typeof parseInt, typeof parseInt === 'function']);
            results.push(["globalNaN", typeof NaN, typeof NaN === 'number']);
            results.push(["check11", st_parseInt !== parseInt, "expected true"]);
            results;
            """);

        var arr = Assert.IsType<JsArray>(result);
        for (var i = 0; i < (int)arr.Length; i++)
        {
            var item = Assert.IsType<JsArray>(arr.GetElement(i).ToObject());
            var name = item.GetElement(0).ToObject()?.ToString();
            var value = item.GetElement(1).ToObject();
            var expected = item.GetElement(2).ToObject();
            Console.WriteLine($"{name}: {value}, expected: {expected}");
        }
    }
}
