using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class WithStatementTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task With_UnscopablesGetterCalledOnceForIncrement()
    {
        await using var engine = CreateEngine();
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
    public async Task With_StrictPostfixIncrementThrowsReferenceErrorWhenGetterDeletesBinding()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var count = 0;
            var caught = false;
            var scope = {
                get x() {
                    delete this.x;
                    return 2;
                }
            };

            with (scope) {
                (function() {
                    "use strict";
                    try {
                        count++;
                        x++;
                        count++;
                    } catch (e) {
                        caught = e instanceof ReferenceError;
                    }
                    count++;
                })();
            }

            ({ caught, count, hasX: "x" in scope });
            """);

        var obj = Assert.IsType<JsObject>(result);
        Assert.True(obj.TryGetProperty("caught", out var caught));
        Assert.True(caught.AsBoolean());
        Assert.True(obj.TryGetProperty("count", out var count));
        Assert.Equal(2d, count.AsDouble());
        Assert.True(obj.TryGetProperty("hasX", out var hasX));
        Assert.False(hasX.AsBoolean());
    }

    [Fact(Timeout = 2000)]
    public async Task With_SloppyPostfixIncrementCanRecreateBindingAfterGetterDeletesIt()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var scope = {
                get x() {
                    delete this.x;
                    return 2;
                }
            };

            var observed;
            with (scope) {
                observed = x++;
            }

            ({ observed, value: scope.x, hasX: "x" in scope });
            """);

        var obj = Assert.IsType<JsObject>(result);
        Assert.True(obj.TryGetProperty("observed", out var observed));
        Assert.Equal(2d, observed.AsDouble());
        Assert.True(obj.TryGetProperty("value", out var value));
        Assert.Equal(3d, value.AsDouble());
        Assert.True(obj.TryGetProperty("hasX", out var hasX));
        Assert.True(hasX.AsBoolean());
    }

    [Fact(Timeout = 2000)]
    public async Task With_UnscopablesGetterSkippedWhenPropertyAbsent()
    {
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
        var emptyResult = await engine.Evaluate("1; with ({}) { }");
        Assert.Same(Symbol.Undefined, emptyResult);

        var valueResult = await engine.Evaluate("2; with ({}) { 3; }");
        Assert.Equal(3d, valueResult);
    }

    [Fact(Timeout = 2000)]
    public async Task With_ProxyBindingTracksHasAndGet()
    {
        await using var engine = CreateEngine();
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
        await using var engine = CreateEngine();
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

    [Fact(Timeout = 2000)]
    public async Task With_BreakLeavesObjectEnvironmentBeforeOuterIdentifierRead()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            this.p1 = 1;
            var scope = { p1: "scope" };

            do {
                with (scope) {
                    p1 = "updated";
                    break;
                }
            } while (false);

            [p1, scope.p1];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(1d, array.GetElement(0).ToObject());
        Assert.Equal("updated", array.GetElement(1).ToObject());
    }

    [Fact(Timeout = 2000)]
    public async Task With_ProxySimpleAndCompoundAssignmentsRecheckBindingBeforeSet()
    {
        await using var engine = CreateEngine();
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
                },
                getOwnPropertyDescriptor(obj, key) {
                    log.push(`getOwnPropertyDescriptor:${String(key)}`);
                    return Reflect.getOwnPropertyDescriptor(obj, key);
                },
                defineProperty(obj, key, desc) {
                    log.push(`defineProperty:${String(key)}`);
                    return Reflect.defineProperty(obj, key, desc);
                }
            });

            with (proxy) {
                p = 1;
                p += 2;
            }

            [target.p, log];
            """);

        var array = Assert.IsType<JsArray>(result);
        Assert.Equal(3d, array.GetElement(0).ToObject());

        var logArray = Assert.IsType<JsArray>(array.GetElement(1).ToObject());
        AssertLogContainsInOrder(
            logArray,
            "has:p",
            "get:Symbol(Symbol.unscopables)",
            "has:p",
            "set:p",
            "has:p",
            "get:Symbol(Symbol.unscopables)",
            "has:p",
            "get:p",
            "has:p",
            "set:p");
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
        await using var engine = CreateEngine();
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
    public async Task With_FunctionLiteralCreatedInsideWithBlock_UsesIrPlanInsteadOfDynamicScopeExecutor()
    {
        var logger = new TestLogger(output, "WithLiteral", minLogLevel: LogLevel.Debug);
        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            const scope = {
                parseInt: function() { return 'obj_parseInt'; }
            };

            with (scope) {
                const read = function() {
                    return parseInt();
                };

                read();
            }
            """);

        Assert.Equal("obj_parseInt", result);
        Assert.DoesNotContain(
            logger.Collector.Snapshot(),
            static record => record.Message.Contains(
                "Executing sync function via dynamic-scope executor func=<anonymous>",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 2000)]
    public async Task With_FunctionDeclarationCreatedInsideWithBlock_UsesIrPlanInsteadOfDynamicScopeExecutor()
    {
        var logger = new TestLogger(output, "WithDeclaration", minLogLevel: LogLevel.Debug);
        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            const scope = { value: 41 };

            with (scope) {
                function read() {
                    return value + 1;
                }

                read();
            }
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(
            logger.Collector.Snapshot(),
            static record => record.Message.Contains(
                "Executing sync function via dynamic-scope executor func=read",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 2000)]
    public async Task While_BreakCompletionValue_EmptyBody()
    {
        // Test: eval('1; while (true) { break; }') should return undefined
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("eval('1; while (true) { break; }')");
        Assert.True(ReferenceEquals(result, Symbol.Undefined)); // undefined
    }

    [Fact(Timeout = 2000)]
    public async Task While_BreakCompletionValue_WithValue()
    {
        // Test: eval('2; while (true) { 3; break; }') should return 3
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("eval('2; while (true) { 3; break; }')");
        Assert.Equal(3.0, result);
    }
}
