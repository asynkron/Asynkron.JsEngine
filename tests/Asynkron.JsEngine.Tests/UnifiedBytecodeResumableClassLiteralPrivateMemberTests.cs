using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableClassLiteralPrivateMemberTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    [Fact]
    public void EvaluateResumable_PrivateMethodClassLiteral_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g(seed) {
                yield 0;
                const C = class {
                    constructor(value) {
                        this.value = this.#double(value);
                    }

                    #double(value) {
                        return value * 2;
                    }
                };
                yield new C(seed).value;
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
    public void EvaluateResumable_PrivateAccessorClassLiteral_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            async function run(seed, gate) {
                await gate;
                const C = class {
                    constructor(value) {
                        this.#boxed = value;
                        this.value = this.#boxed;
                    }

                    get #boxed() {
                        return this.storage + 1;
                    }

                    set #boxed(value) {
                        this.storage = value * 2;
                    }
                };
                return new C(seed).value;
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
    public void EvaluateResumable_PrivateFieldClassLiteral_AdmitsLoadClassLiteral()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield 0;
                const C = class {
                    #value = 1;
                    constructor() {
                        this.value = this.#value;
                    }
                };
                yield new C().value;
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
    public void EvaluateResumable_PrivateMethodCapturingLocal_StillDeclines()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                var n = 1;
                const C = class {
                    constructor() {
                        this.value = this.#read();
                    }

                    #read() {
                        return n;
                    }
                };
                yield new C().value;
                n = 2;
                yield new C().value;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains(
            "private member body captures activation binding 'n'",
            result.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_PrivateAccessorCapturingLocal_StillDeclines()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                var n = 1;
                const C = class {
                    get #read() {
                        return n;
                    }

                    constructor() {
                        this.value = this.#read;
                    }
                };
                yield new C().value;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains(
            "private member body captures activation binding 'n'",
            result.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_PrivateMemberClassConstructorCapturingLocal_StillDeclines()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                var n = 1;
                const C = class {
                    constructor() {
                        this.value = n;
                    }

                    #tag() {
                        return 0;
                    }
                };
                yield new C().value;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains(
            "constructor body captures activation binding 'n'",
            result.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_ExtendsClassLiteral_StillDeclines()
    {
        var plan = GetFunctionPlan("""
            function* g(Base) {
                yield 0;
                const C = class extends Base {
                    #m() {
                        return 1;
                    }
                };
                yield new C();
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Contains("extends", result.Reason, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorPrivateMethodClassLiteral_RoutesResumableAndCallsPrivateMethod()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(seed) {
                yield 0;
                const C = class {
                    constructor(value) {
                        this.value = this.#double(value);
                    }

                    #double(value) {
                        return value * 2;
                    }
                };
                yield new C(seed).value;
            }

            const it = g(6);
            const first = it.next().value;
            const second = it.next().value;
            first + "|" + second;
            """);

        Assert.Equal("0|12", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncPrivateAccessorClassLiteral_RoutesResumableAndUsesGetterSetter()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var done = undefined;
            async function run(seed, gate) {
                await gate;
                const C = class {
                    constructor(value) {
                        this.#boxed = value;
                        this.value = this.#boxed;
                    }

                    get #boxed() {
                        return this.storage + 1;
                    }

                    set #boxed(value) {
                        this.storage = value * 2;
                    }
                };
                return new C(seed).value;
            }

            run(4, Promise.resolve(0)).then(value => done = "" + value);
            done;
            """);

        Assert.Equal("9", result);
        AssertAsyncFastPath("run", argc: 2);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorPrivateMethodCapturesLocalAcrossYields_CorrectButDeclinesToRunner()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(){
                var n = 1;
                const C = class {
                    constructor() { this.value = this.#read(); }
                    #read() { return n; }
                };
                yield new C().value;
                n = 2;
                yield new C().value;
            }
            const it = g();
            const a = it.next().value;
            const b = it.next().value;
            a + "|" + b;
            """);

        Assert.Equal("1|2", result);
        AssertGeneratorNotRouted();
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private void AssertGeneratorNotRouted() =>
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal));

    private void AssertAsyncFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

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
