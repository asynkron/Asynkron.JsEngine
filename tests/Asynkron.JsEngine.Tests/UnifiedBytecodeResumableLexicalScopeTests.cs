using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableLexicalScopeTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";

    [Fact]
    public void EvaluateResumable_BlockScopedLetAcrossYield_AdmitsPushEnvironment()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                {
                    let x = 1;
                    yield x;
                    x = 2;
                    yield x;
                }
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PushEnvironment);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PopEnvironment);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.Yield);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorBlockScopedLet_RoutesResumableAndPreservesSlotAcrossYield()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                {
                    let x = 1;
                    yield x;
                    x = 2;
                    yield x;
                }
            }

            var iterator = g();
            iterator.next().value + "|" + iterator.next().value + "|" + iterator.next().done;
            """);

        Assert.Equal("1|2|true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorBlockScopedConstAssignment_RoutesResumableAndThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                {
                    const x = 1;
                    yield x;
                    x = 2;
                }
            }

            var iterator = g();
            var first = iterator.next().value;
            var caught = "none";
            try {
                iterator.next();
            } catch (e) {
                caught = e.name;
            }

            first + "|" + caught + "|" + iterator.next().done;
            """);

        Assert.Equal("1|TypeError|true", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    [Fact(Timeout = 5000)]
    public async Task GeneratorForOfLet_RoutesResumableAndCopiesPerIterationSlot()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* values(items) {
                for (let value of items) {
                    yield value;
                }
            }

            var iterator = values([4, 5]);
            iterator.next().value + "|" + iterator.next().value + "|" + iterator.next().done;
            """);

        Assert.Equal("4|5|true", result);
        AssertGeneratorFastPath("values", argc: 1);
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
