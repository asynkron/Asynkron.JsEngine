using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableDestructuringTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    [Fact]
    public void EvaluateResumable_ArrayDestructuringAfterYield_AdmitsArrayDestructuringOpcodes()
    {
        var plan = GetFunctionPlan("""
            function* g(items) {
                yield 1;
                var [a, b] = items;
                yield a + b;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringInit);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringElement);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ArrayDestructuringClose);
    }

    [Fact]
    public void EvaluateResumable_ObjectDestructuringAfterYield_AdmitsObjectDestructuringOpcodes()
    {
        var plan = GetFunctionPlan("""
            function* g(source) {
                yield 1;
                var { a, b } = source;
                yield a + b;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ObjectDestructuringInit);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ObjectDestructuringProperty);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ObjectDestructuringClose);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorArrayDestructuringAfterYield_RoutesResumableAndReadsValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(items) {
                yield 1;
                var [a, b] = items;
                yield a + b;
            }

            var iterator = g([2, 3]);
            iterator.next().value + "|" + iterator.next().value + "|" + iterator.next().done;
            """);

        Assert.Equal("1|5|true", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func=g argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorObjectDestructuringAfterYield_RoutesResumableAndReadsValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(source) {
                yield 1;
                var { a, b } = source;
                yield a + b;
            }

            var iterator = g({ a: 4, b: 5 });
            iterator.next().value + "|" + iterator.next().value + "|" + iterator.next().done;
            """);

        Assert.Equal("1|9|true", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func=g argc=1",
                StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncDestructuringAfterAwait_RoutesResumableAndReadsValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(items, source) {
                await Promise.resolve();
                var [a, b] = items;
                var { c, d } = source;
                return a + b + c + d;
            }

            run([1, 2], { c: 3, d: 4 })
                .then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(10.0, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=run argc=2",
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
