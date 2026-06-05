using System;
using System.Linq;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     A41 (burn-down) — ADMITTED. "Slot-resolved identifier via dynamic-name reference op."
///
///     SHAPE: a slot-bound local assigned to as a CONSUMED (value-producing) sub-expression — e.g.
///     <c>return (x = v)</c>, <c>var y = (x = v)</c>, <c>g(x = v)</c>, chained <c>(x = z = v)</c>. Because the
///     assignment's value is used, it lowers to the expression-program <c>ResolveIdentifierReference</c> /
///     <c>StoreResolvedIdentifier</c> ops (the "dynamic-name reference" path) rather than a top-level
///     <c>AssignmentSlotInstruction</c> → <c>StoreSlot</c>.
///
///     ADMISSION: <see cref="UnifiedBytecodeProductionEligibility"/> and
///     <see cref="UnifiedBytecodeCompiler"/> now track explicit slot-resolved identifier references as compiler
///     side-state. <c>LoadResolvedIdentifierValue</c> lowers to <c>LoadSlot</c>, and
///     <c>StoreResolvedIdentifier</c> lowers to <c>DuplicateTop</c> + <c>StoreSlot</c>, preserving the assignment
///     expression result while still writing the flat slot. Name-only activation-slot matches remain declined so
///     with/dynamic shadowing does not get converted into a flat-slot write.
///
///     STANDING TRIPWIRE: each consumed assignment case asserts CORRECT runtime results and a production
///     route hit. The statement neighbor remains pinned to prove discarded and consumed slot stores are both
///     bytecode-owned.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class A41SlotReferenceAssignAdmissionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private bool RoutedFunction(string func) =>
        CurrentLogger!.Collector.Snapshot().Any(rec => rec.Message.Contains(
            $"unified-bytecode-production-fast-path func={func}",
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

    // --- ADMITTED shapes: correct runtime results and production route hits. ---

    [Theory]
    [InlineData("function f(){ var x=0; return (x = 5); } f();", 5d, "f")]
    [InlineData("function f(){ var x=0; var y = (x = 5); return x+y; } f();", 10d, "f")]
    [InlineData("function f(){ var x=0; g(x = 5); return x; } function g(a){} f();", 5d, "f")]
    [InlineData("function f(){ var x=0; return (x = 5) + x; } f();", 10d, "f")]
    [InlineData("function f(){ var x=0,z=0; return (x = z = 5); } f();", 5d, "f")]
    public async Task ConsumedSlotAssign_RoutesThroughProduction(string source, double expected, string func)
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate(source);
        Assert.Equal(expected, Convert.ToDouble(r));
        Assert.True(
            RoutedFunction(func),
            "a slot-bound assignment consumed as a value must route through the production VM");
    }

    [Theory]
    [InlineData("function f(){ var x=0; return (x = 5); }", false)]
    [InlineData("function f(){ var x=0; return (x = 5); }", true)]
    [InlineData("function f(){ var x=0; var y = (x = 5); return x+y; }", false)]
    [InlineData("function f(){ var x=0; var y = (x = 5); return x+y; }", true)]
    [InlineData("function f(){ var x=0; g(x = 5); return x; } function g(a){}", true)]
    [InlineData("function f(){ var x=0,z=0; return (x = z = 5); }", true)]
    public void ConsumedSlotAssign_IsEligible(string source, bool allowsDynamicNames)
    {
        var plan = GetFunctionPlan(source, "f");
        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: allowsDynamicNames));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }

    [Fact]
    public void ConsumedSlotAssign_CompilesToValuePreservingSlotStore()
    {
        var plan = GetFunctionPlan("function f(){ var x=0; return (x = 5) + x; }", "f");
        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());

        Assert.True(result.IsEligible, result.Reason);
        var opCodes = result.Program.Instructions.Select(static instruction => instruction.OpCode).ToArray();
        Assert.Contains(UnifiedBytecodeOpCode.DuplicateTop, opCodes);
        Assert.Contains(UnifiedBytecodeOpCode.StoreSlot, opCodes);
        Assert.DoesNotContain(UnifiedBytecodeOpCode.StoreDynamicIdentifierReference, opCodes);

        var returnInstruction = Assert.Single(
            plan.Instructions.OfType<ReturnInstruction>(),
            i => i.ReturnProgram is not null);
        Assert.NotNull(returnInstruction.ReturnProgram);
        var returnProgram = returnInstruction.ReturnProgram.Value;
        Assert.Contains(
            returnProgram.GetOps(ExpressionOpKind.ResolveIdentifierReference),
            static op => op.Name.Name == "x" && (op.FlatSlotId >= 0 || op.SlotIndex >= 0));
        Assert.Contains(
            returnProgram.GetOps(ExpressionOpKind.StoreResolvedIdentifier),
            static op => op.Name.Name == "x" && (op.FlatSlotId >= 0 || op.SlotIndex >= 0));
    }

    // --- ADMITTED neighbor: the SAME assignment as a top-level STATEMENT (value discarded) routes. ---

    [Fact]
    public async Task StatementSlotAssign_RoutesThroughProduction()
    {
        // `x = 5;` as a statement lowers to AssignmentSlotInstruction -> StoreSlot and routes, proving the
        // statement and consumed-expression slot-store paths are both production-owned.
        await using var engine = CreateEngine();
        var r = await engine.Evaluate("function f(){ var x=0; x = 5; return x; } f();");
        Assert.Equal(5d, r);
        Assert.True(
            RoutedFunction("f"),
            "a top-level statement slot assignment must keep routing through production");
    }

    [Fact]
    public void StatementSlotAssign_IsEligible()
    {
        var plan = GetFunctionPlan("function f(){ var x=0; x = 5; return x; }", "f");
        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor());
        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);
    }
}
