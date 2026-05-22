using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
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
    public async Task Collect_ForRepresentativeProgram_ReportsEncodedAndUnsupportedInstructionFamilies()
    {
        var parsedProgram = _engine.ParseProgram("""
            function sample(value) {
                let next = value + 1;
                if (next > 2) {
                    next += 1;
                }

                return next;
            }

            sample(1);
            """);

        await _engine.Evaluate(parsedProgram);
        var snapshot = StatementInstructionStorageDiagnostics.Collect(parsedProgram);

        Assert.True(snapshot.InstructionCount > 0);
        Assert.True(snapshot.EncodedInstructionCount > 0);
        Assert.True(snapshot.UnsupportedInstructionCount >= 0);
        Assert.Equal(snapshot.InstructionCount, snapshot.EncodedInstructionCount + snapshot.UnsupportedInstructionCount);
        Assert.True(snapshot.EncodedInstructionBytes > 0);
        Assert.NotEmpty(snapshot.InstructionKindHistogram);
        Assert.NotEmpty(snapshot.UnsupportedInstructionKindHistogram);
    }

    [Fact]
    public async Task Collect_ForSimpleDeclarationPlan_UsesSupportedSubsetOnly()
    {
        var parsedProgram = _engine.ParseProgram("""
            function declareSimple(value) {
                let next = value + 1;
                return next;
            }
            """);

        await _engine.Evaluate(parsedProgram);
        var plan = GetFunctionPlan(parsedProgram, "declareSimple");
        var snapshot = StatementInstructionStorageDiagnostics.Collect(plan);

        Assert.True(snapshot.InstructionCount > 0);
        Assert.Equal(snapshot.InstructionCount, snapshot.EncodedInstructionCount);
        Assert.Equal(0, snapshot.UnsupportedInstructionCount);
        Assert.Empty(snapshot.UnsupportedInstructionKindHistogram);
    }

    [Fact]
    public async Task Collect_ForForOfLoop_AccountsUnsupportedInstructionKinds()
    {
        var parsedProgram = _engine.ParseProgram("""
            function sum(items) {
                let total = 0;
                for (const item of items) {
                    total += item;
                }
                return total;
            }
            """);

        await _engine.Evaluate(parsedProgram);
        var snapshot = StatementInstructionStorageDiagnostics.Collect(parsedProgram);

        Assert.True(snapshot.UnsupportedInstructionCount > 0);
        Assert.Contains(snapshot.UnsupportedInstructionKindHistogram, pair => pair.Key == InstructionKind.IteratorMoveNext);
        Assert.Contains(snapshot.UnsupportedInstructionKindHistogram, pair => pair.Key == InstructionKind.IteratorClose);
    }

    [Fact]
    public void Codec_ForSupportedInstruction_RoundTripsKindNextAndFlags()
    {
        var instruction = new AssignmentSlotInstruction(
            Next: 42,
            TargetSymbol: Symbol.Intern("value"),
            ValueProgram: ExpressionProgram.Empty,
            SuppressCompletionValue: true);

        Assert.True(StatementInstructionStorageCodec.TryEncode(instruction, out var encoded));

        var decoded = StatementInstructionStorageCodec.Decode(encoded);
        Assert.Equal(InstructionKind.AssignmentSlot, decoded.Kind);
        Assert.Equal(42, decoded.Next);
        Assert.Equal((byte)0b0000_0010, decoded.Flags);
    }

    [Fact]
    public void Codec_ForUnsupportedInstruction_ReturnsFalse()
    {
        var instruction = new JumpInstruction(TargetIndex: 7);
        Assert.False(StatementInstructionStorageCodec.TryEncode(instruction, out _));
    }

    private static ExecutionPlan GetFunctionPlan(ProgramNode program, string name)
    {
        var function = program
            .Body
            .OfType<FunctionDeclaration>()
            .Select(static declaration => declaration.Function)
            .Single(functionExpression => functionExpression.Name?.Name == name);

        var cache = ((IAstCacheable<ExecutionPlanCache>)function).GetOrCreateCache();
        Assert.True(cache.Succeeded, $"Expected execution plan build to succeed. Failure: {cache.FailureReason}");
        Assert.NotNull(cache.Plan);
        return cache.Plan!;
    }
}
