using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
[Category(TestCategories.Performance)]
[Trait("Category", "IrLowering")]
public sealed class StatementInstructionStorageDiagnosticsTests : IAsyncLifetime
{
    private JsEngine _engine = null!;

    public Task InitializeAsync()
    {
        _engine = new JsEngine();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _engine.DisposeAsync();
    }

    [Fact]
    public async Task Collect_ForRepresentativeLoweredProgram_ReportsHistogramAndCompactEstimate()
    {
        var parsedProgram = _engine.ParseProgram("""
            function walk(limit) {
                let total = 0;
                for (let i = 0; i < limit; i++) {
                    if (i > 5) break;
                    total = total + i;
                }
                return total;
            }

            walk(8);
            """);
        await _engine.Evaluate(parsedProgram);

        var snapshot = StatementInstructionStorageDiagnostics.Collect(parsedProgram);

        Assert.True(snapshot.PlanCount > 0);
        Assert.True(snapshot.InstructionCount > 0);
        Assert.True(snapshot.SupportedInstructionCount > 0);
        Assert.True(snapshot.UnsupportedInstructionCount > 0);
        Assert.True(snapshot.EstimatedCompactEncodedBytes > 0);
        Assert.NotEmpty(snapshot.InstructionKindHistogram);
        Assert.NotEmpty(snapshot.SupportedInstructionKindHistogram);
        Assert.NotEmpty(snapshot.UnsupportedInstructionKindHistogram);
        Assert.Contains(
            snapshot.SupportedInstructionKindHistogram,
            entry => entry.Key is InstructionKind.SetCompletionValue or InstructionKind.Break or InstructionKind.BreakableExit);
        Assert.Contains(snapshot.UnsupportedInstructionKindHistogram, entry => entry.Key == InstructionKind.PushEnvironment);
    }

    [Fact]
    public void Collect_ForManuallyConstructedSimpleFamilyPlan_MatchesExpectedEncodedByteEstimate()
    {
        var instructions = new ExecutionInstruction[]
        {
            new JumpInstruction(4),
            new SetCompletionValueInstruction(2),
            new BreakInstruction(9, 2),
            new ContinueInstruction(12, 2),
            new PopEnvironmentInstruction(ScopeId: 5, AllowPooling: true, Next: 3),
            new LeaveTryInstruction(6),
            new EndFinallyInstruction(7)
        };

        var plan = new ExecutionPlan(instructions.ToImmutableArray(), EntryPoint: 0);
        var snapshot = StatementInstructionStorageDiagnostics.Collect(plan);

        Assert.Equal(1, snapshot.PlanCount);
        Assert.Equal(instructions.Length, snapshot.InstructionCount);
        Assert.Equal(4, snapshot.SupportedInstructionCount);
        Assert.Equal(3, snapshot.UnsupportedInstructionCount);
        Assert.Equal(64, snapshot.EstimatedCompactEncodedBytes);
    }

    [Fact]
    public async Task Collect_PreservesExecutionPlanInstructionRuntimeShape()
    {
        var parsedProgram = _engine.ParseProgram("""
            function sample(value) {
                return value + 1;
            }
            """);
        await _engine.Evaluate(parsedProgram);
        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(parsedProgram.Body));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        var plan = Assert.IsType<ExecutionPlan>(cache.Plan);
        var baselineInstructionCount = plan.Instructions.Length;

        _ = StatementInstructionStorageDiagnostics.Collect(parsedProgram);

        var planAfter = Assert.IsType<ExecutionPlan>(cache.Plan);
        Assert.Same(plan, planAfter);
        Assert.Equal(baselineInstructionCount, planAfter.Instructions.Length);
    }

    [Fact]
    public async Task Collect_FromFunctionPlan_TraversesNestedDeclarationPlans()
    {
        var parsedProgram = _engine.ParseProgram("""
            function outer() {
                function inner() {
                    return 1;
                }

                class Local {
                    method() {
                        return inner();
                    }
                }

                return inner();
            }
            """);
        await _engine.Evaluate(parsedProgram);

        var outerDeclaration = Assert.IsType<FunctionDeclaration>(Assert.Single(parsedProgram.Body));
        var outerCache = ((IAstCacheable<ExecutionPlanCache>)outerDeclaration.Function).GetOrCreateCache();
        var outerPlan = Assert.IsType<ExecutionPlan>(outerCache.Plan);

        var fromPlan = StatementInstructionStorageDiagnostics.Collect(outerPlan);

        Assert.True(fromPlan.PlanCount >= 3);
        Assert.Contains(fromPlan.InstructionKindHistogram, entry => entry.Key == InstructionKind.FunctionDeclaration);
        Assert.Contains(fromPlan.InstructionKindHistogram, entry => entry.Key == InstructionKind.ClassDeclaration);
        Assert.Contains(fromPlan.InstructionKindHistogram, entry => entry.Key == InstructionKind.Return);
    }
}
