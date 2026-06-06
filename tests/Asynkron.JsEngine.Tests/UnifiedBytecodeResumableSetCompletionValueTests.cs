using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for admitting <see cref="SetCompletionValueInstruction" /> in resumable bodies.
///     In function/generator bodies this instruction is completion bookkeeping that compiles to a
///     VM-owned jump path because there is no script-completion slot.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableSetCompletionValueTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    [Fact]
    public void EvaluateResumable_IfWithoutElse_AdmitsSetCompletionValue()
    {
        var plan = GetFunctionPlan("""
            function* g(flag) {
                if (flag) {
                    yield "then";
                }
                yield "after";
            }
            """,
            "g");

        Assert.Contains(plan.Instructions, static instruction => instruction is SetCompletionValueInstruction);

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.JumpIfFalse);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorIfWithoutElse_RoutesResumableOnBothBranches()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(flag) {
                if (flag) {
                    yield "then";
                }
                yield "after";
            }

            var yes = g(true);
            var yesFirst = yes.next();
            var yesSecond = yes.next();
            var yesThird = yes.next();

            var no = g(false);
            var noFirst = no.next();
            var noSecond = no.next();

            yesFirst.value + ":" + yesFirst.done + "|" +
                yesSecond.value + ":" + yesSecond.done + "|" +
                String(yesThird.value) + ":" + yesThird.done + "|" +
                noFirst.value + ":" + noFirst.done + "|" +
                String(noSecond.value) + ":" + noSecond.done;
            """);

        Assert.Equal("then:false|after:false|undefined:true|after:false|undefined:true", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    [Fact(Timeout = 5000)]
    public async Task AsyncIfWithoutElseAndAwait_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var output = undefined;
            async function run(flag, promise) {
                if (flag) {
                    await promise;
                }
                return flag;
            }

            run(true, Promise.resolve(0)).then(value => output = String(value));
            output;
            """);

        Assert.Equal("true", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            static record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func=run argc=2",
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
