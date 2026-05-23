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
        Assert.True(snapshot.OwnerBackedEncodedBytes > 0);
        Assert.True(snapshot.OperandTableEntryCount > 0);
        Assert.True(snapshot.ExtraOperandTableEntryCount >= 0);
        Assert.True(snapshot.ExpressionReferenceCount >= 0);
        Assert.True(snapshot.SecondaryExpressionReferenceCount >= 0);
        Assert.True(snapshot.SymbolOperandCount >= 0);
        Assert.True(snapshot.BindingTargetOperandCount >= 0);
        Assert.True(snapshot.ExpressionProgramReferenceTableCount >= 0);
        Assert.True(snapshot.EstimatedCompactEncodedBytes > 0);
        Assert.NotEmpty(snapshot.InstructionKindHistogram);
        Assert.NotEmpty(snapshot.SupportedInstructionKindHistogram);
        Assert.NotEmpty(snapshot.UnsupportedInstructionKindHistogram);
        Assert.NotEmpty(snapshot.UnsupportedFamilyReasonHistogram);
        Assert.Contains(
            snapshot.SupportedInstructionKindHistogram,
            entry => entry.Key is InstructionKind.SetCompletionValue or InstructionKind.Break or InstructionKind.BreakableExit);
        Assert.Contains(snapshot.UnsupportedInstructionKindHistogram, entry => entry.Key == InstructionKind.PushEnvironment);
        Assert.Contains(snapshot.UnsupportedFamilyReasonHistogram, entry => entry.Key == "declaration-and-scope");
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
        Assert.Equal(64, snapshot.OwnerBackedEncodedBytes);
        Assert.Equal(4, snapshot.OperandTableEntryCount);
        Assert.Equal(0, snapshot.ExtraOperandTableEntryCount);
        Assert.Equal(0, snapshot.ExpressionReferenceCount);
        Assert.Equal(0, snapshot.SecondaryExpressionReferenceCount);
        Assert.Equal(0, snapshot.SymbolOperandCount);
        Assert.Equal(0, snapshot.BindingTargetOperandCount);
        Assert.Equal(0, snapshot.ExpressionProgramReferenceTableCount);
        Assert.Equal(64, snapshot.EstimatedCompactEncodedBytes);
        Assert.Contains(snapshot.UnsupportedFamilyReasonHistogram, entry => entry.Key == "declaration-and-scope" && entry.Value == 1);
        Assert.Contains(snapshot.UnsupportedFamilyReasonHistogram, entry => entry.Key == "suspend-and-exception-flow" && entry.Value == 2);
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
    public async Task Build_PopulatesPureControlFlowCompactSidecar_WithoutMutatingInstructionRuntimeShape()
    {
        var parsedProgram = _engine.ParseProgram("""
            function sample(limit) {
                let total = 0;
                if (limit > 1) {
                    total = total + limit;
                } else {
                    total = total + 1;
                }
                return total;
            }
            """);
        await _engine.Evaluate(parsedProgram);
        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(parsedProgram.Body));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        var plan = Assert.IsType<ExecutionPlan>(cache.Plan);
        var baselineInstructions = plan.Instructions;

        Assert.NotNull(plan.CompactStatementStorageBoundary);
        Assert.All(
            plan.CompactStatementStorageBoundary!.SupportedKindClassifications,
            entry => Assert.True(
                entry.PayloadGroup is CompactStatementPayloadGroup.ControlFlowNoPayload or
                    CompactStatementPayloadGroup.CompletionControl));
        Assert.DoesNotContain(
            plan.CompactStatementStorageBoundary.SupportedKindClassifications,
            entry => entry.Kind == InstructionKind.Return);
        Assert.Equal(baselineInstructions, plan.Instructions);
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

    [Fact]
    public void Collect_ReusedExpressionPrograms_UsesStableReferenceTableIds()
    {
        var sharedProgram = ExpressionProgram.Empty;
        var instructions = new ExecutionInstruction[]
        {
            new EvaluateAndDiscardInstruction(1, sharedProgram, SuppressCompletionValue: false),
            new AwaitAndDiscardInstruction(2, Symbol.Intern("await"), sharedProgram, SuppressCompletionValue: false),
            new ReturnInstruction(3, sharedProgram, Symbol.Intern("await"), sharedProgram)
        };

        var plan = new ExecutionPlan(instructions.ToImmutableArray(), EntryPoint: 0);
        var snapshot = StatementInstructionStorageDiagnostics.Collect(plan);

        Assert.Equal(1, snapshot.ExpressionProgramReferenceTableCount);
        Assert.True(snapshot.ExpressionReferenceCount >= 3);
        Assert.True(snapshot.SecondaryExpressionReferenceCount >= 1);
    }

    [Fact]
    public void CompactStatementStorageBoundary_ForMixedInstructionKinds_SeparatesSupportedAndDeferredFamilies()
    {
        var instructions = new ExecutionInstruction[]
        {
            new JumpInstruction(4),
            new ReturnInstruction(8, ReturnProgram: ExpressionProgram.Empty),
            new PopEnvironmentInstruction(ScopeId: 2, AllowPooling: false, Next: 9),
            new BranchInstruction(ConditionProgram: ExpressionProgram.Empty, ConsequentIndex: 3, AlternateIndex: 5)
        };

        var boundary = CompactStatementStorage.CreateBoundary(instructions);

        Assert.Equal(2, boundary.Storage.InstructionCount);
        Assert.Equal(2, boundary.Storage.DecodeSemanticView().Length);
        Assert.Contains(
            boundary.SupportedKindClassifications,
            entry => entry.Kind == InstructionKind.Jump &&
                     entry.PayloadGroup == CompactStatementPayloadGroup.ControlFlowNoPayload &&
                     entry.IsSupported);
        Assert.Contains(
            boundary.SupportedKindClassifications,
            entry => entry.Kind == InstructionKind.Return &&
                     entry.PayloadGroup == CompactStatementPayloadGroup.CompletionValueWithOptionalAwait &&
                     entry.IsSupported);
        Assert.Contains(
            boundary.DeferredKindClassifications,
            entry => entry.Kind == InstructionKind.PopEnvironment &&
                     entry.PayloadGroup == CompactStatementPayloadGroup.DeferredDeclarationAndScope &&
                     !entry.IsSupported);
        Assert.Contains(
            boundary.DeferredKindClassifications,
            entry => entry.Kind == InstructionKind.Branch &&
                     entry.PayloadGroup == CompactStatementPayloadGroup.DeferredBranching &&
                     !entry.IsSupported);
    }

    [Fact]
    public void CompactStatementStorageBoundary_StoresExpressionProgramsAsReferencesOutsideOpcodeStream()
    {
        var expressionProgram = ExpressionProgram.Empty;
        var instructions = new ExecutionInstruction[]
        {
            new EvaluateAndDiscardInstruction(1, expressionProgram),
            new ReturnInstruction(2, ReturnProgram: expressionProgram)
        };

        var boundary = CompactStatementStorage.CreateBoundary(instructions);
        var storage = boundary.Storage;

        Assert.Equal(2, storage.InstructionCount);
        Assert.Equal(2, storage.OpcodeStream.Length);
        Assert.Equal(2, storage.ReferenceTables.ExpressionPrograms.Length);
        Assert.All(storage.ReferenceTables.ExpressionPrograms, program => Assert.Equal(expressionProgram, program));
    }
}
