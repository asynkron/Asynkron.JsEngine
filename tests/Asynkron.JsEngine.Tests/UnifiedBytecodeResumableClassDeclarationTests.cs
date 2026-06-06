using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for the narrow B36 direct root class-declaration slice in resumable bodies.
///     Simple class declarations can route through <see cref="UnifiedBytecodeOpCode.DeclareClass" />;
///     activation-safe computed public class declarations can route through the same instruction,
///     while unsafe neighboring class-definition state stays declined before VM execution.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableClassDeclarationTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";
    private const string ResumableAsyncGeneratorFastPathLog =
        "unified-bytecode-resumable-async-generator-fast-path";

    [Fact]
    public void EvaluateResumable_ClassDeclaration_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                yield "ready";
                class Box {
                    constructor(value) {
                        this.value = value;
                    }
                }
                var box = new Box(7);
                yield box.value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationComputedPublicElements_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(key) {
                yield "ready";
                class Box {
                    [key = "value"]() {
                        return 42;
                    }

                    static ["seed"] = 7;
                }
                var box = new Box();
                yield box.value() + Box.seed;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationComputedNameActivationCall_DeclinesBeforeVm()
    {
        var plan = GetFunctionPlan("""
            function* g(read) {
                yield "ready";
                class Box {
                    [read()]() {
                        return 1;
                    }
                }
                yield typeof Box;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("not supported by B24h", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationComputedNameActivationDelete_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            function* g(key) {
                yield "ready";
                class Box {
                    [delete key]() {
                        return 42;
                    }
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationExtends_AdmitsDeclareClass()
    {
        var plan = GetFunctionPlan("""
            class Base {}
            function* g() {
                yield "ready";
                class Box extends Base {
                }
                yield typeof Box;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.DeclareClass);
    }

    [Fact]
    public void EvaluateResumable_ClassDeclarationActivationExtends_DeclinesBeforeVm()
    {
        var plan = GetFunctionPlan("""
            function* g(Base) {
                yield "ready";
                class Box extends Base {
                }
                yield typeof Box;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("superclass captures activation binding 'Base'", result.Reason, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclaration_RoutesResumableAndKeepsBodyScope()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                yield "ready";
                class Box {
                    constructor(value) {
                        this.value = value;
                    }
                }
                var box = new Box(7);
                yield box.value;
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            var outside = typeof Box;
            first.value + ":" + first.done + "|" +
                second.value + ":" + second.done + "|" +
                outside;
            """);

        Assert.Equal("ready:false|7:false|undefined", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicClassDeclaration_RoutesResumableAndSyncsName()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(key) {
                yield "ready";
                class Box {
                    [key = "value"]() {
                        return 42;
                    }

                    static ["seed"] = 7;
                }
                var box = new Box();
                yield key + "|" + box.value() + "|" + Box.seed;
            }

            var iterator = g("initial");
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|value|42|7:false", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedPublicClassDeclarationActivationDelete_RejectsStrictIdentifierDelete()
    {
        await using var engine = CreateEngine();
        var exception = await Assert.ThrowsAsync<ThrowSignal>(async () => await engine.Evaluate("""
            function* g(key) {
                yield "ready";
                class Box {
                    [delete key]() {
                        return key;
                    }
                }
                var box = new Box();
                yield box.false();
            }

            var iterator = g("value");
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """));

        Assert.Contains("SyntaxError", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Delete of an unqualified identifier", exception.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorClassDeclarationExtends_RoutesResumableAndPreservesSuperclass()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class Base {
                constructor(seed) {
                    this.seed = seed;
                }

                read() {
                    return this.seed + 1;
                }
            }

            function* g() {
                yield "ready";
                class Box extends Base {
                }
                var box = new Box(41);
                yield box.read() + "|" + (box instanceof Base);
            }

            var iterator = g();
            var first = iterator.next();
            var second = iterator.next();
            first.value + ":" + first.done + "|" + second.value + ":" + second.done;
            """);

        Assert.Equal("ready:false|42|true:false", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncClassDeclarationAfterAwait_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            async function run(seed) {
                await 0;
                class Box {
                    constructor(value) {
                        this.value = value;
                    }
                }
                var box = new Box(seed);
                return box.value;
            }

            run(5).then(value => output = value);
            output;
            """);

        Assert.Equal(5d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=run argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorClassDeclaration_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;

            async function* values(seed) {
                yield "ready";
                class Box {
                    constructor(value) {
                        this.value = value;
                    }
                }
                var box = new Box(seed);
                yield box.value;
            }

            async function run() {
                var iterator = values(8);
                var first = await iterator.next();
                var second = await iterator.next();
                var third = await iterator.next();
                return first.value + ":" + first.done + "|" +
                    second.value + ":" + second.done + "|" +
                    String(third.value) + ":" + third.done;
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal("ready:false|8:false|undefined:true", result?.ToString());
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=values argc=1",
                StringComparison.Ordinal));
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
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
