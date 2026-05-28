using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeProductionInvocationTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string UnifiedBytecodeProductionFastPathLog = "unified-bytecode-production-fast-path";
    private const string SimpleIrParameterNumberBinaryFastPathLog =
        "simple-ir-parameter-number-binary-fast-path";

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
    public async Task StrictEqualityBranchFunction_DeclinesUnifiedBytecodeAndFallsBack()
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
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=chooseByStrictEquality",
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

    [Theory(Timeout = 5000)]
    [InlineData(
        """
        function spreadArray(source) {
            return [1, ...source];
        }

        spreadArray([41])[1];
        """,
        "spreadArray",
        41d)]
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

    [Fact(Timeout = 5000)]
    public async Task CallExpressionFunction_DeclinesUnifiedBytecodeAndFallsBack()
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
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=invoke",
                StringComparison.Ordinal));
    }

    [Theory(Timeout = 5000)]
    [InlineData(
        """
        function callMember(box) {
            return box.read();
        }

        callMember({ read() { return 3; } });
        """,
        "callMember",
        3d)]
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
        function readThis() {
            return this.value;
        }

        readThis.call({ value: 7 });
        """,
        "readThis",
        7d)]
    [InlineData(
        """
        function readOptional(box) {
            return box?.value;
        }

        readOptional({ value: 1 });
        """,
        "readOptional",
        1d)]
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
        function writeDiscarded(box, value) {
            box.value = value;
            return box.value;
        }

        writeDiscarded({ value: 1 }, 42);
        """,
        "writeDiscarded",
        42d)]
    [InlineData(
        """
        function updateDiscarded(box) {
            box.value++;
            return box.value;
        }

        updateDiscarded({ value: 1 });
        """,
        "updateDiscarded",
        2d)]
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

    public static TheoryData<string, string, double, string> UnsupportedControlFlowFunctions =>
        new()
        {
            {
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
                "labeled"
            },
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
                "breakLoop"
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
                "continueLoop"
            },
            {
                """
                function nonCanonicalFor(n) {
                    var total = 0;
                    for (; n > 0; n = n - 1) {
                        total = total + n;
                    }

                    return total;
                }
                """,
                "nonCanonicalFor(3)",
                6d,
                "nonCanonicalFor"
            },
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
}
