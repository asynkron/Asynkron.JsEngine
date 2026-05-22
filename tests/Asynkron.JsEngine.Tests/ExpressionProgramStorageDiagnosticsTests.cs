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
}
