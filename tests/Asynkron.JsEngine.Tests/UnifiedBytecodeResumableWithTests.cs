using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableWithTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    [Fact]
    public void EvaluateResumable_WithBodyAcrossYields_AdmitsEnterAndLeaveWith()
    {
        var plan = GetFunctionPlan("""
            function* g(obj) {
                yield "ready";
                with (obj) {
                    yield value;
                    value = value + 1;
                    yield read();
                }

                yield typeof value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnterWith);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.LeaveWith);
    }

    [Fact]
    public void EvaluateResumable_WithObjectParameterOnlyUse_AdmitsCurrentEnvironment()
    {
        var plan = GetFunctionPlan("""
            function* g(o) {
                yield 0;
                with (o) {
                    yield value;
                }

                yield typeof value;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.EnterWith);
        Assert.Contains("o", result.Program.SlotNames);
        Assert.Contains(result.Program.ParameterSlotIndices, static slot => slot >= 0);
    }

    [Fact]
    public void ScriptPlan_WithNestedGeneratorWith_DoesNotInlineGeneratorBody()
    {
        var plan = GetScriptPlan("""
            function* g(o) {
                yield 0;
                with (o) {
                    yield value;
                }

                yield typeof value;
            }

            var it = g({ value: 3 });
            """);

        Assert.DoesNotContain(plan.Instructions, static instruction => instruction is EnterWithInstruction);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorWithBodyAcrossYields_RoutesResumableAndKeepsWithScope()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(obj) {
                yield "ready";
                with (obj) {
                    yield value;
                    value = value + 1;
                    yield read();
                }
            }

            var obj = {
                value: 4,
                read: function () { return this.value; }
            };

            var it = g(obj);
            var first = it.next().value;
            var second = it.next().value;
            var third = it.next().value;
            var fourth = it.next().value;
            first + "|" + second + "|" + third + "|" + fourth + "|" + obj.value;
            """);

        Assert.Equal("ready|4|5|undefined|5", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorWithObjectParameterOnlyUsedByWith_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                yield 0;
                with (o) {
                    yield value;
                }

                yield typeof value;
            }

            var it = g({ value: 3 });
            var first = it.next().value;
            var second = it.next().value;
            var third = it.next().value;
            first + "|" + second + "|" + third;
            """);

        Assert.Equal("0|3|undefined", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncFunctionWithBodyAfterAwait_RoutesResumableAndKeepsWithScope()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(obj) {
                await Promise.resolve(0);
                var result;
                with (obj) {
                    value = value + 2;
                    result = read();
                }

                return result + "|" + typeof value;
            }

            var obj = {
                value: 4,
                read: function () { return this.value; }
            };

            run(obj).then(value => asyncResult = value + "|" + obj.value);
            asyncResult;
            """);

        Assert.Equal("6|undefined|6", result);
        AssertAsyncFastPath("run", argc: 1);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

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

    private static ExecutionPlan GetScriptPlan(string source)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var cache = ((IAstCacheable<ScriptPlanCache>)pipeline.Analyzed).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}
