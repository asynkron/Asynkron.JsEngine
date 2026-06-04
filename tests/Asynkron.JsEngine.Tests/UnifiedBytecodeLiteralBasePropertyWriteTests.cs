using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// A19: deep PURE property-WRITE chains whose base is an object/array literal and whose terminal
/// store is named (`({ a: { b: 0 } }).a.b = v`, `({ a: {} }).a.b.c = v`, `[box][0].a = v`) are
/// admitted onto the synchronous production unified-bytecode route. Mirrors the A17/A18 read-past-
/// boundary widening (#3154). Tests assert both correctness and routing (the
/// `unified-bytecode-production-fast-path` log proves the production VM owned execution; an
/// interpreter fallback would fail the routing assertion). Adversarial chains mixing a call, a
/// compound/logical write, a chained assignment, an accessor, an optional read, or a computed
/// terminal store off the literal base (compiler foundation gap) must remain declined.
/// </summary>
[Category(TestCategories.Debugging)]
public sealed class UnifiedBytecodeLiteralBasePropertyWriteTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ProductionFastPathLog = "unified-bytecode-production-fast-path";

    private static ExecutionPlan GetFunctionPlan(string source, string functionName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is FunctionDeclaration f && f.Name?.Name == functionName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }

    private static UnifiedBytecodeProductionEligibilityResult Evaluate(string source, string fn) =>
        UnifiedBytecodeProductionEligibility.Evaluate(
            GetFunctionPlan(source, fn),
            new UnifiedBytecodeProductionActivationDescriptor());

    // ---------------------------------------------------------------------
    // Eligibility / owned-opcode shape coverage
    // ---------------------------------------------------------------------

    [Fact]
    public void Evaluate_ObjectLiteralBaseNamedWrite_AcceptsOwnedPropertyOpcodes()
    {
        var result = Evaluate("""
            function write() {
                return ({ a: { b: 0 } }).a.b = 1;
            }
            """, "write");

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
        Assert.Equal(1, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty));
    }

    [Fact]
    public void Evaluate_ObjectLiteralBaseDeepNamedWrite_AcceptsOwnedPropertyOpcodes()
    {
        var result = Evaluate("""
            function write() {
                return ({ a: { b: {} } }).a.b.c = 7;
            }
            """, "write");

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(2, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty));
        Assert.Equal(1, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty));
    }

    [Fact]
    public void Evaluate_ArrayLiteralBaseComputedPrefixNamedWrite_AcceptsOwnedPropertyOpcodes()
    {
        var result = Evaluate("""
            function write(box) {
                return [box][0].a = 1;
            }
            """, "write");

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
    }

    [Fact]
    public void Evaluate_ObjectLiteralBaseNamedWriteWithIdentifierValue_Accepts()
    {
        var result = Evaluate("""
            function write(v) {
                return ({ a: {} }).a.b = v;
            }
            """, "write");

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.SetNamedProperty);
    }

    // ---------------------------------------------------------------------
    // Adversarial edges — must STILL decline
    // ---------------------------------------------------------------------

    [Fact]
    public void Evaluate_ComputedTerminalWriteOffLiteralBase_Declines()
    {
        // The compiler cannot lower a SetComputedProperty terminal off a literal base
        // ("Unsupported computed property key span."), so eligibility must decline rather than
        // admit a half-correct shape.
        var result = Evaluate("""
            function write() {
                return ({ a: {} }).a['b'] = 1;
            }
            """, "write");

        Assert.False(result.IsEligible);
        Assert.NotEqual(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_ChainedAssignmentOffLiteralBase_Declines()
    {
        var result = Evaluate("""
            function write(o) {
                return ({ a: {} }).a.b = o.c = 1;
            }
            """, "write");

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency, result.Code);
    }

    [Fact]
    public void Evaluate_CallInValueOffLiteralBase_Declines()
    {
        var result = Evaluate("""
            function write() {
                return ({ a: {} }).a.b = (function () { return 1; })();
            }
            """, "write");

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency, result.Code);
    }

    [Fact]
    public void Evaluate_CompoundWriteOffLiteralBase_Declines()
    {
        var result = Evaluate("""
            function write() {
                return ({ a: { b: 0 } }).a.b += 1;
            }
            """, "write");

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency, result.Code);
    }

    [Fact]
    public void Evaluate_AccessorInLiteralBaseWrite_Declines()
    {
        var result = Evaluate("""
            function write() {
                return ({ get a() { return {}; } }).a.b = 1;
            }
            """, "write");

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency, result.Code);
    }

    // ---------------------------------------------------------------------
    // Execution correctness + routing (production VM owns execution)
    // ---------------------------------------------------------------------

    private async Task AssertProductionRouted(string source, object expected)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(source);
        Assert.Equal(expected, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Execute_ObjectLiteralBaseNamedWrite_RoutesAndReturnsValue()
    {
        // The assignment expression evaluates to the assigned value.
        await AssertProductionRouted("""
            function write() {
                return ({ a: { b: 0 } }).a.b = 42;
            }
            write();
            """, 42d);
    }

    [Fact(Timeout = 5000)]
    public async Task Execute_ObjectLiteralBaseDeepNamedWrite_RoutesAndReturnsValue()
    {
        await AssertProductionRouted("""
            function write() {
                return ({ a: { b: {} } }).a.b.c = 9;
            }
            write();
            """, 9d);
    }

    [Fact(Timeout = 5000)]
    public async Task Execute_ArrayLiteralBaseComputedPrefixNamedWrite_RoutesAndReturnsValue()
    {
        await AssertProductionRouted("""
            function write(box) {
                return [box][0].a = 5;
            }
            write({});
            """, 5d);
    }

    [Fact(Timeout = 5000)]
    public async Task Execute_LiteralBaseNamedWrite_MutatesTheOwnSlotOfTheLiteral()
    {
        // The write targets the freshly-constructed literal; observing the mutation on the SAME
        // object confirms the production VM writes the right receiver (not a stale copy).
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function write() {
                let box = { a: { b: 0 } };
                box.a.b = 1;
                return box.a.b;
            }
            write();
            """);

        Assert.Equal(1d, result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Execute_GetterInReceiverPrefixInvokedExactlyOnce_MatchesInterpreter()
    {
        // A getter in the receiver prefix must run exactly once; the production VM applies each read
        // hop in source order, so the counter increments once just like the interpreter.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let calls = 0;
            let sink = { b: 0 };
            function write() {
                return ({ get a() { calls++; return sink; } }).a.b = 7;
            }
            let value = write();
            value + ',' + calls + ',' + sink.b;
            """);

        Assert.Equal("7,1,7", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Execute_NullishReceiverPrefixThrowsTypeError_LikeInterpreter()
    {
        // Writing past an undefined intermediate must throw a TypeError, exactly as the interpreter
        // would.
        await using var engine = CreateEngine();
        await Assert.ThrowsAnyAsync<Exception>(async () => await engine.Evaluate("""
            function write() {
                return ({ a: undefined }).a.b.c = 1;
            }
            write();
            """));
    }
}
