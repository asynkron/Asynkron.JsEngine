using Asynkron.JsEngine.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
[Category(TestCategories.Eval)]
[Category(TestCategories.StrictMode)]
[Category(TestCategories.AsyncRuntime)]
[Category(TestCategories.IteratorRuntime)]
public sealed class ActivationSemanticsProofPackTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string SimpleIrActivationFastPathLog = "simple-ir-activation-fast-path";
    private const string SimpleIrParameterNumberBinaryFastPathLog = "simple-ir-parameter-number-binary-fast-path";
    private const string SimpleIrParameterNumberBinaryChainFastPathLog =
        "simple-ir-parameter-number-binary-chain-fast-path";
    private const string SimpleIrParameterBinaryFastPathLog = "simple-ir-parameter-binary-fast-path";
    private const string SimpleIrParameterBinaryChainFastPathLog = "simple-ir-parameter-binary-chain-fast-path";
    private const string SimpleIrReturnFastPathLog = "simple-ir-return-fast-path";
    private const string UnifiedBytecodeProductionFastPathLog = "unified-bytecode-production-fast-path";
    private const string ClassifiedOrdinarySyncFunctionFallbackLog = "classified-ordinary-sync-function-fallback";
    private const string OrdinarySyncDynamicScopeExecutorLog =
        "ordinary-sync-declined-body-dynamic-scope-executor";

    [Fact(Timeout = 5000)]
    public async Task SimpleSyncFunction_DoesNotUseCallerBinaryOrSimpleReturnFastPaths()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function add(a, b) {
                var c = a;
                var d = b;
                return c + d;
            }

            add(20, 22);
            """);

        Assert.Equal(42d, result);
        var logRecords = CurrentLogger!.Collector.Snapshot();
        Assert.DoesNotContain(logRecords,
            static record => record.Message.Contains(SimpleIrParameterBinaryFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(logRecords,
            static record => record.Message.Contains(SimpleIrReturnFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleReturnFunction_NumberArgumentsUseProductionUnifiedBytecode()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function add(a, b) {
                return a + b;
            }

            add(20, 22);
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrActivationFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrParameterNumberBinaryFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrParameterBinaryFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrReturnFastPathLog, StringComparison.Ordinal));
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(UnifiedBytecodeProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleReturnFunction_NumberParameterBinaryChainUsesProductionUnifiedBytecode()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function blend(a, b, c) {
                return (a + b) ^ c;
            }

            blend(20, 22, 0);
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrActivationFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                SimpleIrParameterNumberBinaryChainFastPathLog,
                StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrParameterBinaryChainFastPathLog, StringComparison.Ordinal));
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(UnifiedBytecodeProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleReturnFunction_CoercingParameterBinaryChainUsesProductionUnifiedBytecode()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function blend(a, b, c) {
                return (a + b) ^ c;
            }

            blend("40", 2, 0);
            """);

        Assert.Equal(402d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrActivationFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                SimpleIrParameterNumberBinaryChainFastPathLog,
                StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrParameterBinaryChainFastPathLog, StringComparison.Ordinal));
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(UnifiedBytecodeProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleReturnFunction_NonNumberArgumentsDoNotUseCallerOrReturnFastPaths()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let hits = 0;
            const box = {
                valueOf() {
                    hits++;
                    return 20;
                }
            };

            function add(a, b) {
                return a + b;
            }

            String(add(box, 22)) + ":" + String(hits);
            """);

        Assert.Equal("42:1", result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrActivationFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrParameterNumberBinaryFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrParameterBinaryFastPathLog, StringComparison.Ordinal));
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(UnifiedBytecodeProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleReturnFunction_PropagatesThrownExpression()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function boom() {
                throw new Error("fast path throw");
            }

            function probe() {
                return boom();
            }

            try {
                probe();
            } catch (err) {
                err.message;
            }
            """);

        Assert.Equal("fast path throw", result);
        // A10 (burn-down): probe() returns a FREE identifier call (boom()), so it now routes through the
        // production unified bytecode VM (the free call target lowers to PrepareDynamicIdentifierCallTarget)
        // rather than the older simple-IR-return fast path. Thrown exceptions still propagate correctly.
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrReturnFastPathLog, StringComparison.Ordinal));
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(UnifiedBytecodeProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleReturnLiteralFunction_UsesProductionUnifiedBytecode()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function ping() {
                return 1;
            }

            ping();
            """);

        Assert.Equal(1d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrActivationFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrReturnFastPathLog, StringComparison.Ordinal));
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(UnifiedBytecodeProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleReturnParameterFunction_NoArguments_UsesProductionUnifiedBytecode()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                return a;
            }

            probe();
            """);

        Assert.Equal(Asynkron.JsEngine.Ast.Symbol.Undefined, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrReturnFastPathLog, StringComparison.Ordinal));
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrActivationFastPathLog, StringComparison.Ordinal));
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(UnifiedBytecodeProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleReturnParameterFunction_WithArgument_DoesNotUseNoArgsReturnFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                return a;
            }

            probe(42);
            """);

        Assert.Equal(42d, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains("simple-ir-return-fast-path func=probe argc=0", StringComparison.Ordinal));
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(UnifiedBytecodeProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task DirectEvalFunctionDeclarationParameterDefault_UsesIrProductionOrOrdinaryDeclinedRoute()
    {
        var logger = new TestLogger(output, "DirectEvalDefaultRoute", minLogLevel: LogLevel.Debug);
        await using var engine = CreateEngine(() => new JsEngineOptions
        {
            DebugMode = true,
            Logger = logger
        });

        var result = await engine.Evaluate("""
            function read(value = eval("40 + 2")) {
                return value;
            }

            read();
            """);

        var records = logger.Collector.Snapshot();
        Assert.Equal(42d, result);
        Assert.DoesNotContain(records,
            static record => record.Message.Contains(
                ClassifiedOrdinarySyncFunctionFallbackLog + " reason=production-unified-bytecode-declined func=read",
                StringComparison.Ordinal));
        Assert.Contains(records,
            static record =>
                record.Message.Contains(
                    OrdinarySyncDynamicScopeExecutorLog + " reason=production-unified-bytecode-declined func=read",
                    StringComparison.Ordinal) ||
                record.Message.Contains(
                    UnifiedBytecodeProductionFastPathLog + " func=read",
                    StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ClassMethodCreatedInsideFunctionWithoutCapturedNames_UsesProductionUnifiedBytecode()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function make() {
                return class {
                    static seed = 41;
                    static value() {
                        return this.seed + 1;
                    }
                };
            }

            make().value();
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous> argc=0",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task ClassMethodCreatedInsideFunctionWithCapturedName_UsesCapturedClosureProductionPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function make(seed) {
                return class {
                    static value() {
                        return seed + 1;
                    }
                };
            }

            make(41).value();
            """);

        var records = CurrentLogger!.Collector.Snapshot();
        Assert.Equal(42d, result);
        Assert.Contains(records,
            static record => record.Message.Contains(
                "unified-bytecode-production-fast-path func=<anonymous> argc=0",
                StringComparison.Ordinal));
        Assert.DoesNotContain(records,
            static record => record.Message.Contains(
                "ExecutionPlanRunner",
                StringComparison.Ordinal));
    }

    [Theory(Timeout = 5000)]
    [InlineData("""
        function probe(a) {
            return arguments[0];
        }

        probe(42);
        """, 42d)]
    [InlineData("""
        function probe(a) {
            eval("var local = a + 1;");
            return local;
        }

        probe(41);
        """, 42d)]
    [InlineData("""
        var box = { value: 41 };
        function probe() {
            with (box) {
                return value + 1;
            }
        }

        probe();
        """, 42d)]
    [InlineData("""
        function probe(a = 42) {
            return a;
        }

        probe();
        """, 42d)]
    [InlineData("""
        function probe(...items) {
            return items[0];
        }

        probe(42);
        """, 42d)]
    [InlineData("""
        function probe({ value }) {
            return value;
        }

        probe({ value: 42 });
        """, 42d)]
    [InlineData("""
        function probe(a, a) {
            return a;
        }

        probe(1, 42);
        """, 42d)]
    [InlineData("""
        function* probe(a) {
            yield a;
        }

        probe(42).next().value;
        """, 42d)]
    [InlineData("""
        class Probe {
            constructor(value) {
                this.value = value;
            }
        }

        new Probe(42).value;
        """, 42d)]
    [InlineData("""
        class Base {
            value() {
                return 41;
            }
        }

        class Probe extends Base {
            value() {
                return super.value() + 1;
            }
        }

        new Probe().value();
        """, 42d)]
    [InlineData("""
        class Probe {
            #value = 41;
            value(delta) {
                return this.#value + delta;
            }
        }

        new Probe().value(1);
        """, 42d)]
    public async Task UnsafeActivationShapes_DoNotUseIrActivationFastPath(string script, double expected)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(script);

        Assert.Equal(expected, result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrActivationFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncActivation_DoesNotUseIrActivationFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            async function probe(a) {
                return a + 1;
            }

            probe(41);
            "called";
            """);

        Assert.Equal("called", result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(SimpleIrActivationFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task SloppySimpleParameters_MapArgumentsObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                a = a + 1;
                return arguments[0] + ":" + a;
            }

            probe(41);
            """);

        Assert.Equal("42:42", result);
    }

    [Fact(Timeout = 5000)]
    public async Task SloppyMappedArgumentsObject_SurvivesEscapedActivation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var arg;
            (function probe(a, b, c) {
                arg = arguments;
            }(0, 1, 2));

            Object.defineProperties(arg, {
                "0": {
                    value: 20,
                    writable: false,
                    enumerable: false,
                    configurable: false
                }
            });

            var descriptor = Object.getOwnPropertyDescriptor(arg, "0");
            arg[0] + ":" + descriptor.writable + ":" + descriptor.enumerable + ":" + descriptor.configurable;
            """);

        Assert.Equal("20:false:false:false", result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictSimpleParameters_DoNotMapArgumentsObject()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                "use strict";
                a = a + 1;
                return arguments[0] + ":" + a;
            }

            probe(41);
            """);

        Assert.Equal("41:42", result);
    }

    [Fact(Timeout = 5000)]
    public async Task ArgumentsLengthRead_RespectsMutationAndPrototypeFallback()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                "use strict";
                const before = arguments.length;
                arguments.length = 0;
                const afterSet = arguments.length;
                delete arguments.length;
                Object.setPrototypeOf(arguments, { length: 9 });
                return [before, afterSet, arguments.length, arguments[0]].join(":");
            }

            probe(41);
            """);

        Assert.Equal("1:0:9:41", result);
    }

    [Fact(Timeout = 5000)]
    public async Task ArgumentsNumericIndexRead_RespectsAccessorDescriptor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                Object.defineProperty(arguments, "0", {
                    get() { return a + 1; }
                });
                return arguments[0];
            }

            probe(41);
            """);

        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task ArgumentsNumericIndexRead_FallsBackToPrototypeAfterDelete()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                Object.setPrototypeOf(arguments, { 0: 42 });
                delete arguments[0];
                return arguments[0];
            }

            probe(41);
            """);

        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task ArgumentsLazyIndices_RemainVisibleToDescriptorsAndEnumeration()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a, b) {
                const descriptor = Object.getOwnPropertyDescriptor(arguments, "0");
                return [
                    Object.keys(arguments).join(","),
                    Object.getOwnPropertyNames(arguments).slice(0, 3).join(","),
                    descriptor.value,
                    descriptor.enumerable,
                    descriptor.writable,
                    descriptor.configurable
                ].join(":");
            }

            probe(40, 2);
            """);

        Assert.Equal("0,1:0,1,length:40:true:true:true", result);
    }

    [Fact(Timeout = 5000)]
    public async Task ArgumentsNonCanonicalNumericNames_DoNotAliasLazyIndices()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a, b) {
                const beforeDescriptor = Object.getOwnPropertyDescriptor(arguments, "00");
                const beforeHasOwn = Object.hasOwn(arguments, "00");
                arguments["00"] = 99;
                const afterDescriptor = Object.getOwnPropertyDescriptor(arguments, "00");
                const names = Object.getOwnPropertyNames(arguments);
                const keys = Object.keys(arguments);
                return [
                    beforeDescriptor === undefined,
                    beforeHasOwn,
                    Object.hasOwn(arguments, "00"),
                    afterDescriptor.value,
                    a,
                    names.indexOf("00") >= 0,
                    keys.indexOf("00") >= 0,
                    Object.getOwnPropertyDescriptor(arguments, "0").value
                ].join(":");
            }

            probe(41, 2);
            """);

        Assert.Equal("true:false:true:99:41:true:true:41", result);
    }

    [Fact(Timeout = 5000)]
    public async Task ArgumentsLazyIndices_DefinePropertyAfterPreventExtensionsUsesExistingIndex()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                Object.preventExtensions(arguments);
                Object.defineProperty(arguments, "0", { value: 42 });
                return arguments[0] + ":" + Object.getOwnPropertyDescriptor(arguments, "0").value;
            }

            probe(41);
            """);

        Assert.Equal("42:42", result);
    }

    [Fact(Timeout = 5000)]
    public async Task ArgumentsLazyIndices_SealUpdatesTrackedIndexDescriptors()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                Object.seal(arguments);
                const descriptor = Object.getOwnPropertyDescriptor(arguments, "0");
                return [
                    Object.isSealed(arguments),
                    descriptor.configurable,
                    descriptor.writable,
                    descriptor.value
                ].join(":");
            }

            probe(41);
            """);

        Assert.Equal("true:false:true:41", result);
    }

    [Fact(Timeout = 5000)]
    public async Task ArgumentsLazyIndices_FreezeUpdatesTrackedIndexDescriptors()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                Object.freeze(arguments);
                const descriptor = Object.getOwnPropertyDescriptor(arguments, "0");
                const before = arguments[0];
                arguments[0] = 42;
                a = 99;
                return [
                    Object.isFrozen(arguments),
                    descriptor.configurable,
                    descriptor.writable,
                    before,
                    arguments[0],
                    a
                ].join(":");
            }

            probe(41);
            """);

        Assert.Equal("true:false:false:41:41:99", result);
    }

    [Fact(Timeout = 5000)]
    public async Task ArgumentsMaterializedIndex_DeleteStillFallsBackToPrototype()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                Object.getOwnPropertyDescriptor(arguments, "0");
                Object.setPrototypeOf(arguments, { 0: 42 });
                delete arguments[0];
                return arguments[0] + ":" + Object.keys(arguments).join(",");
            }

            probe(41);
            """);

        Assert.Equal("42:", result);
    }

    [Fact(Timeout = 5000)]
    public async Task ArgumentsMaterializedIndex_AssignmentUpdatesTrackedDescriptor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                "use strict";
                Object.getOwnPropertyDescriptor(arguments, "0");
                arguments[0] = 42;
                return arguments[0] + ":" + Object.getOwnPropertyDescriptor(arguments, "0").value;
            }

            probe(41);
            """);

        Assert.Equal("42:42", result);
    }

    [Fact(Timeout = 5000)]
    public async Task DefaultParameterDisablesArgumentsMapping()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a = 1) {
                a = a + 1;
                return arguments[0] + ":" + a + ":" + arguments.length;
            }

            probe(41);
            """);

        Assert.Equal("41:42:1", result);
    }

    [Fact(Timeout = 5000)]
    public async Task RestAndDestructuredParameters_BindFromActivationWithoutArgumentsAliasing()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe({ x }, y = x + 1, ...rest) {
                rest[0] = 99;
                return [x, y, arguments[0].x, rest[0], arguments.length].join(":");
            }

            probe({ x: 41 }, undefined, 7);
            """);

        Assert.Equal("41:42:41:99:3", result);
    }

    [Fact(Timeout = 5000)]
    public async Task ArgumentsLoop_NonCapturingForLet_ComputesAndKeepsLoopBindingScoped()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';

            function score() {
                let value = 0;
                for (let i = 0; i < arguments.length; i++) {
                    value += arguments[i];
                }

                try {
                    i;
                    return "leaked";
                } catch (error) {
                    return value + ":" + (error instanceof ReferenceError);
                }
            }

            score(10, 20, 12);
            """);

        Assert.Equal("42:true", result);
    }

    [Fact(Timeout = 5000)]
    public async Task NestedClosures_KeepCapturedActivationStateAcrossCalls()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function makeCounter(start) {
                let count = start;
                return function () {
                    count = count + 1;
                    return count;
                };
            }

            const a = makeCounter(0);
            const b = makeCounter(10);

            [a(), a(), b(), a(), b()].join(",");
            """);

        Assert.Equal("1,2,11,3,12", result);
    }

    [Fact(Timeout = 5000)]
    public async Task ClosureCaptureRead_UsesProductionUnifiedBytecodeFastPath()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function makeReader(a) {
                return function read() {
                    return a;
                };
            }

            var read = makeReader(42);
            read();
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(UnifiedBytecodeProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task DirectEvalInParameterDefault_SeesActivationBindings()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a, b = eval("a + 1")) {
                return b;
            }

            probe(41);
            """);

        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task DirectEvalVarDeclaration_WritesIntoFunctionActivation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function probe(a) {
                eval("var local = a + 1;");
                return local;
            }

            probe(41);
            """);

        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task RepeatedDirectEval_UsesCurrentActivationBindings()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            'use strict';

            let shared = 0;
            function makeEvalReader(offset) {
                return function read(x) {
                    const y = x + offset;
                    return eval('y + shared');
                };
            }

            const first = makeEvalReader(1);
            const second = makeEvalReader(10);
            shared = 5;
            const a = first(2);
            shared = 7;
            const b = first(3);
            shared = 11;
            const c = second(4);
            [a, b, c].join(',');
            """);

        Assert.Equal("8,11,25", result);
    }

    [Fact(Timeout = 5000)]
    public async Task WithAndStrictMode_StaySeparated()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var box = { value: 41 };

            function sloppy() {
                with (box) {
                    return value + 1;
                }
            }

            var strictCaught = false;
            try {
                eval("'use strict'; with (box) { value; }");
            } catch (err) {
                strictCaught = err instanceof SyntaxError;
            }

            [sloppy(), strictCaught].join(":");
            """);

        Assert.Equal("42:true", result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictAndSloppyCalls_KeepDistinctThisBindingRules()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sloppyThis() {
                return this === globalThis;
            }

            function strictThis() {
                "use strict";
                return this === undefined;
            }

            [sloppyThis(), strictThis()].join(":");
            """);

        Assert.Equal("true:true", result);
    }

    [Fact(Timeout = 5000)]
    public async Task StrictAndSloppyCall_WithPrimitiveThis_PreserveCoercionRules()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function sloppyThis() {
                return [typeof this, this instanceof Number, this.valueOf()].join(":");
            }

            function strictThis() {
                "use strict";
                return [typeof this, this === 7].join(":");
            }

            [sloppyThis.call(7), strictThis.call(7)].join("|");
            """);

        Assert.Equal("object:true:7|number:true", result);
    }

    [Fact(Timeout = 5000)]
    public async Task DerivedConstructor_InitializesBaseInstanceWithSuperCall()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor(value) {
                    this.value = value;
                }
            }

            class Derived extends Base {
                constructor(value) {
                    super(value + 1);
                }
            }

            new Derived(41).value;
            """);

        Assert.Equal(42d, result);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorActivation_PreservesCapturedParameterAcrossSuspension()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* probe(a) {
                const read = () => a;
                yield read();
                a = a + 1;
                yield read();
            }

            const iterator = probe(41);
            [iterator.next().value, iterator.next().value].join("|");
            """);

        Assert.Equal("41|42", result);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncFunctionActivation_PreservesCapturedParameterAcrossAwait()
    {
        await using var engine = CreateEngine();

        await engine.Evaluate("""
            var observed = "";

            async function probe(a) {
                const read = () => a;
                const first = read();
                a = await Promise.resolve(a + 1);
                const second = read();
                return first + ":" + second;
            }

            probe(41).then(function(value) {
                observed = value;
            });
            """);

        var result = await engine.Evaluate("observed;");
        Assert.Equal("41:42", result);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorActivation_CapturedParameterFailsExplicitlyAfterUnifiedDecline()
    {
        await using var engine = CreateEngine();

        var result = await engine.EvaluateAndAwait("""
            var output = "";
            var observed = [];

            async function* probe(a) {
                const read = () => a;
                observed.push("start:" + read());
                a = await Promise.resolve(a + 1);
                yield read();
            }

            async function run() {
                for await (const value of probe(41)) {
                    observed.push("yield:" + value);
                }
            }

            run().then(function() {
                output = observed.join("|");
            }, function(e) {
                output = "error:" + String(e);
            });

            output;
            """);

        var message = result?.ToString();
        Assert.NotNull(message);
        Assert.StartsWith("error:Async-generator body 'probe' is not eligible for unified bytecode execution:", message, StringComparison.Ordinal);
    }
}
