using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Reflection;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
[Category(TestCategories.Performance)]
[Trait("Category", "IrLowering")]
public sealed class ExpressionProgramStorageDiagnosticsTests : IAsyncLifetime
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
    public async Task Collect_ForRepresentativeLoweredProgram_ReportsNonZeroStorage()
    {
        var parsedProgram = _engine.ParseProgram("""
            function compute(a, b) {
                const next = a + b;
                return next * 2;
            }

            compute(40, 2);
            """);
        await _engine.Evaluate(parsedProgram);

        var snapshot = ExpressionProgramStorageDiagnostics.Collect(parsedProgram);

        Assert.True(snapshot.ProgramCount > 0, "Expected at least one lowered expression program.");
        Assert.True(snapshot.OperationCount > 0, "Expected lowered expression operations.");
        Assert.True(snapshot.EstimatedEncodedOperationBytes > 0, "Expected non-zero estimated encoded operation storage.");
        Assert.NotEmpty(snapshot.OperationKindHistogram);
        Assert.True(snapshot.OperationsWithImmediate0Count >= 0);
        Assert.True(snapshot.OperationsWithImmediate1Count >= 0);
        Assert.True(snapshot.OperationsWithBothImmediatesCount >= 0);
        Assert.True(snapshot.OperationsWithFlagsCount >= 0);
        Assert.True(snapshot.EstimatedMaxStackSlotCount > 0);
        Assert.True(snapshot.EstimatedMaxStackValueBytes > 0);
        Assert.True(snapshot.EstimatedMaxStackFlagWordCount > 0);
        Assert.True(snapshot.EstimatedMaxStackFlagBytes > 0);
        Assert.NotEmpty(snapshot.MaxStackDepthHistogram);
    }

    [Fact]
    public void Collect_ForSingleOperandProgram_ReportsCompactEncodedOperationBytes()
    {
        var program = new ExpressionProgram(ImmutableArray.Create(
            PackedExpressionOp.LoadThis,
            PackedExpressionOp.UnaryLogicalNot));

        var snapshot = ExpressionProgramStorageDiagnostics.Collect(program);
        var legacyPackedBytes = snapshot.OperationCount * Unsafe.SizeOf<PackedExpressionOp>();

        Assert.Equal(2, snapshot.OperationCount);
        Assert.Equal(program.EstimatedEncodedOperationBytes, snapshot.EstimatedEncodedOperationBytes);
        Assert.True(
            snapshot.EstimatedEncodedOperationBytes < legacyPackedBytes,
            "Expected encoded owner storage to be smaller than the decoded PackedExpressionOp view.");
    }

    [Fact]
    public void Collect_ForDefaultAndEmptyPrograms_ReportsZeroOpsAndDepth()
    {
        var defaultSnapshot = ExpressionProgramStorageDiagnostics.Collect(default(ExpressionProgram));
        Assert.Equal(1, defaultSnapshot.ProgramCount);
        Assert.Equal(0, defaultSnapshot.OperationCount);
        Assert.Equal(0, defaultSnapshot.EstimatedEncodedOperationBytes);
        Assert.Equal(0, defaultSnapshot.OptionalOperationCount);
        Assert.Equal(0, defaultSnapshot.ShortCircuitOperationCount);
        Assert.Equal(0, defaultSnapshot.OperationsWithFlagsCount);
        Assert.Equal(0, defaultSnapshot.OperationsWithImmediate0Count);
        Assert.Equal(0, defaultSnapshot.OperationsWithImmediate1Count);
        Assert.Equal(0, defaultSnapshot.OperationsWithBothImmediatesCount);
        Assert.Equal(0, defaultSnapshot.EstimatedMaxStackSlotCount);
        Assert.Equal(0, defaultSnapshot.EstimatedMaxStackValueBytes);
        Assert.Equal(0, defaultSnapshot.EstimatedMaxStackFlagWordCount);
        Assert.Equal(0, defaultSnapshot.EstimatedMaxStackFlagBytes);
        Assert.Empty(defaultSnapshot.OperationKindHistogram);
        var defaultDepth = Assert.Single(defaultSnapshot.MaxStackDepthHistogram);
        Assert.Equal(0, defaultDepth.Key);
        Assert.Equal(1, defaultDepth.Value);

        var emptySnapshot = ExpressionProgramStorageDiagnostics.Collect(ExpressionProgram.Empty);
        Assert.Equal(1, emptySnapshot.ProgramCount);
        Assert.Equal(0, emptySnapshot.OperationCount);
        Assert.Equal(0, emptySnapshot.EstimatedEncodedOperationBytes);
        Assert.Equal(0, emptySnapshot.OptionalOperationCount);
        Assert.Equal(0, emptySnapshot.ShortCircuitOperationCount);
        Assert.Equal(0, emptySnapshot.OperationsWithFlagsCount);
        Assert.Equal(0, emptySnapshot.OperationsWithImmediate0Count);
        Assert.Equal(0, emptySnapshot.OperationsWithImmediate1Count);
        Assert.Equal(0, emptySnapshot.OperationsWithBothImmediatesCount);
        Assert.Equal(0, emptySnapshot.EstimatedMaxStackSlotCount);
        Assert.Equal(0, emptySnapshot.EstimatedMaxStackValueBytes);
        Assert.Equal(0, emptySnapshot.EstimatedMaxStackFlagWordCount);
        Assert.Equal(0, emptySnapshot.EstimatedMaxStackFlagBytes);
        Assert.Empty(emptySnapshot.OperationKindHistogram);
        var emptyDepth = Assert.Single(emptySnapshot.MaxStackDepthHistogram);
        Assert.Equal(0, emptyDepth.Key);
        Assert.Equal(1, emptyDepth.Value);
    }

    [Fact]
    public void Collect_ForCallWithCallSpecificFlags_DoesNotCountOptionalOrShortCircuit()
    {
        var program = new ExpressionProgram(
            ImmutableArray.Create(
                PackedExpressionOp.Call(ArgumentCount: 1, HasExplicitThis: true, IsDirectEval: true)));

        var snapshot = ExpressionProgramStorageDiagnostics.Collect(program);

        Assert.Equal(0, snapshot.OptionalOperationCount);
        Assert.Equal(0, snapshot.ShortCircuitOperationCount);
    }

    [Fact]
    public void Collect_ForZeroValuedImmediates_StillCountsImmediateShape()
    {
        var program = new ExpressionProgram(
            ImmutableArray.Create(
                PackedExpressionOp.LoadLiteralConstant(0),
                PackedExpressionOp.Jump(0),
                PackedExpressionOp.Call(ArgumentCount: 0, SpreadMaskConstantIndex: -1)));

        var snapshot = ExpressionProgramStorageDiagnostics.Collect(program);

        Assert.Equal(3, snapshot.OperationCount);
        Assert.Equal(3, snapshot.OperationsWithImmediate0Count);
        Assert.Equal(1, snapshot.OperationsWithImmediate1Count);
        Assert.Equal(1, snapshot.OperationsWithBothImmediatesCount);
    }

    [Fact]
    public async Task Collect_ForOptionalChain_ReportsOptionalAndShortCircuitShape()
    {
        var parsedProgram = _engine.ParseProgram("""
            function maybeGet(value) {
                return value?.child?.name;
            }
            maybeGet({ child: { name: "ok" } });
            """);

        await _engine.Evaluate(parsedProgram);
        var snapshot = ExpressionProgramStorageDiagnostics.Collect(parsedProgram);

        Assert.True(snapshot.OptionalOperationCount > 0, "Expected optional-chain operations.");
        Assert.True(snapshot.ShortCircuitOperationCount > 0, "Expected short-circuit operations.");
        Assert.Contains(
            snapshot.OperationKindHistogram,
            entry => entry.Key is ExpressionOpKind.GetNamedProperty or ExpressionOpKind.GetComputedProperty);
    }

    [Fact]
    public async Task Collect_ForSimpleDeclarationInitializer_CountsInitializerProgramStorage()
    {
        var parsedProgram = _engine.ParseProgram("""
            function declareSimple(value) {
                let next = value + 1;
                return next;
            }
            """);

        await _engine.Evaluate(parsedProgram);
        var plan = GetFunctionPlan(parsedProgram, "declareSimple");

        var declaration = Assert.Single(plan.Instructions.OfType<SimpleVariableDeclarationInstruction>(), i => i.TargetSymbol.Name == "next");
        Assert.Null(declaration.AwaitedProgram);
        var initializerProgram = Assert.NotNull(declaration.InitializerProgram);
        var initializerSnapshot = ExpressionProgramStorageDiagnostics.Collect(initializerProgram);
        Assert.True(initializerSnapshot.OperationCount > 0);

        var snapshot = ExpressionProgramStorageDiagnostics.Collect(parsedProgram);
        Assert.True(snapshot.OperationCount >= initializerSnapshot.OperationCount);
    }

    [Fact]
    public async Task Collect_ForClassStaticBlock_CountsStaticBlockProgramStorage()
    {
        var baselineProgram = _engine.ParseProgram("""
            class CounterBaseline {
                static value = 1;
            }
            """);
        await _engine.Evaluate(baselineProgram);
        var baselineSnapshot = ExpressionProgramStorageDiagnostics.Collect(baselineProgram);

        var parsedProgram = _engine.ParseProgram("""
            class Counter {
                static value = 1;
                static {
                    this.value = this.value + 41;
                }
            }
            """);

        await _engine.Evaluate(parsedProgram);

        var classDeclaration = Assert.IsType<ClassDeclaration>(Assert.Single(parsedProgram.Body));
        var cache = ((IAstCacheable<ClassDefinitionProgramCache>)classDeclaration.Definition).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Class cache should build. Failure: {cache.FailureReason}");

        var staticBlockPlan = Assert.Single(cache.Definition.StaticBlockPlans);
        Assert.NotEmpty(staticBlockPlan.Instructions);

        var snapshot = ExpressionProgramStorageDiagnostics.Collect(parsedProgram);
        Assert.True(
            snapshot.OperationCount > baselineSnapshot.OperationCount,
            "Expected static block expression bytecode to increase counted operation storage.");
    }

    [Fact]
    public async Task Collect_ForArrayDestructuringInitializer_CountsSourceProgramStorage()
    {
        var baselineProgram = _engine.ParseProgram("""
            function baseline(source) {
                return source;
            }
            """);
        await _engine.Evaluate(baselineProgram);
        var baselineSnapshot = ExpressionProgramStorageDiagnostics.Collect(baselineProgram);

        var parsedProgram = _engine.ParseProgram("""
            function destructure(source) {
                const [first] = source;
                return first;
            }
            """);
        await _engine.Evaluate(parsedProgram);

        var plan = GetFunctionPlan(parsedProgram, "destructure");
        var initInstruction = Assert.Single(plan.Instructions.OfType<ArrayDestructuringInitInstruction>());
        var sourceSnapshot = ExpressionProgramStorageDiagnostics.Collect(initInstruction.SourceProgram);
        Assert.True(sourceSnapshot.OperationCount > 0, "Expected destructuring source expression program to contain operations.");

        var snapshot = ExpressionProgramStorageDiagnostics.Collect(parsedProgram);
        Assert.True(
            snapshot.OperationCount >= baselineSnapshot.OperationCount + sourceSnapshot.OperationCount,
            "Expected destructuring source program operations to be included in total storage diagnostics.");
    }

    [Fact]
    public async Task Collect_ForCatchDestructuringBindingTarget_CountsBindingProgramsStorage()
    {
        var baselineProgram = _engine.ParseProgram("""
            function catchBindingBaseline(source) {
                try {
                    throw source;
                } catch (error) {
                    return 1;
                }
            }
            """);
        await _engine.Evaluate(baselineProgram);
        var baselineSnapshot = ExpressionProgramStorageDiagnostics.Collect(baselineProgram);

        var parsedProgram = _engine.ParseProgram("""
            function catchBinding(source) {
                try {
                    throw source;
                } catch ({ [source.key]: value = source.fallback }) {
                    return value;
                }
            }
            """);
        await _engine.Evaluate(parsedProgram);

        var plan = GetFunctionPlan(parsedProgram, "catchBinding");
        var enterCatch = Assert.Single(plan.Instructions.OfType<EnterCatchInstruction>());
        Assert.NotNull(enterCatch.CatchBindingProgram);

        var snapshot = ExpressionProgramStorageDiagnostics.Collect(parsedProgram);
        Assert.True(
            snapshot.OperationCount > baselineSnapshot.OperationCount,
            "Expected catch binding target programs to increase total storage diagnostics.");
    }

    [Fact]
    public void ExpressionRuntimeBuffers_KeepPackedFlagStorageWithoutByteOrBoolArrays()
    {
        var runnerType = typeof(TypedAstEvaluator)
            .GetNestedType("ExecutionPlanRunner", BindingFlags.NonPublic);
        Assert.NotNull(runnerType);

        var fields = runnerType!.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        var expressionFlagField = Assert.Single(fields, field => field.Name == "_expressionFlagBuffer");
        Assert.Equal(typeof(ulong[]), expressionFlagField.FieldType);

        var hasExpressionBoolArray = fields.Any(field =>
            field.Name.Contains("expression", StringComparison.OrdinalIgnoreCase) &&
            field.FieldType == typeof(bool[]));
        var hasExpressionByteArray = fields.Any(field =>
            field.Name.Contains("expression", StringComparison.OrdinalIgnoreCase) &&
            field.FieldType == typeof(byte[]));

        Assert.False(hasExpressionBoolArray);
        Assert.False(hasExpressionByteArray);
    }

    private static ExecutionPlan GetFunctionPlan(ProgramNode program, string functionName)
    {
        var function = Assert.IsType<FunctionDeclaration>(
            program.Body.Single(statement => statement is FunctionDeclaration declaration && declaration.Name.Name == functionName));

        var cache = ((IAstCacheable<ExecutionPlanCache>)function.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build. Failure: {cache.FailureReason}");
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}
