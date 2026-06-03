using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for the first production async-generator route through
///     <see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />. The admitted boundary is deliberately
///     narrow: simple-parameter async generators whose body is otherwise resumable-eligible can route, while
///     async-generator <c>yield*</c> delegation remains a pre-VM decline until delegated async iterator
///     settlement is VM-owned.
/// </summary>
[Category(TestCategories.AsyncRuntime)]
[Category(TestCategories.IteratorRuntime)]
public sealed class UnifiedBytecodeAsyncGeneratorRouteTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ResumableAsyncGeneratorFastPathLog =
        "unified-bytecode-resumable-async-generator-fast-path";

    [Fact]
    public void EvaluateResumable_AsyncGeneratorSimpleYield_AdmitsYield()
    {
        var plan = GetFunctionPlan("""
            async function* values(x) {
                yield x;
            }
            """,
            "values");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true, IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Yield);
    }

    [Fact]
    public void EvaluateResumable_AsyncGeneratorYieldStar_StaysDeclined()
    {
        var plan = GetFunctionPlan("""
            async function* relay(values) {
                yield* values;
            }
            """,
            "relay");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true, IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("Async-generator yield* delegation", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateResumable_AsyncGeneratorYieldStarAwait_StaysDeclined()
    {
        var plan = GetFunctionPlan("""
            async function* relay(values) {
                yield* await values;
            }
            """,
            "relay");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsAsyncLike: true, IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains("Async-generator yield* delegation", result.Reason, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorDirectNext_RoutesResumableAndSettlesIteratorResults()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;

            async function* values(x) {
                yield x;
                yield x + 1;
                return x + 2;
            }

            async function run() {
                var iterator = values(4);
                var first = await iterator.next();
                var second = await iterator.next();
                var third = await iterator.next();
                return first.value + ":" + first.done + "|" +
                    second.value + ":" + second.done + "|" +
                    third.value + ":" + third.done;
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal("4:false|5:false|6:true", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=values argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncGeneratorYieldStar_KeepsIrRunnerRoute()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;

            async function* relay(values) {
                yield* values;
            }

            async function run() {
                var iterator = relay([1]);
                var first = await iterator.next();
                var second = await iterator.next();
                return first.value + ":" + first.done + "|" +
                    String(second.value) + ":" + second.done;
            }

            run().then(value => output = value);
            output;
            """);

        Assert.Equal("1:false|undefined:true", result);
        Assert.DoesNotContain(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncGeneratorFastPathLog} func=relay",
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
