using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for the narrow B24 class-expression field slices in the resumable VM.
///     Public non-computed instance fields without activation-capturing initializers and public
///     non-computed static fields may route, including the mixed public static+instance field subset,
///     while nearby class-element families remain pre-VM declines.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableClassExpressionTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    [Fact]
    public void EvaluateResumable_ClassExpressionPublicInstanceFields_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 0;
                var C = class {
                    first = 2;
                    empty;
                    second = this.first + 1;
                    constructor(value) {
                        this.ctor = value;
                    }
                };
                var c = new C(7);
                yield c.second;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_GeneratorPublicStaticFieldClassExpression_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                yield "ready";
                var C = class {
                    static value = seed + 1;
                };
                return C.value;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_AsyncPublicStaticFieldClassExpression_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            async function run(seed) {
                await 0;
                var C = class {
                    static value = seed + 2;
                };
                return C.value;
            }
            """,
            "run");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_PublicStaticAndInstanceFieldClassExpression_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield "ready";
                var C = class {
                    static seed = 41;
                    value = this.constructor.seed + 1;
                };
                return new C().value;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionPublicAccessors_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 0;
                var C = class {
                    get value() {
                        return this._value + 1;
                    }

                    set value(next) {
                        this._value = next;
                    }
                };
                var c = new C();
                c.value = 41;
                yield c.value;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact]
    public void EvaluateResumable_AsyncClassExpressionPublicAccessors_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            async function run() {
                await 0;
                var C = class {
                    get value() {
                        return this._value + 1;
                    }

                    set value(next) {
                        this._value = next;
                    }
                };
                var c = new C();
                c.value = 41;
                return c.value;
            }
            """,
            "run");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LoadClassLiteral);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionPublicInstanceFields_RoutesResumableAndInitializesFields()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield 0;
                var C = class {
                    first = 2;
                    empty;
                    second = this.first + 1;
                    constructor(value) {
                        this.ctor = value;
                        this.order = this.first + ":" + this.second;
                    }
                };
                var c = new C(7);
                yield c.first + "|" + c.empty + "|" + c.second + "|" + c.ctor + "|" + c.order;
            }

            var it = g();
            var beforeClass = it.next().value;
            var afterClass = it.next().value;
            beforeClass + "|" + afterClass;
            """);

        Assert.Equal("0|2|undefined|3|7|2:3", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorPublicStaticFieldClassExpression_RoutesResumableAndReadsClosure()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var C = class {
                    static value = seed + 1;
                };
                return C.value;
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncPublicStaticFieldClassExpression_RoutesResumableAndReadsClosure()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            async function run(seed) {
                await 0;
                var C = class {
                    static value = seed + 2;
                };
                return C.value;
            }

            run(40).then(value => output = value);
            output;
            """);

        Assert.Equal(42d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=run argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorPublicStaticAndInstanceFieldClassExpression_RoutesResumableAndInitializesBoth()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                var C = class {
                    static seed = 41;
                    static label = "box";
                    value = this.constructor.seed + 1;
                    name = this.constructor.label;
                };
                var c = new C();
                return C.seed + "|" + C.label + "|" + c.value + "|" + c.name;
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|41|box|42|box:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassExpressionPublicAccessors_RoutesResumableAndPreservesDescriptorAndReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                var C = class {
                    get value() {
                        return this._value + 1;
                    }

                    set value(next) {
                        this._value = next;
                    }
                };
                var descriptor = Object.getOwnPropertyDescriptor(C.prototype, "value");
                var receiver = { _value: 0 };
                descriptor.set.call(receiver, 41);
                return descriptor.get.call(receiver) + "|" +
                    descriptor.enumerable + "|" +
                    descriptor.configurable + "|" +
                    (descriptor.get !== undefined) + "|" +
                    (descriptor.set !== undefined);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|false|true|true|true:true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncClassExpressionPublicAccessors_RoutesResumableAndPreservesDescriptorAndReceiver()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            async function run() {
                await 0;
                var C = class {
                    get value() {
                        return this._value + 1;
                    }

                    set value(next) {
                        this._value = next;
                    }
                };
                var descriptor = Object.getOwnPropertyDescriptor(C.prototype, "value");
                var receiver = { _value: 0 };
                descriptor.set.call(receiver, 41);
                return descriptor.get.call(receiver) + "|" +
                    descriptor.enumerable + "|" +
                    descriptor.configurable + "|" +
                    (descriptor.get !== undefined) + "|" +
                    (descriptor.set !== undefined);
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal("42|false|true|true|true", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=run argc=0",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""
        function* g(seed) {
            yield class {
                field = seed;
            };
        }
        """)]
    [InlineData("""
        function* g(key) {
            yield class {
                [key] = 1;
                field = 2;
            };
        }
        """)]
    [InlineData("""
        function* g() {
            yield class {
                #value = 1;
                field = 2;
            };
        }
        """)]
    public void EvaluateResumable_ClassExpressionNonB24bShapes_Decline(string source)
    {
        var plan = GetFunctionPlan(source, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible, source);
    }

    [Fact]
    public void EvaluateResumable_ClassExpressionComputedPublicAccessor_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        function* g(key) {
            yield 1;
            var C = class {
                get [key]() { return 1; }
            };
            return C;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionStaticPublicAccessor_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        function* g() {
            yield 1;
            var C = class {
                static get value() { return 1; }
            };
            return C.value;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionCapturingPublicAccessor_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        function* g(seed) {
            yield 1;
            var C = class {
                get value() { return seed; }
            };
            return new C().value;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionPublicAccessorWithSuper_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        function* g() {
            yield 1;
            var C = class extends Array {
                get value() { return super.length; }
            };
            return new C().value;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionMixedPublicFieldAndPublicAccessor_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        function* g() {
            yield 1;
            var C = class {
                field = 1;
                get value() { return 2; }
            };
            return C;
        }
        """);

    [Fact]
    public void EvaluateResumable_ClassExpressionMixedPrivateFieldAndPublicAccessor_DeclinesBeforeVm() =>
        AssertClassExpressionDeclines("""
        function* g() {
            yield 1;
            var C = class {
                #value = 1;
                get value() { return this.#value; }
            };
            return C;
        }
        """);

    private static void AssertClassExpressionDeclines(string source)
    {
        var plan = GetFunctionPlan(source, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible, source);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("B24", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""
        function* g() {
            yield 1;
            var C = class {
                static { this.value = 1; }
            };
            return C.value;
        }
        """)]
    [InlineData("""
        function* g(name) {
            yield 1;
            var C = class {
                static [name] = 1;
            };
            return C[name];
        }
        """)]
    [InlineData("""
        function* g() {
            yield 1;
            var C = class {
                static #value = 1;
            };
            return C;
        }
        """)]
    [InlineData("""
        function* g() {
            yield 1;
            var C = class {
                static get value() { return 1; }
            };
            return C.value;
        }
        """)]
    [InlineData("""
        function* g(seed) {
            yield 1;
            var C = class {
                static value = () => seed;
            };
            return C.value();
        }
        """)]
    [InlineData("""
        function* g(seed) {
            yield 1;
            var C = class {
                static value = { read() { return seed; } };
            };
            return C.value.read();
        }
        """)]
    [InlineData("""
        function* g(seed) {
            yield 1;
            var C = class {
                static value = { get read() { return seed; } };
            };
            return C.value.read;
        }
        """)]
    [InlineData("""
        function* g(seed) {
            yield 1;
            var C = class {
                static value = class {
                    static read() { return seed; }
                };
            };
            return C.value.read();
        }
        """)]
    public void EvaluateResumable_UnownedClassExpressionShapes_DeclineBeforeVm(string source)
    {
        var plan = GetFunctionPlan(source, "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("B24", result.Reason, StringComparison.Ordinal);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    [Fact(Timeout = 5000)]
    public async Task GeneratorStaticFieldClosureInitializer_FallsBackAndObservesLaterActivationMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                var C = class {
                    static read = () => current;
                };
                current = seed + 1;
                return C.read();
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func=g argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorStaticFieldObjectMethodInitializer_FallsBackAndObservesLaterActivationMutation()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield "ready";
                var current = seed;
                var C = class {
                    static bag = { read() { return current; } };
                };
                current = seed + 1;
                return C.bag.read();
            }

            var iterator = g(41);
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42:true", result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func=g argc=1",
                StringComparison.Ordinal));
    }

    private static ExecutionPlan GetFunctionPlan(string source, string functionName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is FunctionDeclaration f && f.Name?.Name == functionName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}
