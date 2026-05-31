using System.IO;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeProductionInvocationTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string UnifiedBytecodeProductionFastPathLog = "unified-bytecode-production-fast-path";
    private const string SimpleIrParameterNumberBinaryFastPathLog =
        "simple-ir-parameter-number-binary-fast-path";
    private const string SimpleIrParameterNumberBinaryChainFastPathLog =
        "simple-ir-parameter-number-binary-chain-fast-path";

    [Fact(Timeout = 5000)]
    public async Task LinearSlotReturnFunction_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function passThrough(x) {
                var y = x;
                return y;
            }

            passThrough(42);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=passThrough argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LocalLiteralReturnFunction_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readLocal() {
                var y = 7;
                return y;
            }

            readLocal();
            """);

        Assert.Equal(7d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readLocal argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleObjectDestructuring_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readAb(source) {
                var { a, b } = source;
                return a + b;
            }

            readAb({ a: 40, b: 2 });
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readAb argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ObjectDestructuring_PreservesPropertyReadOrder_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var order = [];
            function readAb(source) {
                var { a, b } = source;
                return a + b;
            }

            var src = {};
            Object.defineProperty(src, "a", {
                get: function () { order.push("a"); return 1; },
                enumerable: true
            });
            Object.defineProperty(src, "b", {
                get: function () { order.push("b"); return 2; },
                enumerable: true
            });

            var sum = readAb(src);
            [sum, order.join(",")];
            """);

        var steps = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal(3d, steps.Items[0].AsDouble());
        Assert.Equal("a,b", steps.Items[1].AsString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readAb argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ObjectDestructuringRest_CollectsRemainingProperties_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readRest(source) {
                var { a, ...rest } = source;
                return rest.c;
            }

            readRest({ a: 1, b: 2, c: 3 });
            """);

        Assert.Equal(3d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readRest argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ObjectDestructuringNullSource_ThrowsTypeError_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readAb(source) {
                var { a } = source;
                return a;
            }

            var captured = false;
            try {
                readAb(null);
            } catch (error) {
                captured = error instanceof TypeError;
            }

            captured;
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readAb argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ObjectDestructuringGetterThrow_PropagatesAndCleansUp_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readAb(source) {
                var { a, b } = source;
                return a + b;
            }

            var src = {
                a: 1,
                get b() { throw new RangeError("boom"); }
            };

            var message = null;
            try {
                readAb(src);
            } catch (error) {
                message = error instanceof RangeError ? error.message : "wrong";
            }

            message;
            """);

        Assert.Equal("boom", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readAb argc=1",
                StringComparison.Ordinal));
    }

    [Theory(Timeout = 5000)]
    [InlineData(
        """
        function readObjectDefault(source) {
            var { a = 5 } = source;
            return a;
        }

        readObjectDefault({});
        """,
        "readObjectDefault",
        5d)]
    [InlineData(
        """
        function readObjectComputed(source, key) {
            var { [key]: value } = source;
            return value;
        }

        readObjectComputed({ picked: 7 }, "picked");
        """,
        "readObjectComputed",
        7d)]
    public async Task UnsupportedObjectDestructuringShapes_DeclineUnifiedBytecodeAndFallBack(
        string source,
        string functionName,
        object expected)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(source);

        Assert.Equal(expected, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"unified-bytecode-production-fast-path func={functionName}",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleGeneratorYieldSend_UsesResumableUnifiedBytecodeFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* gen(input) {
                var x = yield input;
                return x + 1;
            }

            var iterator = gen(10);
            var first = iterator.next();
            var second = iterator.next(41);
            [first.value, first.done, second.value, second.done];
            """);

        var steps = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal(10d, steps.Items[0].AsDouble());
        Assert.False(steps.Items[1].AsBoolean());
        Assert.Equal(42d, steps.Items[2].AsDouble());
        Assert.True(steps.Items[3].AsBoolean());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-resumable-generator-fast-path func=gen argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleAsyncAwaitReturn_UsesResumableUnifiedBytecodeFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(input) {
                await input;
                return await 41;
            }

            run(1).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(41d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-resumable-async-fast-path func=run argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleGeneratorYieldStar_DeclinesResumableUnifiedBytecodeFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* relay(values) {
                yield* values;
            }

            var iterator = relay([3]);
            var first = iterator.next();
            var second = iterator.next();
            [first.value, first.done, second.value, second.done];
            """);

        var steps = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal(3d, steps.Items[0].AsDouble());
        Assert.False(steps.Items[1].AsBoolean());
        Assert.Equal(Symbol.Undefined, steps.Items[2]);
        Assert.True(steps.Items[3].AsBoolean());
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-resumable-generator-fast-path func=relay",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleGeneratorYieldStar_ReturnDelegatesThroughIrAfterResumableDecline()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* outer(delegated) {
                return yield* delegated;
            }

            var calls = [];
            var delegated = {
                [Symbol.iterator]() {
                    return {
                        next() {
                            calls.push("next");
                            return { value: "first", done: false };
                        },
                        return(value) {
                            calls.push("return:" + value);
                            return { value: "closed:" + value, done: true };
                        }
                    };
                }
            };

            var iterator = outer(delegated);
            var first = iterator.next();
            var second = iterator.return("stop");
            [first.value, first.done, second.value, second.done, calls.join(",")];
            """);

        var steps = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal("first", steps.Items[0].AsString());
        Assert.False(steps.Items[1].AsBoolean());
        Assert.Equal("closed:stop", steps.Items[2].AsString());
        Assert.True(steps.Items[3].AsBoolean());
        Assert.Equal("next,return:stop", steps.Items[4].AsString());
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-resumable-generator-fast-path func=outer",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleGeneratorYieldStar_ThrowDelegatesThroughIrAfterResumableDecline()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* outer(delegated) {
                return yield* delegated;
            }

            var calls = [];
            var delegated = {
                [Symbol.iterator]() {
                    return {
                        next() {
                            calls.push("next");
                            return { value: "first", done: false };
                        },
                        throw(value) {
                            calls.push("throw:" + value);
                            return { value: "handled:" + value, done: true };
                        }
                    };
                }
            };

            var iterator = outer(delegated);
            var first = iterator.next();
            var second = iterator.throw("boom");
            [first.value, first.done, second.value, second.done, calls.join(",")];
            """);

        var steps = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal("first", steps.Items[0].AsString());
        Assert.False(steps.Items[1].AsBoolean());
        Assert.Equal("handled:boom", steps.Items[2].AsString());
        Assert.True(steps.Items[3].AsBoolean());
        Assert.Equal("next,throw:boom", steps.Items[4].AsString());
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-resumable-generator-fast-path func=outer",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task MissingArgument_InitializesParameterSlotToUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function passThrough(x) {
                var y = x;
                return y;
            }

            passThrough();
            """);

        Assert.Equal(Symbol.Undefined, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=passThrough argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task WithDynamicIdentifierOperations_UseUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function run(scope) {
                with (scope) {
                    value = value + 2;
                    ++count;
                    missingType = typeof missing;
                    deleteResult = delete removable;
                    removableType = typeof removable;
                    return value + ":" +
                        count + ":" +
                        missingType + ":" +
                        deleteResult + ":" +
                        removableType;
                }
            }

            run({
                value: 1,
                count: 4,
                removable: 9,
                missingType: "",
                deleteResult: false,
                removableType: ""
            });
            """);

        Assert.Equal("3:5:undefined:true:undefined", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=run argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task WithFunctionVarInitializer_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function run(scope) {
                var add = 2;
                with (scope) {
                    return value + add;
                }
            }

            run({ value: 40 });
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=run argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task WithVarInitializer_PreResolvesBindingBeforeInitializerOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var scope = new Proxy(
                { value: 1, hide: 0 },
                {
                    has(target, key) {
                        return key === "value" ? target.hide === 0 : key in target;
                    }
                });

            function run(scope) {
                with (scope) {
                    var value = (++hide, 42);
                    return value;
                }
            }

            run(scope) + ":" + scope.value + ":" + scope.hide;
            """);

        Assert.Equal("undefined:42:1", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=run argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NestedFunctionVarDeclaration_IsHoistedAcrossOuterWithWhenThrowingOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function run(scope) {
                var result = "result";
                var f = function() {
                    throw value;
                    var value = "local";
                };

                try {
                    with (scope) {
                        f();
                    }
                } catch (error) {
                    result = error;
                }

                return result;
            }

            run({ value: "scope" });
            """);

        Assert.Equal(Symbol.Undefined, result);
    }

    [Fact(Timeout = 5000)]
    public async Task WithThenOutsideDynamicIdentifier_DeclinesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var externalValue = 41;

            function run(scope) {
                with (scope) {
                    value = value + 1;
                }

                return externalValue + 1;
            }

            run({ value: 1 });
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=run",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task WithDynamicIdentifierCallTarget_UsesWithReceiverOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function run(scope) {
                with (scope) {
                    return finish();
                }
            }

            run({
                marker: 17,
                finish: function() {
                    return this.marker;
                }
            });
            """);

        Assert.Equal(17d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=run argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task WithDynamicIdentifierLookup_RespectsProxyAndUnscopablesOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var log = [];
            var hidden = 40;

            function run(scope) {
                with (scope) {
                    return hidden + visible;
                }
            }

            var target = {
                hidden: 1,
                visible: 2
            };
            target[Symbol.unscopables] = { hidden: true };

            var proxy = new Proxy(target, {
                has: function(obj, prop) {
                    log.push("has:" + String(prop));
                    return prop in obj;
                },
                get: function(obj, prop, receiver) {
                    log.push("get:" + String(prop));
                    return Reflect.get(obj, prop, receiver);
                }
            });

            run(proxy) + ":" +
                (log.indexOf("has:hidden") >= 0) + ":" +
                (log.indexOf("has:visible") >= 0) + ":" +
                (log.indexOf("get:visible") >= 0);
            """);

        Assert.Equal("42:true:true:true", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=run argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task TryCatch_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function recover() {
                try {
                    throw 40;
                } catch (e) {
                    return e + 2;
                }
            }

            recover();
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=recover argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task TryCatch_CatchBindingDoesNotLeakAfterCatchOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function leak() {
                try {
                    throw 7;
                } catch (e) {
                }

                return typeof e;
            }

            leak();
            """);

        Assert.Equal("undefined", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=leak argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task TryCatch_CatchBindingDirectReadAfterCatchThrowsReferenceErrorOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe() {
                try {
                    throw 7;
                } catch (e) {
                }

                try {
                    return e;
                } catch (error) {
                    return error.name;
                }
            }

            probe();
            """);

        Assert.Equal("ReferenceError", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=probe argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task TryFinally_ReturnFromFinallyReplacesPriorReturnOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function replaceReturn() {
                try {
                    return 1;
                } finally {
                    return 2;
                }
            }

            replaceReturn();
            """);

        Assert.Equal(2d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=replaceReturn argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task TryFinally_NestedReturnThroughFinallyClearsOperandStackOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function replaceNestedReturn() {
                try {
                    return 3;
                } finally {
                    try {
                        return (10 + 20) + (30 + 40);
                    } finally {
                        (1 + 2) + (3 + 4);
                    }
                }
            }

            replaceNestedReturn();
            """);

        Assert.Equal(100d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=replaceNestedReturn argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task TryFinally_ThrowFromFinallyReplacesPriorThrowOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function replaceThrow() {
                try {
                    try {
                        throw 1;
                    } finally {
                        throw 2;
                    }
                } catch (e) {
                    return e;
                }
            }

            replaceThrow();
            """);

        Assert.Equal(2d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=replaceThrow argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task TryFinally_BreakThroughFinallyUsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function breakFinally(n) {
                var marker = 0;
                while (n > 0) {
                    try {
                        break;
                    } finally {
                        marker = marker + 10;
                    }
                }

                return marker;
            }

            breakFinally(2);
            """);

        Assert.Equal(10d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=breakFinally argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task TryFinally_ContinueThroughFinallyUsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function continueFinally(n) {
                var i = 0;
                var marker = 0;
                while (i < n) {
                    i = i + 1;
                    try {
                        continue;
                    } finally {
                        marker = marker + 10;
                    }
                }

                return marker + i;
            }

            continueFinally(2);
            """);

        Assert.Equal(22d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=continueFinally argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ForOfNestedInnerBreak_DoesNotCloseOuterIteratorBeforeReturnExpressionOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function makeIterable(closeValue, box) {
                return {
                    [Symbol.iterator]: function() {
                        var seen = false;
                        return {
                            next: function() {
                                if (seen) {
                                    return { done: true };
                                }

                                seen = true;
                                return { done: false, value: closeValue };
                            },
                            return: function() {
                                box.value = box.value + closeValue;
                                return {};
                            }
                        };
                    }
                };
            }

            function probe(outer, inner, box) {
                for (var outerValue of outer) {
                    for (var innerValue of inner) {
                        break;
                    }

                    return box.value;
                }

                return -1;
            }

            var box = { value: 0 };
            var result = probe(makeIterable(10, box), makeIterable(1, box), box);
            result + ":" + box.value;
            """);

        Assert.Equal("1:11", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=probe argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LabeledBlockBreak_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function labeledBlock(flag) {
                var total = 0;
                block: {
                    total = total + 1;
                    if (flag) {
                        break block;
                    }

                    total = total + 10;
                }

                return total;
            }

            labeledBlock(true);
            """);

        Assert.Equal(1d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=labeledBlock argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LabeledBreakInLoop_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function labeledBreak(n) {
                outer: while (n > 0) {
                    break outer;
                }

                return n;
            }

            labeledBreak(3);
            """);

        Assert.Equal(3d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=labeledBreak argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LabeledContinueInLoop_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function labeledContinue(n) {
                var total = 0;
                outer: while (n > 0) {
                    n = n - 1;
                    continue outer;
                    total = total + 1;
                }

                return total;
            }

            labeledContinue(3);
            """);

        // continue outer skips the post-continue increment on every iteration, so total stays 0.
        Assert.Equal(0d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=labeledContinue argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LabeledBreakOutOfForOf_ClosesDriverOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function makeIterable(closeValue, box) {
                return {
                    [Symbol.iterator]: function() {
                        var i = 0;
                        return {
                            next: function() {
                                i = i + 1;
                                return { done: i > 5, value: i };
                            },
                            return: function() {
                                box.value = box.value + closeValue;
                                return {};
                            }
                        };
                    }
                };
            }

            function probe(source, box) {
                outer: for (var value of source) {
                    break outer;
                }

                return box.value;
            }

            var box = { value: 0 };
            var result = probe(makeIterable(7, box), box);
            result + ":" + box.value;
            """);

        // Labeled break out of the for-of closes its iterator (box += 7) before probe returns.
        Assert.Equal("7:7", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=probe argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task DirectBranchReturnFunction_UsesUnifiedBytecodeProductionFastPathForTrueAndFalseOutcomes()
    {
        await using var engine = CreateEngine();
        var trueResult = await engine.Evaluate("""
            function pick(flag) {
                var branch = flag;
                if (branch) {
                    return 1;
                }

                return 2;
            }

            pick(true);
            """);

        var falseResult = await engine.Evaluate("""
            pick(false);
            """);

        Assert.Equal(1d, trueResult);
        Assert.Equal(2d, falseResult);
        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Contains(logRecords,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=pick",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BinaryReturnFunction_KeepsExistingSpecializedFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function add(a, b) {
                return a + b;
            }

            add(20, 22);
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(42d, result);
        Assert.DoesNotContain(logRecords,
            static record => record.Message.Contains(UnifiedBytecodeProductionFastPathLog, StringComparison.Ordinal));
        Assert.Contains(logRecords,
            static record => record.Message.Contains(SimpleIrParameterNumberBinaryFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BinaryChainReturnFunction_KeepsExistingSpecializedFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function addChain(a, b, c) {
                return a + b + c;
            }

            addChain(10, 20, 12);
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(42d, result);
        Assert.DoesNotContain(logRecords,
            static record => record.Message.Contains(UnifiedBytecodeProductionFastPathLog, StringComparison.Ordinal));
        Assert.Contains(logRecords,
            static record => record.Message.Contains(SimpleIrParameterNumberBinaryChainFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BinaryComparisonFunction_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function isLess(a, b) {
                return a < b;
            }

            isLess(20, 22);
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(true, result);
        Assert.Contains(logRecords,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=isLess argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task DirectIdentifierCall_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function helper(value) {
                return value + 1;
            }

            function invoke(helper, value) {
                return helper(value);
            }

            invoke(helper, 41);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ZeroArgumentIdentifierCall_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function helper() {
                return 42;
            }

            function invoke(helper) {
                return helper();
            }

            invoke(helper);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task TwoArgumentIdentifierCall_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function helper(left, right) {
                return left + right;
            }

            function invoke(helper, left, right) {
                return helper(left, right);
            }

            invoke(helper, 20, 22);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ParameterPassedDebugAwareIdentifierCall_PreservesCallerEnvironment()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(fn) {
                return fn();
            }

            invoke(__debug);
            """);

        Assert.Equal(Symbol.Undefined, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=1",
                StringComparison.Ordinal));

        var debugMessage = await engine.DebugMessages().ReadAsync();
        Assert.Contains("fn", debugMessage.Variables.Keys);
        Assert.Contains(debugMessage.EnvironmentChain,
            static environment => environment.HasSlots && environment.SlotCount > 0);
    }

    [Fact(Timeout = 5000)]
    public async Task BlockScopedDebugAwareIdentifierCall_PreservesActiveLexicalEnvironment()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(fn) {
                var result = 0;
                {
                    let x = 1;
                    result = x;
                    fn();
                }

                return result;
            }

            invoke(__debug);
            """);

        Assert.Equal(1d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=1",
                StringComparison.Ordinal));

        var debugMessage = await engine.DebugMessages().ReadAsync();
        Assert.Contains("fn", debugMessage.Variables.Keys);
        Assert.Contains("x", debugMessage.Variables.Keys);
        Assert.Equal(1d, debugMessage.Variables["x"]);
        Assert.Contains(debugMessage.EnvironmentChain,
            static environment => string.Equals(
                environment.Description,
                "unified-bytecode-scope",
                StringComparison.Ordinal) &&
                environment.HasSlots);
    }

    [Fact(Timeout = 5000)]
    public async Task NonCallableIdentifierCall_PropagatesTypeErrorThroughUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(fn) {
                return fn();
            }

            try {
                invoke(1);
                "missing";
            } catch (error) {
                error instanceof TypeError;
            }
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedMemberCall_UsesUnifiedBytecodeProductionFastPathAndPreservesThis()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = {
                read(value) {
                    return this === box ? value + 1 : -1;
                }
            };

            function invoke(box, value) {
                return box.read(value);
            }

            invoke(box, 41);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedMemberCall_UsesUnifiedBytecodeProductionFastPathAndPreservesThis()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = {
                offset: 1,
                read(value) {
                    return value + this.offset;
                }
            };

            function invoke(box, key, value) {
                return box[key](value);
            }

            invoke(box, { toString() { return "read"; } }, 41);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedMemberCall_NullishReceiverThrowsBeforeKeyCoercion()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(box, key) {
                return box[key]();
            }

            var key = {
                count: 0,
                toString() {
                    this.count++;
                    return "read";
                }
            };

            try {
                invoke(null, key);
                "missing";
            } catch (error) {
                [
                    error instanceof TypeError,
                    key.count
                ].join("|");
            }
            """);

        Assert.Equal("true|0", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NestedNamedMemberCall_BindsThisToFinalReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var root = {
                offset: 100,
                child: {
                    offset: 1,
                    read(value) {
                        return value + this.offset;
                    }
                }
            };

            function invoke(root, value) {
                return root.child.read(value);
            }

            invoke(root, 41);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task DeeperNamedMemberCall_BindsThisToDeepestReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var root = {
                offset: 1000,
                child: {
                    offset: 100,
                    branch: {
                        offset: 10,
                        leaf: {
                            offset: 1,
                            read(value) {
                                return this === root.child.branch.leaf ? value + this.offset : -1;
                            }
                        }
                    }
                }
            };

            function invoke(root, value) {
                return root.child.branch.leaf.read(value);
            }

            invoke(root, 41);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedMemberCall_PreservesKeyConversionSideEffectsAndThisBinding()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var log = "";
            var key = {
                toString() {
                    log = log + "k";
                    return "read";
                }
            };
            var box = {
                offset: 1,
                read(value) {
                    log = log + (this === box ? "t" : "x");
                    return value + this.offset;
                }
            };

            function invoke(box, key, value) {
                return box[key](value);
            }

            invoke(box, key, 41) + ":" + log;
            """);

        Assert.Equal("42:kt", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedMemberCall_NullishReceiverPreservesFallbackTypeErrorShape()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var converted = 0;
            var key = {
                toString() {
                    converted = 1;
                    return "read";
                }
            };

            function invoke(box, key) {
                return box[key]();
            }

            try {
                invoke(null, key);
                "missing";
            } catch (error) {
                (error instanceof TypeError) + ":" + converted;
            }
            """);

        Assert.Equal("true:0", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedMemberCall_NonCallableCalleePreservesFallbackTypeErrorShape()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = { read: 1 };

            function invoke(box, key) {
                return box[key]();
            }

            try {
                invoke(box, "read");
                "missing";
            } catch (error) {
                error instanceof TypeError;
            }
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ReceiverOptionalNamedMemberCall_ShortCircuitsToUndefinedWhenReceiverNullish()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(box) {
                return box?.read();
            }

            invoke(null) + "," + invoke(undefined);
            """);

        Assert.Equal("undefined,undefined", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ReceiverOptionalNamedMemberCall_InvokesMethodAndPreservesThisWhenReceiverNonNull()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = {
                read(value) {
                    return this === box ? value + 1 : -1;
                }
            };

            function invoke(box, value) {
                return box?.read(value);
            }

            invoke(box, 41);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task CalleeOptionalNamedMemberCall_ShortCircuitsToUndefinedWhenMethodNullish()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = { read: null };

            function invoke(box) {
                return box.read?.();
            }

            invoke(box) + "," + invoke({ read: undefined });
            """);

        Assert.Equal("undefined,undefined", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task CalleeOptionalNamedMemberCall_InvokesMethodAndPreservesThisWhenCalleeNonNull()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = {
                offset: 1,
                read(value) {
                    return this === box ? value + this.offset : -1;
                }
            };

            function invoke(box, value) {
                return box.read?.(value);
            }

            invoke(box, 41);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task CalleeOptionalComputedMemberCall_ShortCircuitsToUndefinedWhenMethodNullish()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = { read: null };

            function invoke(box, key) {
                return box[key]?.();
            }

            invoke(box, "read") + "," + invoke({ read: undefined }, "read");
            """);

        Assert.Equal("undefined,undefined", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task CalleeOptionalComputedMemberCall_InvokesMethodAndPreservesThisWhenCalleeNonNull()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = {
                offset: 1,
                read(value) {
                    return this === box ? value + this.offset : -1;
                }
            };

            function invoke(box, key, value) {
                return box[key]?.(value);
            }

            invoke(box, "read", 41);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task OptionalNamedPropertyRead_ReturnsUndefinedWhenBaseIsNull()
    {
        // gh2771: a?.b short-circuits to undefined when base is null/undefined.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readOptional(box) {
                return box?.value;
            }

            readOptional(null);
            """);

        Assert.Equal("undefined", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readOptional",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task OptionalNamedPropertyRead_ReturnsPropertyValueWhenBaseIsNonNull()
    {
        // gh2771: a?.b returns the property value when base is not null/undefined.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readOptional(box) {
                return box?.value;
            }

            readOptional({ value: 42 });
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readOptional",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task OptionalComputedPropertyRead_ReturnsUndefinedWhenBaseIsNull()
    {
        // gh2771: a?.[k] short-circuits to undefined when base is null/undefined.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readOptionalComputed(box, key) {
                return box?.[key];
            }

            readOptionalComputed(null, "value");
            """);

        Assert.Equal("undefined", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readOptionalComputed",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task OptionalComputedPropertyRead_ReturnsPropertyValueWhenBaseIsNonNull()
    {
        // gh2771: a?.[k] returns the property value when base is not null/undefined.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readOptionalComputed(box, key) {
                return box?.[key];
            }

            readOptionalComputed({ value: 42 }, "value");
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readOptionalComputed",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LooseEqualityBranchFunction_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var truthyResult = await engine.Evaluate("""
            function chooseByLooseEquality(value) {
                if (value == 0) {
                    return 10;
                }

                return 20;
            }

            chooseByLooseEquality("0");
            """);

        var falseResult = await engine.Evaluate("""
            chooseByLooseEquality(1);
            """);

        Assert.Equal(10d, truthyResult);
        Assert.Equal(20d, falseResult);
        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Contains(logRecords,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=chooseByLooseEquality argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ThisPropertyRead_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readThis() {
                return this.value;
            }

            readThis.call({ value: 7 });
            """);

        Assert.Equal(7d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readThis argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NewTargetInOrdinaryCall_UsesUnifiedBytecodeProductionFastPathAndReturnsUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readNewTarget() {
                return new.target;
            }

            readNewTarget();
            """);

        Assert.Equal(Symbol.Undefined, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readNewTarget argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task StrictEqualityBranchFunction_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function chooseByStrictEquality(value) {
                if (value === 0) {
                    return 10;
                }

                return 20;
            }

            chooseByStrictEquality("0");
            """);

        Assert.Equal(20d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=chooseByStrictEquality argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task PrimitiveUnaryTypeofAndTemplateStringOperators_UseUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function primitiveLane(value) {
                var text = `${value}`;
                return typeof value + ":" + (+value) + ":" + (-value) + ":" + (!value) + ":" + (~value) + ":" + (void value) + ":" + text;
            }

            primitiveLane("5");
            """);

        Assert.Equal("string:5:-5:false:-6:undefined:5", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=primitiveLane argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task TypeOfNonIdentifier_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function kind(value) {
                return typeof (value + 1);
            }

            kind(41);
            """);

        Assert.Equal("number", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=kind argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task TypeOfIdentifierForLexicalTdz_PropagatesReferenceErrorThroughUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function kind() {
                return typeof x;
                let x = 1;
            }

            try {
                kind();
                "missing";
            } catch (e) {
                e.name + ":" + e.message;
            }
            """);

        Assert.Equal("ReferenceError:Cannot access 'x' before initialization", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=kind argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LoadSlotForLexicalTdz_PropagatesReferenceErrorThroughUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function read() {
                return x;
                let x = 1;
            }

            try {
                read();
                "missing";
            } catch (e) {
                e.name + ":" + e.message;
            }
            """);

        Assert.Equal("ReferenceError:Cannot access 'x' before initialization", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=read argc=0",
                StringComparison.Ordinal));
    }

    // ── Slice A (#2678): sync iterator / for-in TDZ head environments ──────────────
    // for (const/let x in/of …) owns a per-iteration lexical head. These prove the
    // loop both routes through the production fast path and produces correct results,
    // including head-environment TDZ enforcement during source evaluation.

    [Fact(Timeout = 5000)]
    public async Task ForInConstHead_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function collect(obj) {
                var keys = "";
                for (const key in obj) {
                    keys = keys + key;
                }

                return keys;
            }

            collect({ a: 1, b: 2, c: 3 });
            """);

        Assert.Equal("abc", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=collect argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ForOfConstHead_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sum(values) {
                var total = 0;
                for (const value of values) {
                    total = total + value;
                }

                return total;
            }

            sum([10, 20, 12]);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=sum argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ForOfLetHead_ReassignsPerIteration_OnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function doubled(values) {
                var total = 0;
                for (let value of values) {
                    value = value * 2;
                    total = total + value;
                }

                return total;
            }

            doubled([1, 2, 3]);
            """);

        Assert.Equal(12d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=doubled argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ForOfConstTdzHead_ThrowsReferenceError_OnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function read() {
                for (const x of [x]) {
                    return x;
                }
            }

            try {
                read();
                "missing";
            } catch (e) {
                e.name + ":" + e.message;
            }
            """);

        Assert.Equal("ReferenceError:Cannot access 'x' before initialization", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=read argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ForInConstTdzHead_ThrowsReferenceError_OnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function read() {
                for (const k in k) {
                    return k;
                }
            }

            try {
                read();
                "missing";
            } catch (e) {
                e.name + ":" + e.message;
            }
            """);

        Assert.Equal("ReferenceError:Cannot access 'k' before initialization", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=read argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ForOfLetHead_CapturedPerIterationBinding_DoesNotUseProductionFastPath()
    {
        // A closure capturing the per-iteration binding makes per-iteration freshness observable.
        // The production path declines captured activations wholesale, so this must run on the legacy
        // path (no fast-path log) while still producing the correct per-iteration values [1,2,3].
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function captured(values) {
                var fns = [];
                for (let value of values) {
                    fns.push(function () { return value; });
                }

                var sum = 0;
                for (var i = 0; i < fns.length; i = i + 1) {
                    sum = sum + fns[i]();
                }

                return sum;
            }

            captured([1, 2, 3]);
            """);

        Assert.Equal(6d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=captured",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BlockLexicalScope_UsesUnifiedBytecodeProductionFastPathAndPreservesShadowing()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function scoped(value) {
                var result = value;
                {
                    let value = 5;
                    const next = value + 1;
                    result = next;
                }

                return result + value;
            }

            scoped(10);
            """);

        Assert.Equal(16d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=scoped argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task IntegratedCompletedLaneProgram_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function integrated(box, n, key, seed) {
                var total = seed;
                var values = [1, , n];
                var record = { first: 1, [key]: n };
                {
                    let local = record[key];
                    const currentRaw = box.value;
                    const current = +currentRaw;
                    total = total + local + current;
                }

                while (n > 0) {
                    total = total + n;
                    n = n - 1;
                }

                box.value = total;
                var count = ++box.count;
                var stored = box.value;
                return stored + count + 1 + 3;
            }

            integrated({ value: 5, count: 0 }, 3, "dynamic", 10);
            """);

        Assert.Equal(29d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=integrated argc=4",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BlockLexicalScope_ReadBeforeDeclarationPreservesTdz()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function scoped() {
                {
                    var result = value;
                    let value = 1;
                }

                return 0;
            }

            try {
                scoped();
                "missing";
            } catch (e) {
                e.name + ":" + e.message;
            }
            """);

        Assert.Equal("ReferenceError:Cannot access 'value' before initialization", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=scoped argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task UnaryCoercionAbruptCompletion_PropagatesThroughUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function plus(value) {
                return +value;
            }

            try {
                plus({
                    valueOf() {
                        throw new Error("boom");
                    }
                });
                "missing";
            } catch (e) {
                e.message;
            }
            """);

        Assert.Equal("boom", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=plus argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task DiscardedPropertyRead_UsesUnifiedBytecodeProductionFastPathAndKeepsSideEffects()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var hits = 0;
            var box = {};
            Object.defineProperty(box, "value", {
                get() {
                    hits = hits + 1;
                    return 41;
                }
            });

            function discardRead(box) {
                box.value;
                return 1;
            }

            discardRead(box) + hits;
            """);

        Assert.Equal(2d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=discardRead argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task EmptyReturns_UseUnifiedBytecodeProductionFastPathAndReturnUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function explicitEmpty() {
                return;
            }

            function implicitEmpty(value) {
                var local = value;
            }

            explicitEmpty() === undefined && implicitEmpty(1) === undefined;
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(true, result);
        Assert.Contains(logRecords,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=explicitEmpty argc=0",
                StringComparison.Ordinal));
        Assert.Contains(logRecords,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=implicitEmpty argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ThrowStatement_UsesUnifiedBytecodeProductionFastPathAndIsCatchable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function fail(value) {
                throw value;
            }

            var caught = 0;
            try {
                fail(42);
            } catch (error) {
                caught = error;
            }

            caught;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=fail argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task DiscardedPropertyWrite_UsesUnifiedBytecodeProductionFastPathAndKeepsSideEffects()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function writeDiscarded(box, value) {
                box.value = value;
                return box.value;
            }

            writeDiscarded({ value: 1 }, 42);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=writeDiscarded argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task DiscardedPropertyUpdate_UsesUnifiedBytecodeProductionFastPathAndKeepsSideEffects()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function updateDiscarded(box) {
                box.value++;
                return box.value;
            }

            updateDiscarded({ value: 1 });
            """);

        Assert.Equal(2d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=updateDiscarded argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task DirectivePrologue_UsesUnifiedBytecodeProductionFastPathAndKeepsStrictness()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = {};
            Object.defineProperty(box, "value", {
                value: 1,
                writable: false
            });

            function strictWriteDiscarded(box, value) {
                "use strict";
                box.value = value;
                return "not reached";
            }

            var strictThrew = false;
            try {
                strictWriteDiscarded(box, 42);
            } catch (error) {
                strictThrew = error instanceof TypeError;
            }

            strictThrew && box.value === 1;
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=strictWriteDiscarded argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BranchBothArms_UseUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function pick(flag) {
                if (flag) {
                    return 1;
                }

                return 2;
            }

            pick(true) * 10 + pick(false);
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(12d, result);
        Assert.Equal(2, logRecords.Count(record =>
            record.Message.Contains("unified-bytecode-production-fast-path func=pick argc=1", StringComparison.Ordinal)));
    }

    [Fact(Timeout = 5000)]
    public async Task BranchJoinedLocalUpdates_UseUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function choose(pick) {
                var value = 1;
                if (pick) {
                    value = 2;
                } else {
                    value = 3;
                }

                return value;
            }

            choose(true) * 10 + choose(false);
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(23d, result);
        Assert.Equal(2, logRecords.Count(record =>
            record.Message.Contains("unified-bytecode-production-fast-path func=choose argc=1", StringComparison.Ordinal)));
    }

    [Theory(Timeout = 5000)]
    [InlineData(0, 0)]
    [InlineData(4, 10)]
    public async Task CanonicalWhileLoop_UsesUnifiedBytecodeProductionFastPath(int input, int expected)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($$"""
            function sumTo(n) {
                var total = 0;
                while (n > 0) {
                    total = total + n;
                    n = n - 1;
                }

                return total;
            }

            sumTo({{input}});
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal((double)expected, result);
        Assert.Contains(logRecords,
            record => record.Message.Contains("unified-bytecode-production-fast-path func=sumTo argc=1", StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task StringConcatenationBinary_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function concatWithSuffix(value) {
                return value + "!";
            }

            concatWithSuffix("ok");
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal("ok!", result?.ToString());
        Assert.Contains(logRecords,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=concatWithSuffix argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPropertyRead_UsesUnifiedBytecodeProductionFastPathAndGetterSemantics()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var hits = 0;
            var box = {};
            Object.defineProperty(box, "value", {
                get() {
                    hits = hits + 1;
                    return 41;
                }
            });

            function read(box) {
                return box.value;
            }

            read(box) + hits;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=read argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task TryCatch_GetterThrow_IsCaughtOnProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function recover(box) {
                try {
                    return box.value;
                } catch (error) {
                    return "caught:" + error;
                }
            }

            recover({
                get value() {
                    throw "boom";
                }
            });
            """);

        Assert.Equal("caught:boom", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=recover argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPropertyRead_UsesUnifiedBytecodeProductionFastPathAndPrimitiveBoxing()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function read(value) {
                return value.length;
            }

            read("abcd");
            """);

        Assert.Equal(4d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=read argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPropertyRead_UsesUnifiedBytecodeProductionFastPathAndProxyGetSemantics()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var hits = 0;
            var proxy = new Proxy({ value: 40 }, {
                get(target, prop) {
                    hits = hits + 1;
                    return target[prop] + 1;
                }
            });

            function read(box) {
                return box.value;
            }

            read(proxy) + hits;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=read argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ArrayLiteralConstruction_UsesUnifiedBytecodeProductionFastPathAndPreservesHoles()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function create(value) {
                return [1, , [value]];
            }

            var array = create(7);
            array.length === 3 && array[0] === 1 && !(1 in array) && array[2][0] === 7;
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=create argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ObjectLiteralConstruction_UsesUnifiedBytecodeProductionFastPathAndPreservesDataSemantics()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function create(key, proto) {
                return { __proto__: proto, a: 1, a: 2, [key]: 3 };
            }

            var object = create("b", { inherited: 9 });
            object.a + object.b + object.inherited;
            """);

        Assert.Equal(14d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=create argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ObjectLiteralConstruction_UsesUnifiedBytecodeProductionFastPathAndCoercesComputedKeys()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var hits = 0;
            var key = {
                toString() {
                    hits = hits + 1;
                    return "value";
                }
            };

            function create(key) {
                return { [key]: 41 };
            }

            create(key).value + hits;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=create argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleSourceArraySpread_UsesUnifiedBytecodeProductionFastPathAndSpreadsCorrectly()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function spreadArray(source) {
                return [1, ...source];
            }

            spreadArray([41])[1];
            """);

        Assert.Equal(41d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=spreadArray",
                StringComparison.Ordinal));
    }

    [Theory(Timeout = 5000)]
    [InlineData(
        """
        function methodObject() {
            return { value() { return 42; } };
        }

        methodObject().value();
        """,
        "methodObject",
        42d)]
    public async Task ExcludedLiteralConstructionShapes_DeclineUnifiedBytecodeAndFallBack(
        string source,
        string functionName,
        object expected)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(source);

        Assert.Equal(expected, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"unified-bytecode-production-fast-path func={functionName}",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedPropertyRead_UsesUnifiedBytecodeProductionFastPathAndToPropertyKeySemantics()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var hits = 0;
            var box = { value: 40 };
            var key = {
                toString() {
                    hits = hits + 1;
                    return "value";
                }
            };

            function read(box, key) {
                return box[key];
            }

            read(box, key) + hits;
            """);

        Assert.Equal(41d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=read argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPropertyRead_PropagatesGetterAbruptCompletionThroughUnifiedBytecode()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = {};
            Object.defineProperty(box, "value", {
                get() {
                    throw new Error("boom");
                }
            });

            function read(box) {
                return box.value;
            }

            try {
                read(box);
                "missing";
            } catch (e) {
                e.message;
            }
            """);

        Assert.Equal("boom", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=read argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task TwoHopNamedPropertyRead_UsesUnifiedBytecodeProductionFastPathAndGetterSemantics()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var hits = 0;
            var box = {};
            Object.defineProperty(box, "child", {
                get() {
                    hits = hits + 1;
                    return {
                        get value() {
                            hits = hits + 1;
                            return 40;
                        }
                    };
                }
            });

            function read(box) {
                return box.child.value;
            }

            read(box) + hits;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=read argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task DeeperNamedPropertyRead_UsesUnifiedBytecodeProductionFastPathAndGetterSemantics()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var hits = 0;
            var box = {};
            Object.defineProperty(box, "child", {
                get() {
                    hits = hits + 1;
                    return {
                        get branch() {
                            hits = hits + 1;
                            return {
                                get leaf() {
                                    hits = hits + 1;
                                    return {
                                        get value() {
                                            hits = hits + 1;
                                            return 38;
                                        }
                                    };
                                }
                            };
                        }
                    };
                }
            });

            function read(box) {
                return box.child.branch.leaf.value;
            }

            read(box) + hits;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=read argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedPropertyInNamedChain_DeclinesUnifiedBytecodeAndFallsBack()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function read(box, key) {
                return box.child[key];
            }

            read({ child: { value: 42 } }, "value");
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=read argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPropertyReadInsideBranch_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readWhen(flag, box) {
                if (flag) {
                    return box.value;
                }

                return 0;
            }

            readWhen(true, { value: 42 });
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readWhen argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPropertyReadWithCanonicalLoopShape_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function readAfterLoop(box, count) {
                while (count > 0) {
                    count = count - 1;
                }

                return box.value;
            }

            readAfterLoop({ value: 42 }, 2);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readAfterLoop argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPropertyWrite_UsesUnifiedBytecodeProductionFastPathAndSetterSemantics()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var hits = 0;
            var box = {};
            Object.defineProperty(box, "value", {
                set(value) {
                    hits = hits + value;
                }
            });

            function write(box, value) {
                return box.value = value;
            }

            write(box, 42) + hits;
            """);

        Assert.Equal(84d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=write argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedPropertyWrite_UsesUnifiedBytecodeProductionFastPathAndKeySemantics()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var hits = 0;
            var box = {};
            var key = {
                toString() {
                    hits = hits + 1;
                    return "value";
                }
            };

            function write(box, key, value) {
                return box[key] = value;
            }

            write(box, key, 41) + box.value + hits;
            """);

        Assert.Equal(83d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=write argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPropertyWrite_UsesUnifiedBytecodeProductionFastPathAndProxyReceiverIdentity()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var receiverMatches = false;
            var observed = 0;
            var proxy = new Proxy({}, {
                set(target, key, value, receiver) {
                    receiverMatches = receiver === proxy;
                    observed = value;
                    return true;
                }
            });

            function write(box, value) {
                return box.value = value;
            }

            (write(proxy, 42) === 42) && receiverMatches && (observed === 42);
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=write argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPropertyWrite_UsesUnifiedBytecodeProductionFastPathAndStrictSloppyFailureSemantics()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = {};
            Object.defineProperty(box, "value", {
                value: 1,
                writable: false
            });

            function sloppyWrite(box, value) {
                return box.value = value;
            }

            function strictWrite(box, value) {
                "use strict";
                return box.value = value;
            }

            var sloppyResult = sloppyWrite(box, 42);
            var sloppyStored = box.value;
            var strictThrew = false;
            try {
                strictWrite(box, 43);
            } catch (error) {
                strictThrew = error instanceof TypeError;
            }

            (sloppyResult === 42) && (sloppyStored === 1) && strictThrew && (box.value === 1);
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(true, result);
        Assert.Contains(logRecords,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=sloppyWrite argc=2",
                StringComparison.Ordinal));
        Assert.Contains(logRecords,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=strictWrite argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedPropertyWrite_UsesUnifiedBytecodeProductionFastPathAndEvaluationOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var events = [];
            var box = new Proxy({}, {
                set(target, key, value, receiver) {
                    events.push("set:" + String(key) + ":" + value);
                    target[key] = value;
                    return true;
                }
            });
            var key = {
                toString() {
                    events.push("key");
                    return "value";
                }
            };

            function rhs() {
                events.push("rhs");
                return 9;
            }

            function write(box, key, value) {
                return box[key] = value;
            }

            String(write(box, key, rhs())) + ":" + events.join(",");
            """);

        Assert.Equal("9:rhs,key,set:value:9", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=write argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedCompoundPropertyWrite_UsesUnifiedBytecodeProductionFastPathAndGetterSetterSemantics()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var events = [];
            var box = {};
            Object.defineProperty(box, "value", {
                get() {
                    events.push("get");
                    return 37;
                },
                set(value) {
                    events.push("set:" + value);
                }
            });

            function write(box, value) {
                return box.value += value;
            }

            String(write(box, 5)) + ":" + events.join(",");
            """);

        Assert.Equal("42:get,set:42", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=write argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedCompoundPropertyWrite_UsesUnifiedBytecodeProductionFastPathAndResolvesKeyOnce()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var events = [];
            var box = {};
            Object.defineProperty(box, "value", {
                get() {
                    events.push("get");
                    return 4;
                },
                set(value) {
                    events.push("set:" + value);
                }
            });
            var key = {
                toString() {
                    events.push("key");
                    return "value";
                }
            };

            function write(box, key, value) {
                return box[key] += value;
            }

            String(write(box, key, 5)) + ":" + events.join(",");
            """);

        Assert.Equal("9:key,get,set:9", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=write argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedCompoundPropertyWrite_UsesUnifiedBytecodeProductionFastPathAndStrictSloppyFailureSemantics()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = {};
            Object.defineProperty(box, "value", {
                value: 1,
                writable: false
            });

            function sloppyWrite(box, value) {
                return box.value += value;
            }

            function strictWrite(box, value) {
                "use strict";
                return box.value += value;
            }

            var sloppyResult = sloppyWrite(box, 41);
            var sloppyStored = box.value;
            var strictThrew = false;
            try {
                strictWrite(box, 42);
            } catch (error) {
                strictThrew = error instanceof TypeError;
            }

            (sloppyResult === 42) && (sloppyStored === 1) && strictThrew && (box.value === 1);
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(true, result);
        Assert.Contains(logRecords,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=sloppyWrite argc=2",
                StringComparison.Ordinal));
        Assert.Contains(logRecords,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=strictWrite argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPropertyUpdate_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function update(box) {
                return ++box.value;
            }

            var box = { value: 41 };
            update(box) + box.value;
            """);

        Assert.Equal(84d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=update argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPostfixPropertyUpdate_UsesUnifiedBytecodeProductionFastPathAndReturnsOldValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function update(box) {
                return box.value++;
            }

            var box = { value: 41 };
            update(box) + box.value;
            """);

        Assert.Equal(83d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=update argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedPrefixPropertyUpdate_UsesUnifiedBytecodeProductionFastPathAndReturnsNewValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function update(box, key) {
                return ++box[key];
            }

            var box = { value: 41 };
            update(box, "value") + box.value;
            """);

        Assert.Equal(84d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=update argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedPropertyUpdate_UsesUnifiedBytecodeProductionFastPathAndResolvesKeyOnce()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var hits = 0;
            var key = {
                toString() {
                    hits = hits + 1;
                    return "value";
                }
            };

            function update(box, key) {
                return box[key]++;
            }

            var box = { value: 40 };
            update(box, key) + box.value + hits;
            """);

        Assert.Equal(82d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=update argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPrefixPropertyDecrement_UsesUnifiedBytecodeProductionFastPathAndReturnsNewValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function update(box) {
                return --box.value;
            }

            var box = { value: 41 };
            update(box) + box.value;
            """);

        Assert.Equal(80d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=update argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NamedPostfixPropertyDecrement_UsesUnifiedBytecodeProductionFastPathAndReturnsOldValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function update(box) {
                return box.value--;
            }

            var box = { value: 41 };
            update(box) + box.value;
            """);

        Assert.Equal(81d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=update argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedPrefixPropertyDecrement_UsesUnifiedBytecodeProductionFastPathAndReturnsNewValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function update(box, key) {
                return --box[key];
            }

            var box = { value: 41 };
            update(box, "value") + box.value;
            """);

        Assert.Equal(80d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=update argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ComputedPropertyDecrement_UsesUnifiedBytecodeProductionFastPathAndResolvesKeyOnce()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var hits = 0;
            var key = {
                toString() {
                    hits = hits + 1;
                    return "value";
                }
            };

            function update(box, key) {
                return box[key]--;
            }

            var box = { value: 41 };
            update(box, key) + box.value + hits;
            """);

        Assert.Equal(82d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=update argc=2",
                StringComparison.Ordinal));
    }

    [Theory(Timeout = 5000)]
    [InlineData(
        """
        function readAt(values) {
            return values[1];
        }

        readAt([10, 32]);
        """,
        "readAt",
        32d)]
    [InlineData(
        """
        function charAt(value) {
            return value[1];
        }

        charAt("xyz");
        """,
        "charAt",
        "y")]
    public async Task IndexedReads_UseUnifiedBytecodeProductionFastPathWhenAdmitted(
        string source,
        string functionName,
        object expected)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(source);

        Assert.Equal(expected, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"unified-bytecode-production-fast-path func={functionName} argc=1",
                StringComparison.Ordinal));
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(UnsupportedControlFlowFunctions))]
    public async Task UnsupportedControlFlowShapes_DeclineUnifiedBytecodeAndFallBack(
        string source,
        string invocation,
        double expected,
        string functionName)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($$"""
            {{source}}

            {{invocation}};
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(expected, result);
        Assert.DoesNotContain(logRecords,
            record => record.Message.Contains(
                $"unified-bytecode-production-fast-path func={functionName}",
                StringComparison.Ordinal));
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(SupportedLoopControlFunctions))]
    public async Task SupportedLoopControlShapes_UseUnifiedBytecodeProductionFastPath(
        string source,
        string invocation,
        double expected,
        string functionName,
        int argumentCount)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate($$"""
            {{source}}

            {{invocation}};
            """);

        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(expected, result);
        Assert.Contains(logRecords,
            record => record.Message.Contains(
                $"unified-bytecode-production-fast-path func={functionName} argc={argumentCount}",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task IdentifierCallStoredInLocal_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function invoke(fn, x) {
                var y = fn(x);
                return y;
            }

            function id(x) {
                return x;
            }

            invoke(id, 42);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke argc=2",
                StringComparison.Ordinal));
    }

    [Theory(Timeout = 5000)]
    [InlineData(
        """
        function deleteMember(box) {
            delete box.value;
            return "value" in box ? 1 : 0;
        }

        deleteMember({ value: 1 });
        """,
        "deleteMember",
        0d)]
    [InlineData(
        """
        class Base {
            get value() { return 2; }
        }

        class Derived extends Base {
            readViaSuperBoundary() { return super.value; }
        }

        function readSuper() {
            return new Derived().readViaSuperBoundary();
        }

        readSuper();
        """,
        "readViaSuperBoundary",
        2d)]
    [InlineData(
        """
        var externalKey = "value";
        function readDynamic(box) {
            return box[externalKey];
        }

        readDynamic({ value: 2 });
        """,
        "readDynamic",
        2d)]
    [InlineData(
        """
        function logicalWrite(box, value) {
            return box.value ||= value;
        }

        logicalWrite({ value: 0 }, 42);
        """,
        "logicalWrite",
        42d)]
    [InlineData(
        """
        function logicalAndWrite(box, value) {
            return box.value &&= value;
        }

        logicalAndWrite({ value: 1 }, 42);
        """,
        "logicalAndWrite",
        42d)]
    [InlineData(
        """
        function logicalNullishWrite(box, value) {
            return box.value ??= value;
        }

        logicalNullishWrite({ value: null }, 42);
        """,
        "logicalNullishWrite",
        42d)]
    [InlineData(
        """
        var externalValue = 42;
        function dynamicValueWrite(box) {
            return box.value = externalValue;
        }

        dynamicValueWrite({});
        """,
        "dynamicValueWrite",
        42d)]
    [InlineData(
        """
        function computedExpressionWrite(box, key, suffix, value) {
            return box[key + suffix] = value;
        }

        computedExpressionWrite({}, "val", "ue", 42);
        """,
        "computedExpressionWrite",
        42d)]
    [InlineData(
        """
        function complexCompoundWrite(box, value) {
            return box.child.value += value;
        }

        complexCompoundWrite({ child: { value: 40 } }, 2);
        """,
        "complexCompoundWrite",
        42d)]
    [InlineData(
        """
        function destructureWrite(box, source) {
            ({ value: box.value } = source);
            return box.value;
        }

        destructureWrite({ value: 0 }, { value: 42 });
        """,
        "destructureWrite",
        42d)]
    public async Task UnsupportedPropertyReadAdjacentFamilies_DeclineUnifiedBytecodeAndFallBack(
        string source,
        string functionName,
        object expected)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(source);

        Assert.Equal(expected, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"unified-bytecode-production-fast-path func={functionName}",
                StringComparison.Ordinal));
    }

    [Fact]
    public void SourceGate_OrdinarySyncRouteAttemptsProductionUnifiedBytecodeBeforeGenericIr()
    {
        var repositoryRoot = FindRepositoryRootForSourceGate();
        var invokerPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Ast",
            "TypedAstEvaluator.SyncFunctionInvoker.cs");

        var invokerSource = File.ReadAllText(invokerPath);
        var routeStart = invokerSource.IndexOf(
            "private bool TryInvokeIrFast<TArgs>(",
            StringComparison.Ordinal);
        Assert.True(routeStart >= 0, "Could not locate TryInvokeIrFast route method.");
        var routeEnd = invokerSource.IndexOf(
            "private bool TryInvokeSimpleDerivedClassConstructorFastPath<TArgs>(",
            routeStart,
            StringComparison.Ordinal);
        Assert.True(routeEnd > routeStart, "Could not locate end boundary for TryInvokeIrFast.");
        var routeSource = invokerSource.Substring(routeStart, routeEnd - routeStart);

        var binaryFastPathIndex = routeSource.IndexOf(
            "plan.SimpleReturnParameterBinary",
            StringComparison.Ordinal);
        var binaryChainFastPathIndex = routeSource.IndexOf(
            "plan.SimpleReturnParameterBinaryChain",
            StringComparison.Ordinal);
        var unifiedBytecodeIndex = routeSource.IndexOf(
            "CanUseProductionUnifiedBytecodeFastPath(plan, newTarget)",
            StringComparison.Ordinal);
        var syncIrTrampolineIndex = routeSource.IndexOf(
            "SyncIrCallTrampoline.TryInvoke(",
            StringComparison.Ordinal);
        var genericRunnerIndex = routeSource.IndexOf("new ExecutionPlanRunner(", StringComparison.Ordinal);

        Assert.True(
            binaryFastPathIndex >= 0,
            "Simple binary fast path is missing from the ordinary sync route.");
        Assert.True(
            binaryChainFastPathIndex > binaryFastPathIndex,
            "Binary-chain fast path should stay after the simple binary fast path.");
        Assert.True(
            unifiedBytecodeIndex > binaryChainFastPathIndex,
            "Production unified bytecode should stay behind the specialized simple-return fast paths.");
        Assert.True(
            syncIrTrampolineIndex > unifiedBytecodeIndex,
            "Production unified bytecode should be attempted before SyncIrCallTrampoline.");
        Assert.True(
            genericRunnerIndex > syncIrTrampolineIndex,
            "Generic ExecutionPlanRunner fallback should stay after production unified bytecode and SyncIrCallTrampoline.");
    }

    [Fact]
    public void SourceGate_ProductionUnifiedBytecodeAcceptedPath_DoesNotDelegateToAstOrExecutionPlanRunner()
    {
        var repositoryRoot = FindRepositoryRootForSourceGate();
        var invokerPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Ast",
            "TypedAstEvaluator.SyncFunctionInvoker.cs");
        var vmPath = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Asynkron.JsEngine",
            "Execution",
            "UnifiedBytecode",
            "UnifiedBytecodeVirtualMachine.cs");

        var invokerSource = File.ReadAllText(invokerPath);
        var acceptedPathStart = invokerSource.IndexOf(
            "private bool TryInvokeProductionUnifiedBytecode<TArgs>(",
            StringComparison.Ordinal);
        Assert.True(acceptedPathStart >= 0, "Could not locate TryInvokeProductionUnifiedBytecode fast-path method.");
        var acceptedPathEnd = invokerSource.IndexOf(
            "private bool TryGetProductionUnifiedBytecodeProgram(",
            acceptedPathStart,
            StringComparison.Ordinal);
        Assert.True(acceptedPathEnd > acceptedPathStart, "Could not locate end boundary for TryInvokeProductionUnifiedBytecode.");
        var acceptedPathSource = invokerSource.Substring(acceptedPathStart, acceptedPathEnd - acceptedPathStart);

        Assert.DoesNotContain("ExecutionPlanRunner", acceptedPathSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpressionProgram", acceptedPathSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EvaluateExpression(", acceptedPathSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileEvaluateExpression(", acceptedPathSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EvaluateDynamicExpressionProgram(", acceptedPathSource, StringComparison.Ordinal);

        var vmSource = File.ReadAllText(vmPath);
        Assert.DoesNotContain("ExecutionPlanRunner", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpressionProgram", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EvaluateExpression(", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileEvaluateExpression(", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EvaluateDynamicExpressionProgram(", vmSource, StringComparison.Ordinal);
    }

    private static DirectoryInfo FindRepositoryRootForSourceGate()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if ((Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                 File.Exists(Path.Combine(current.FullName, ".git")) ||
                 File.Exists(Path.Combine(current.FullName, "Asynkron.JsEngine.sln"))) &&
                Directory.Exists(Path.Combine(current.FullName, "src", "Asynkron.JsEngine")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for unified-bytecode source gate.");
    }

    // This-binding widening proof pack (issue #2633 / ADR 0279)

    [Fact(Timeout = 5000)]
    public async Task ClassInstanceMethod_ThisPropertyRead_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Point {
                getX() {
                    return this.x;
                }
            }

            var p = new Point();
            p.x = 42;
            p.getX();
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous>",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task PlainObjectMethod_ThisPropertyMutation_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var counter = {
                count: 0,
                inc() {
                    this.count = 1;
                }
            };

            counter.inc();
            counter.count;
            """);

        Assert.Equal(1d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous>",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ClassInstanceMethod_ThisPropertyCompoundWrite_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Counter {
                constructor() {
                    this.count = 0;
                }

                add(value) {
                    return this.count += value;
                }
            }

            var c = new Counter();
            c.add(40);
            c.add(2);
            c.count;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous>",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task PlainObjectMethod_ThisPropertyCompoundWrite_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var counter = {
                count: 0,
                add(value) {
                    return this.count += value;
                }
            };

            counter.add(40);
            counter.add(2);
            counter.count;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous>",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task StrictFunction_ThisPropertyRead_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            "use strict";

            function readOwn(obj) {
                return obj.value;
            }

            readOwn({ value: 42 });
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readOwn",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SloppyFunction_PrimitiveThis_IsCoercedBeforeVmEntry()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function getLength() {
                return this.length;
            }

            getLength.call("hello");
            """);

        Assert.Equal(5d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=getLength",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ClassMethodWithSuperProperty_DeclinesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                get value() { return 42; }
            }

            class Child extends Base {
                readSuper() {
                    return super.value;
                }
            }

            new Child().readSuper();
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=readSuper",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ArrowFunction_CapturedThis_DeclinesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function makeGetter(obj) {
                var get = () => obj.value;
                return get();
            }

            makeGetter({ value: 42 });
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=get",
                StringComparison.Ordinal));
    }

    public static TheoryData<string, string, double, string> UnsupportedControlFlowFunctions =>
        new()
        {
            {
                """
                function unsupportedBranchPayload(a, b, pick) {
                    if (pick) {
                        return Math.max(a, b);
                    }

                    return b;
                }
                """,
                "unsupportedBranchPayload(2, 3, true)",
                3d,
                "unsupportedBranchPayload"
            }
        };

    public static TheoryData<string, string, double, string, int> SupportedLoopControlFunctions =>
        new()
        {
            {
                """
                function breakLoop(n) {
                    while (n > 0) {
                        break;
                    }

                    return n;
                }
                """,
                "breakLoop(3)",
                3d,
                "breakLoop",
                1
            },
            {
                // Labeled loops are now admitted (loop-control targets are compiler-owned, ADR 0253):
                // the unused outer label no longer forces a LabelControlFlow decline.
                """
                function labeled(n) {
                    outer: while (n > 0) {
                        n = n - 1;
                    }

                    return n;
                }
                """,
                "labeled(2)",
                0d,
                "labeled",
                1
            },
            {
                // Labeled break out of a single loop routes through the owned Break opcode.
                """
                function labeledBreak(n) {
                    outer: while (n > 0) {
                        break outer;
                    }

                    return n;
                }
                """,
                "labeledBreak(3)",
                3d,
                "labeledBreak",
                1
            },
            {
                // Labeled continue of a single loop routes through the owned Continue opcode.
                """
                function labeledContinue(n) {
                    var total = 0;
                    outer: while (n > 0) {
                        n = n - 1;
                        continue outer;
                        total = total + 1;
                    }

                    return total;
                }
                """,
                "labeledContinue(3)",
                0d,
                "labeledContinue",
                1
            },
            {
                """
                function continueLoop(n) {
                    var total = 0;
                    while (n > 0) {
                        n = n - 1;
                        continue;
                        total = total + 1;
                    }

                    return total;
                }
                """,
                "continueLoop(3)",
                0d,
                "continueLoop",
                1
            },
            {
                """
                function continueFor(n) {
                    var total = 0;
                    for (; n > 0; n = n - 1) {
                        total = total + n;
                        continue;
                        total = 1000;
                    }

                    return total;
                }
                """,
                "continueFor(3)",
                6d,
                "continueFor",
                1
            },
            {
                """
                function countDo(n) {
                    var count = 0;
                    do {
                        count = count + 1;
                        n = n - 1;
                    } while (n > 0);

                    return count;
                }
                """,
                "countDo(0)",
                1d,
                "countDo",
                1
            }
        };

    [Fact(Timeout = 5000)]
    public async Task CallWithThisArgument_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = {
                value: 42,
                invoke(val) {
                    return val === this ? 42 : -1;
                }
            };

            box.run = function runMethod() {
                return this.invoke(this);
            };

            box.run();
            """);

        var logs = CurrentLogger!.Collector.Snapshot();
        foreach (var log in logs)
        {
            output.WriteLine(log.Message);
        }

        Assert.Equal(42d, result);
        Assert.Contains(logs,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=runMethod argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task PropertyWriteWithThis_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = {
                self: null
            };

            box.setSelf = function setSelfMethod() {
                this.self = this;
                return this.self === this;
            };

            box.setSelf();
            """);

        var logs = CurrentLogger!.Collector.Snapshot();
        foreach (var log in logs)
        {
            output.WriteLine(log.Message);
        }

        Assert.Equal(true, result);
        Assert.Contains(logs,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=setSelfMethod argc=0",
                StringComparison.Ordinal));
    }

    // Array-literal and object-literal operand widening (gh2705)

    [Fact(Timeout = 5000)]
    public async Task CallWithArrayLiteralArg_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sendPair(receiver, a, b) {
                return receiver([a, b]);
            }

            sendPair(function(arr) { return arr[0] + arr[1]; }, 40, 2);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=sendPair argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task CallWithObjectLiteralArg_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sendConfig(receiver, x, y) {
                return receiver({ a: x, b: y });
            }

            sendConfig(function(obj) { return obj.a + obj.b; }, 40, 2);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=sendConfig argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task CallWithEmptyArrayArg_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sendEmpty(receiver) {
                return receiver([]);
            }

            sendEmpty(function(arr) { return arr.length; });
            """);

        Assert.Equal(0d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=sendEmpty argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task CallWithEmptyObjectArg_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sendEmptyObj(receiver) {
                return receiver({});
            }

            sendEmptyObj(function(obj) { return Object.keys(obj).length; });
            """);

        Assert.Equal(0d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=sendEmptyObj argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task CallWithMixedScalarAndArrayLiteralArgs_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sendMixed(receiver, a, b, c) {
                return receiver(a, [b, c]);
            }

            sendMixed(function(x, arr) { return x + arr[0] + arr[1]; }, 10, 12, 20);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=sendMixed argc=4",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task PowerOperator_ProducesCorrectResult_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function power(a, b) {
                return a ** b;
            }

            power(2, 8);
            """);

        Assert.Equal(256d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=power argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NotEqualOperator_ProducesCorrectResult_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function notEqual(a, b) {
                return a != b;
            }

            notEqual(1, 2);
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=notEqual argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BitwiseAndOperator_ProducesCorrectResult_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function bitwiseAnd(a, b) {
                return a & b;
            }

            bitwiseAnd(5, 3);
            """);

        Assert.Equal(1d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=bitwiseAnd argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BitwiseOrOperator_ProducesCorrectResult_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function bitwiseOr(a, b) {
                return a | b;
            }

            bitwiseOr(5, 3);
            """);

        Assert.Equal(7d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=bitwiseOr argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task BitwiseXorOperator_ProducesCorrectResult_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function bitwiseXor(a, b) {
                return a ^ b;
            }

            bitwiseXor(5, 3);
            """);

        Assert.Equal(6d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=bitwiseXor argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LeftShiftOperator_ProducesCorrectResult_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function leftShift(a, b) {
                return a << b;
            }

            leftShift(1, 3);
            """);

        Assert.Equal(8d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=leftShift argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task RightShiftOperator_ProducesCorrectResult_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function rightShift(a, b) {
                return a >> b;
            }

            rightShift(8, 1);
            """);

        Assert.Equal(4d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=rightShift argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task UnsignedRightShiftOperator_ProducesCorrectResult_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function unsignedRightShift(a, b) {
                return a >>> b;
            }

            unsignedRightShift(-1, 28);
            """);

        Assert.Equal(15d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=unsignedRightShift argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task InOperator_ProducesCorrectResult_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function hasKey(key, obj) {
                return key in obj;
            }

            hasKey("x", { x: 1 });
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=hasKey argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task InstanceOfOperator_ProducesCorrectResult_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function isInstance(value, ctor) {
                return value instanceof ctor;
            }

            isInstance([], Array);
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=isInstance argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task InOperator_ThrowsTypeError_WhenRhsIsPrimitive_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function testIn(key, target) {
                return key in target;
            }

            var caught = false;
            try {
                testIn("x", 42);
            } catch (e) {
                caught = e instanceof TypeError;
            }
            caught;
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=testIn argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task InstanceOfOperator_ThrowsTypeError_WhenRhsIsPrimitive_OnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function testInstanceOf(value, target) {
                return value instanceof target;
            }

            var caught = false;
            try {
                testInstanceOf({}, 42);
            } catch (e) {
                caught = e instanceof TypeError;
            }
            caught;
            """);

        Assert.Equal(true, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=testInstanceOf argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ObjectLiteralWithShorthandProperties_UsesUnifiedBytecodeProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function build(a, b) {
                return { a, b };
            }

            var o = build(40, 2);
            o.a + o.b;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=build argc=2",
                StringComparison.Ordinal));
    }

    // gh2742: computed-key object literals in call-argument position

    [Fact(Timeout = 5000)]
    public async Task CallWithComputedIdentifierKeyObjectArg_UsesUnifiedBytecodeProductionFastPathAndStoresValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sendComputed(receiver, k, v) {
                return receiver({ [k]: v });
            }

            sendComputed(function(obj) { return obj.result; }, "result", 42);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=sendComputed argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task CallWithComputedStringLiteralKeyObjectArg_UsesUnifiedBytecodeProductionFastPathAndStoresValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sendNamed(receiver, v) {
                return receiver({ ["answer"]: v });
            }

            sendNamed(function(obj) { return obj.answer; }, 42);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=sendNamed argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task CallWithMixedStaticAndComputedKeyObjectArg_UsesUnifiedBytecodeProductionFastPathAndPreservesOrder()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sendMixed(receiver, k, x, y) {
                return receiver({ a: x, [k]: y });
            }

            var o = sendMixed(function(obj) { return obj; }, "b", 40, 2);
            o.a + o.b;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=sendMixed argc=4",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalAnd_ShortCircuitsOnFalsyLeft_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function andOp(a, b) {
                return a && b;
            }

            andOp(0, 42);
            """);

        Assert.Equal(0d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=andOp argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalAnd_EvaluatesRight_WhenLeftIsTruthy_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function andOp(a, b) {
                return a && b;
            }

            andOp(1, 42);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=andOp argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalAnd_LiteralRhs_ShortCircuitsOnFalsy()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(a) {
                return a && 99;
            }

            [f(null), f(1)];
            """);

        var arr1 = Assert.IsType<JsTypes.JsArray>(result);
        Assert.True(arr1.Items[0].IsNull);
        Assert.Equal(99d, arr1.Items[1].AsDouble());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=f argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalOr_ShortCircuitsOnTruthyLeft_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function orOp(a, b) {
                return a || b;
            }

            orOp(1, 99);
            """);

        Assert.Equal(1d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=orOp argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalOr_EvaluatesRight_WhenLeftIsFalsy_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function orOp(a, b) {
                return a || b;
            }

            orOp(0, 42);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=orOp argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalOr_LiteralFallback_ShortCircuitsOnTruthy()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(a) {
                return a || 99;
            }

            [f(0), f(7)];
            """);

        var arr3 = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal(99d, arr3.Items[0].AsDouble());
        Assert.Equal(7d, arr3.Items[1].AsDouble());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=f argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NullishCoalescing_ShortCircuitsOnNonNullishLeft_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function nullishOp(a, b) {
                return a ?? b;
            }

            nullishOp(0, 42);
            """);

        Assert.Equal(0d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=nullishOp argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NullishCoalescing_EvaluatesRight_WhenLeftIsNull_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function nullishOp(a, b) {
                return a ?? b;
            }

            nullishOp(null, 42);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=nullishOp argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NullishCoalescing_EvaluatesRight_WhenLeftIsUndefined_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function nullishOp(a, b) {
                return a ?? b;
            }

            nullishOp(undefined, 42);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=nullishOp argc=2",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NullishCoalescing_LiteralFallback_ShortCircuitsOnNonNullish()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function f(a) {
                return a ?? 99;
            }

            [f(null), f(undefined), f(0), f("hello")];
            """);

        var arr5 = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal(99d, arr5.Items[0].AsDouble());
        Assert.Equal(99d, arr5.Items[1].AsDouble());
        Assert.Equal(0d, arr5.Items[2].AsDouble());
        Assert.Equal("hello", arr5.Items[3].AsString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=f argc=1",
                StringComparison.Ordinal));
    }

    // Literal-right operand proof pack for &&/||/?? (ADR 0238 batch-5)

    [Fact(Timeout = 5000)]
    public async Task LogicalAnd_LiteralRight_ShortCircuitsOnFalsyLeft_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function andLiteral(a) {
                return a && 42;
            }

            andLiteral(0);
            """);

        Assert.Equal(0d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=andLiteral argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalAnd_LiteralRight_EvaluatesRight_WhenLeftIsTruthy_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function andLiteral(a) {
                return a && 42;
            }

            andLiteral(1);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=andLiteral argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalOr_LiteralRight_ShortCircuitsOnTruthyLeft_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function orLiteral(a) {
                return a || 99;
            }

            orLiteral(1);
            """);

        Assert.Equal(1d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=orLiteral argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalOr_LiteralRight_EvaluatesRight_WhenLeftIsFalsy_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function orLiteral(a) {
                return a || 99;
            }

            orLiteral(0);
            """);

        Assert.Equal(99d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=orLiteral argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NullishCoalescing_LiteralRight_ShortCircuitsOnNonNullishLeft_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function nullishLiteral(a) {
                return a ?? 99;
            }

            nullishLiteral(0);
            """);

        Assert.Equal(0d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=nullishLiteral argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NullishCoalescing_LiteralRight_EvaluatesRight_WhenLeftIsNull_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function nullishLiteral(a) {
                return a ?? 99;
            }

            nullishLiteral(null);
            """);

        Assert.Equal(99d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=nullishLiteral argc=1",
                StringComparison.Ordinal));
    }

    // This-property-left operand proof pack for &&/||/?? (ADR 0238 batch-5)

    [Fact(Timeout = 5000)]
    public async Task LogicalAnd_ThisPropertyLeft_ShortCircuitsOnFalsyLeft_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Guard {
                check(b) {
                    return this.enabled && b;
                }
            }

            var g = new Guard();
            g.enabled = 0;
            g.check(42);
            """);

        Assert.Equal(0d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous>",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalAnd_ThisPropertyLeft_EvaluatesRight_WhenLeftIsTruthy_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Guard {
                check(b) {
                    return this.enabled && b;
                }
            }

            var g = new Guard();
            g.enabled = 1;
            g.check(42);
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous>",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalOr_ThisPropertyLeft_ShortCircuitsOnTruthyLeft_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Fallback {
                resolve(b) {
                    return this.value || b;
                }
            }

            var f = new Fallback();
            f.value = 1;
            f.resolve(99);
            """);

        Assert.Equal(1d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous>",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task LogicalOr_ThisPropertyLeft_EvaluatesRight_WhenLeftIsFalsy_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Fallback {
                resolve(b) {
                    return this.value || b;
                }
            }

            var f = new Fallback();
            f.value = 0;
            f.resolve(99);
            """);

        Assert.Equal(99d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous>",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NullishCoalescing_ThisPropertyLeft_ShortCircuitsOnNonNullishLeft_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Cache {
                resolve(b) {
                    return this.cached ?? b;
                }
            }

            var c = new Cache();
            c.cached = 0;
            c.resolve(99);
            """);

        Assert.Equal(0d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous>",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task NullishCoalescing_ThisPropertyLeft_EvaluatesRight_WhenLeftIsNull_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Cache {
                resolve(b) {
                    return this.cached ?? b;
                }
            }

            var c = new Cache();
            c.cached = null;
            c.resolve(99);
            """);

        Assert.Equal(99d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous>",
                StringComparison.Ordinal));
    }

    // Conditional (ternary) expression — ADR 0294

    [Fact(Timeout = 5000)]
    public async Task Ternary_TruthyCondition_ReturnsConsequent_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function pick(cond, a, b) {
                return cond ? a : b;
            }

            pick(1, 10, 20);
            """);

        Assert.Equal(10d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=pick argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Ternary_FalsyCondition_ReturnsAlternate_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function pick(cond, a, b) {
                return cond ? a : b;
            }

            pick(0, 10, 20);
            """);

        Assert.Equal(20d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=pick argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Ternary_Nested_ReturnsCorrectBranch_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function classify(x) {
                return x > 0 ? (x > 10 ? 2 : 1) : 0;
            }

            [classify(-1), classify(5), classify(15)];
            """);

        var arr = Assert.IsType<JsTypes.JsArray>(result);
        Assert.Equal(0d, arr.Items[0].AsDouble());
        Assert.Equal(1d, arr.Items[1].AsDouble());
        Assert.Equal(2d, arr.Items[2].AsDouble());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=classify argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Ternary_ConditionIsConsumedNotPeeked_BothBranchesCorrect()
    {
        // AC-2: verifies consume semantics — if the condition were left on the stack
        // (peek), the stack would overflow and results would be wrong across calls.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function pick(cond, a, b) {
                return cond ? a : b;
            }

            pick(1, 10, 20) + pick(0, 10, 20);
            """);

        // 10 (truthy picks a=10) + 20 (falsy picks b=20)
        Assert.Equal(30d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=pick argc=3",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Ternary_LiteralArms_TruthyCondition_ReturnsConsequentLiteral_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function clamp(a) {
                return a > 0 ? 1 : 0;
            }

            clamp(5);
            """);

        Assert.Equal(1d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=clamp argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Ternary_Nested_AllCombinationsCorrect_OnProductionFastPath()
    {
        // AC-3: c1 ? (c2 ? a : b) : d — all four combinations of c1/c2.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function nested(c1, c2, a, b, d) {
                return c1 ? c2 ? a : b : d;
            }

            [nested(1,1,10,20,30), nested(1,0,10,20,30), nested(0,1,10,20,30), nested(0,0,10,20,30)].join(',');
            """);

        Assert.Equal("10,20,30,30", result as string);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=nested",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Ternary_LiteralArms_FalsyCondition_ReturnsAlternateLiteral_UsesProductionFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function clamp(a) {
                return a > 0 ? 1 : 0;
            }

            clamp(-3);
            """);

        Assert.Equal(0d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=clamp argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Ternary_SideEffectOnce_OnlyChosenArmEvaluated()
    {
        // Gate: non-chosen arm must never be evaluated.
        // Note: a ternary with function-call arms is declined from the production fast path;
        // this test verifies behavioral short-circuit correctness independently.
        await using var engine = CreateEngine();

        var countTruthy = await engine.Evaluate("""
            var count = 0;
            function effect(v) { count++; return v; }
            function pick(cond) { return cond ? effect(10) : effect(20); }
            pick(1);
            count;
            """);
        Assert.Equal(1d, countTruthy);

        var countFalsy = await engine.Evaluate("""
            var count2 = 0;
            function effect2(v) { count2++; return v; }
            function pick2(cond) { return cond ? effect2(10) : effect2(20); }
            pick2(0);
            count2;
            """);
        Assert.Equal(1d, countFalsy);
    }
}
