using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for resumable assignment destructuring through <see cref="UnifiedBytecodeOpCode.ApplyBindingTarget" />.
///     This keeps the bounded binding-target bridge explicit: the VM owns routing and operand state, then
///     applies the lowered <see cref="BindingTargetProgram" /> against the materialized activation environment.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableAssignmentDestructuringTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    [Fact]
    public void EvaluateResumable_AssignmentDestructuringAfterYield_AdmitsApplyBindingTarget()
    {
        var plan = GetFunctionPlan("""
            function* g(source, key) {
                var value = 0;
                yield 1;
                ({ [key]: value = 5 } = source);
                yield value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.ApplyBindingTarget);
        Assert.NotEmpty(result.Program.BindingTargetConstants);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorAssignmentDestructuringAfterYield_RoutesResumableAndWritesSlot()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(source, key) {
                var value = 0;
                yield 1;
                ({ [key]: value = 5 } = source);
                yield value;
            }

            var it = g({ chosen: 42 }, "chosen");
            var first = it.next().value;
            var second = it.next().value;
            first + "|" + second;
            """);

        Assert.Equal("1|42", result);
        AssertGeneratorFastPath("g", argc: 2);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorAssignmentDestructuringYieldedSource_RoutesResumableAndUsesDefault()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(key) {
                var value = 0;
                ({ [key]: value = 5 } = yield 1);
                yield value;
            }

            var it = g("missing");
            var first = it.next().value;
            var second = it.next({}).value;
            first + "|" + second;
            """);

        Assert.Equal("1|5", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncAssignmentDestructuringAfterAwait_RoutesResumableAndWritesSlot()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(source, key, gate) {
                var value = 0;
                await gate;
                ({ [key]: value = 5 } = source);
                return value;
            }

            run({ chosen: 11 }, "chosen", Promise.resolve(0)).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(11d, result);
        AssertAsyncFastPath("run", argc: 3);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal) &&
                      record.Message.Contains($"func={functionName}", StringComparison.Ordinal) &&
                      record.Message.Contains($"argc={argc}", StringComparison.Ordinal));

    private void AssertAsyncFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableAsyncFastPathLog, StringComparison.Ordinal) &&
                      record.Message.Contains($"func={functionName}", StringComparison.Ordinal) &&
                      record.Message.Contains($"argc={argc}", StringComparison.Ordinal));

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
