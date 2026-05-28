using System.IO;
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
}
