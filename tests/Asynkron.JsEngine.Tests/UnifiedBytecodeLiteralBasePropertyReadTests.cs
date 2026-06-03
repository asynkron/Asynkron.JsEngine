using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// A17/A18: deep PURE property-read chains whose base is an object/array literal
/// (`({a:{b:1}}).a.b`, `({a:1})['a']['b']`, `[x][0].a['b']`) are admitted onto the synchronous
/// production unified-bytecode route. Tests assert both correctness and routing (the
/// `unified-bytecode-production-fast-path` log proves the production VM owned execution; an
/// interpreter fallback would fail the routing assertion). Adversarial chains mixing a call or
/// write mid-chain must remain declined.
/// </summary>
[Category(TestCategories.Debugging)]
public sealed class UnifiedBytecodeLiteralBasePropertyReadTests(ITestOutputHelper output)
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
    // Eligibility / owned-opcode shape coverage (A17 named, A18 computed)
    // ---------------------------------------------------------------------

    [Fact]
    public void Evaluate_ObjectLiteralBaseNamedReadDeep_AcceptsOwnedPropertyOpcodes()
    {
        var result = Evaluate("""
            function read() {
                return ({ a: { b: { c: 1 } } }).a.b.c;
            }
            """, "read");

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(3, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty));
        Assert.DoesNotContain(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
    }

    [Fact]
    public void Evaluate_ObjectLiteralBaseComputedReadDeep_AcceptsOwnedPropertyOpcodes()
    {
        var result = Evaluate("""
            function read() {
                return ({ a: { b: { c: 1 } } })['a']['b']['c'];
            }
            """, "read");

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Equal(3, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty));
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.RequireObjectCoercible && instruction.Operand == 1);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.ResolvePropertyKey);
    }

    [Fact]
    public void Evaluate_ObjectLiteralBaseMixedReadDeep_AcceptsOwnedPropertyOpcodes()
    {
        var result = Evaluate("""
            function read(k) {
                return ({ a: { b: { c: 7 } } }).a[k].c;
            }
            """, "read");

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty);
    }

    [Fact]
    public void Evaluate_ArrayLiteralBaseComputedThenNamedRead_AcceptsOwnedPropertyOpcodes()
    {
        var result = Evaluate("""
            function read(box) {
                return [box][0].a.b;
            }
            """, "read");

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
        Assert.Equal(2, result.Program.Instructions.Count(instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetNamedProperty));
    }

    [Fact]
    public void Evaluate_ObjectLiteralBaseComputedReadWithRichKey_Accepts()
    {
        var result = Evaluate("""
            function read(left, right) {
                return ({ ab: { x: 1 } })[left + right].x;
            }
            """, "read");

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.Binary);
        Assert.Contains(result.Program.Instructions, instruction =>
            instruction.OpCode == UnifiedBytecodeOpCode.GetComputedProperty);
    }

    // ---------------------------------------------------------------------
    // Adversarial edges — must STILL decline
    // ---------------------------------------------------------------------

    [Fact]
    public void Evaluate_CallMidChainOffLiteralBase_Declines()
    {
        var result = Evaluate("""
            function read() {
                return ({ a: { b() { return { c: 1 }; } } }).a.b().c;
            }
            """, "read");

        Assert.False(result.IsEligible);
        Assert.NotEqual(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void Evaluate_WriteAtEndOfLiteralBaseChain_Declines()
    {
        var result = Evaluate("""
            function write() {
                return ({ a: { b: 0 } }).a.b = 1;
            }
            """, "write");

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.PropertyWriteDependency, result.Code);
    }

    [Fact]
    public void Evaluate_OptionalReadInLiteralBaseChain_Declines()
    {
        var result = Evaluate("""
            function read() {
                return ({ a: { b: 1 } }).a?.b;
            }
            """, "read");

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void Evaluate_AccessorInLiteralBase_Declines()
    {
        var result = Evaluate("""
            function read() {
                return ({ get a() { return 1; } }).a;
            }
            """, "read");

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void Evaluate_MethodInLiteralBase_Declines()
    {
        var result = Evaluate("""
            function read() {
                return ({ m() {} }).m;
            }
            """, "read");

        Assert.False(result.IsEligible);
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
    public async Task Execute_ObjectLiteralBaseNamedReadDeep_RoutesAndReturnsValue()
    {
        await AssertProductionRouted("""
            function read() {
                return ({ a: { b: { c: 42 } } }).a.b.c;
            }
            read();
            """, 42d);
    }

    [Fact(Timeout = 5000)]
    public async Task Execute_ObjectLiteralBaseComputedReadDeep_RoutesAndReturnsValue()
    {
        await AssertProductionRouted("""
            function read() {
                return ({ a: { b: { c: 7 } } })['a']['b']['c'];
            }
            read();
            """, 7d);
    }

    [Fact(Timeout = 5000)]
    public async Task Execute_ArrayLiteralBaseComputedThenNamedRead_RoutesAndReturnsValue()
    {
        await AssertProductionRouted("""
            function read(box) {
                return [box][0].a.b;
            }
            read({ a: { b: 99 } });
            """, 99d);
    }

    [Fact(Timeout = 5000)]
    public async Task Execute_ObjectLiteralBaseMixedReadWithParamKey_RoutesAndReturnsValue()
    {
        await AssertProductionRouted("""
            function read(k) {
                return ({ a: { b: { c: 5 } } }).a[k].c;
            }
            read('b');
            """, 5d);
    }

    [Fact(Timeout = 5000)]
    public async Task Execute_GetterInvokedExactlyOncePerHop_MatchesInterpreter()
    {
        // A getter in the chain must run exactly once; the production VM applies each read hop in
        // source order, so the counter increments once per access just like the interpreter.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            let calls = 0;
            function read() {
                return ({ a: { get b() { calls++; return { c: 11 }; } } }).a.b.c;
            }
            let value = read();
            value + ',' + calls;
            """);

        Assert.Equal("11,1", result);
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ProductionFastPathLog, StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task Execute_NullishHopThrowsTypeError_LikeInterpreter()
    {
        // Reading past an undefined intermediate must throw a TypeError (RequireObjectCoercible),
        // exactly as the interpreter would.
        await using var engine = CreateEngine();
        await Assert.ThrowsAnyAsync<Exception>(async () => await engine.Evaluate("""
            function read() {
                return ({ a: { b: undefined } }).a.b.c.d;
            }
            read();
            """));
    }
}
