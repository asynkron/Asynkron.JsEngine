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
        var instructions = new ExecutionInstruction[]
        {
            new EvaluateAndDiscardInstruction(Next: 1, ExpressionProgram.Empty),
            new AwaitAndDiscardInstruction(Next: 2, AwaitStateKey: Symbol.Intern("awaitState"), AwaitedProgram: ExpressionProgram.Empty),
            new AssignmentSlotInstruction(Next: 3, TargetSymbol: Symbol.Intern("value"), ValueProgram: ExpressionProgram.Empty, SuppressCompletionValue: true),
            new LogicalCompoundAssignmentSlotInstruction(Next: 4, TargetSymbol: Symbol.Intern("lhs"), Operator: BinaryOperator.LogicalOr, RhsProgram: ExpressionProgram.Empty),
            new CompoundAssignmentSlotInstruction(Next: 5, TargetSymbol: Symbol.Intern("counter"), Operator: BinaryOperator.Add, RhsProgram: ExpressionProgram.Empty),
            new SimpleVariableDeclarationInstruction(Next: 6, VarKind: VariableKind.Let, TargetSymbol: Symbol.Intern("declared"), InitializerProgram: ExpressionProgram.Empty),
            new BindingVariableDeclarationInstruction(Next: 7, VarKind: VariableKind.Const, TargetProgram: new IdentifierBindingTargetProgram(Symbol.Intern("bound")), InitializerProgram: ExpressionProgram.Empty),
            new ReturnInstruction(Next: 8, ReturnProgram: ExpressionProgram.Empty),
            new ThrowInstruction(ThrowProgram: ExpressionProgram.Empty),
            new YieldInstruction(Next: 9, YieldProgram: ExpressionProgram.Empty),
            new YieldStarInstruction(Next: 10, IterableProgram: ExpressionProgram.Empty),
            new JumpInstruction(TargetIndex: 11),
            new BranchInstruction(ConsequentIndex: 12, AlternateIndex: 13, ConditionProgram: ExpressionProgram.Empty),
            new IteratorInitInstruction(IteratorDriverKind.Sync, IteratorSlot: Symbol.Intern("iterator"), IteratorSlotIndex: -1, Next: 14, IterableProgram: ExpressionProgram.Empty),
            new ForInInitInstruction(StateSlot: Symbol.Intern("state"), StateSlotIndex: -1, ValueSlot: Symbol.Intern("valueSlot"), ValueSlotIndex: -1, Next: 15, ObjectProgram: ExpressionProgram.Empty),
            new EnterWithInstruction(WithScopeSlot: Symbol.Intern("withScope"), Next: 16, ObjectProgram: ExpressionProgram.Empty),
            new ArrayDestructuringInitInstruction(IteratorSlot: Symbol.Intern("arrayIterator"), IteratorSlotIndex: -1, Next: 17, SourceProgram: ExpressionProgram.Empty)
        };

        foreach (var instruction in instructions)
        {
            Assert.True(StatementInstructionStorageCodec.TryEncode(instruction, out var encoded));
            Assert.Equal(instruction.Kind, encoded.Kind);
            Assert.Equal(instruction.Next, encoded.Next);
            var decoded = StatementInstructionStorageCodec.Decode(encoded);
            Assert.Equal(instruction, decoded);
        }
    }

    [Fact]
    public void Codec_ForUnsupportedInstruction_ReturnsFalse()
    {
        var instruction = new IteratorMoveNextInstruction(
            IteratorDriverKind.Sync,
            IteratorSlot: Symbol.Intern("iterator"),
            ValueSlot: Symbol.Intern("value"),
            IteratorSlotIndex: -1,
            ValueSlotIndex: -1,
            BreakIndex: 9,
            Next: 10);
        Assert.False(StatementInstructionStorageCodec.TryEncode(instruction, out _));
    }

    [Fact]
    public void Codec_InstructionKindSupportClassification_IsSourceGated()
    {
        var expectedSupportedKinds = new HashSet<InstructionKind>
        {
            InstructionKind.Throw,
            InstructionKind.EvaluateAndDiscard,
            InstructionKind.AwaitAndDiscard,
            InstructionKind.AssignmentSlot,
            InstructionKind.LogicalCompoundAssignmentSlot,
            InstructionKind.SimpleVariableDeclaration,
            InstructionKind.BindingVariableDeclaration,
            InstructionKind.Yield,
            InstructionKind.YieldStar,
            InstructionKind.Jump,
            InstructionKind.Branch,
            InstructionKind.Return,
            InstructionKind.EnterWith,
            InstructionKind.CompoundAssignmentSlot,
            InstructionKind.ForInInit,
            InstructionKind.IteratorInit,
            InstructionKind.ArrayDestructuringInit
        };

        var allKinds = Enum.GetValues<InstructionKind>();
        foreach (var kind in allKinds)
        {
            Assert.Equal(expectedSupportedKinds.Contains(kind), StatementInstructionStorageCodec.IsSupportedKind(kind));
        }
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
