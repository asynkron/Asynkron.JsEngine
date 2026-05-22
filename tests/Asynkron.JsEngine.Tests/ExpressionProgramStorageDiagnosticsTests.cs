using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Ast;

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
        Assert.True(snapshot.EstimatedPackedOperationBytes > 0, "Expected non-zero estimated PackedExpressionOp storage.");
        Assert.NotEmpty(snapshot.MaxStackDepthHistogram);
    }

    [Fact]
    public void Collect_ForDefaultAndEmptyPrograms_ReportsZeroOpsAndDepth()
    {
        var defaultSnapshot = ExpressionProgramStorageDiagnostics.Collect(default(ExpressionProgram));
        Assert.Equal(1, defaultSnapshot.ProgramCount);
        Assert.Equal(0, defaultSnapshot.OperationCount);
        Assert.Equal(0, defaultSnapshot.EstimatedPackedOperationBytes);
        var defaultDepth = Assert.Single(defaultSnapshot.MaxStackDepthHistogram);
        Assert.Equal(0, defaultDepth.Key);
        Assert.Equal(1, defaultDepth.Value);

        var emptySnapshot = ExpressionProgramStorageDiagnostics.Collect(ExpressionProgram.Empty);
        Assert.Equal(1, emptySnapshot.ProgramCount);
        Assert.Equal(0, emptySnapshot.OperationCount);
        Assert.Equal(0, emptySnapshot.EstimatedPackedOperationBytes);
        var emptyDepth = Assert.Single(emptySnapshot.MaxStackDepthHistogram);
        Assert.Equal(0, emptyDepth.Key);
        Assert.Equal(1, emptyDepth.Value);
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

    private static ExecutionPlan GetFunctionPlan(ProgramNode program, string functionName)
    {
        var function = Assert.IsType<FunctionDeclaration>(
            program.Body.Single(statement => statement is FunctionDeclaration declaration && declaration.Name.Name == functionName));

        var cache = ((IAstCacheable<ExecutionPlanCache>)function.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Plan should build. Failure: {cache.FailureReason}");
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}
