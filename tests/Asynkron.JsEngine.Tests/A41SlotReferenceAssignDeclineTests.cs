using System;
using System.Linq;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     A41 (burn-down) — DECLINED, investigated. "Slot-resolved identifier via dynamic-name reference op."
///
///     SHAPE: a slot-bound local assigned to as a CONSUMED (value-producing) sub-expression — e.g.
///     <c>return (x = v)</c>, <c>var y = (x = v)</c>, <c>g(x = v)</c>, chained <c>(x = z = v)</c>. Because the
///     assignment's value is used, it lowers to the expression-program <c>ResolveIdentifierReference</c> /
///     <c>StoreResolvedIdentifier</c> ops (the "dynamic-name reference" path) rather than a top-level
///     <c>AssignmentSlotInstruction</c> → <c>StoreSlot</c>.
///
///     WHY IT DECLINES: <see cref="UnifiedBytecodeProductionEligibility"/> declines any
///     <c>ResolveIdentifierReference</c> / <c>StoreResolvedIdentifier</c> whose identifier resolves to an
///     activation slot — UNCONDITIONALLY (not gated on the dynamic-name flag) — with
///     <see cref="UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape"/> and reason
///     "...resolves to an activation slot and is outside the ordinary dynamic-name production slice.". The
///     compiler itself declines the same shape ("...is not eligible for dynamic unified bytecode assignment
///     references."). The dynamic-name VM has no slot-targeting reference-store opcode:
///     <c>StoreDynamicIdentifierReference</c> writes by NAME to the threaded environment, never to a flat slot.
///     Admitting cleanly requires a NEW slot-reference-store lowering (push slot reference, store-to-slot,
///     leave the value on the stack) — compiler + VM foundation work, not a clean admit.
///
///     STANDING TRIPWIRE: each declined case asserts CORRECT runtime results (the IR runner keeps it correct)
///     AND the exact decline code + reason. The ADMITTED neighbor (the SAME assignment written as a top-level
///     STATEMENT, whose value is discarded, taking <c>AssignmentSlotInstruction</c> → <c>StoreSlot</c>) proves
///     the decline is scoped to consumed assignment expressions, not slot stores in general. Complements
///     <see cref="AlreadyRoutingShapePinTests"/> (A46/A49 already-routing pins).
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class A41SlotReferenceAssignDeclineTests(ITestOutputHelper output) : InternalTestBase(output)
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

    // --- DECLINED shapes: correct runtime results, NOT routed. ---

    [Theory]
    [InlineData("function f(){ var x=0; return (x = 5); } f();", 5d, "f")]
    [InlineData("function f(){ var x=0; var y = (x = 5); return x+y; } f();", 10d, "f")]
    [InlineData("function f(){ var x=0; g(x = 5); return x; } function g(a){} f();", 5d, "f")]
    [InlineData("function f(){ var x=0; return (x = 5) + x; } f();", 10d, "f")]
    [InlineData("function f(){ var x=0,z=0; return (x = z = 5); } f();", 5d, "f")]
    public async Task ConsumedSlotAssign_CorrectButDeclined(string source, double expected, string func)
    {
        await using var engine = CreateEngine();
        var r = await engine.Evaluate(source);
        Assert.Equal(expected, Convert.ToDouble(r));
        Assert.False(
            RoutedFunction(func),
            "a slot-bound assignment consumed as a value must NOT route through the production VM");
    }

    // The decline is NOT gated on the dynamic-name flag: it fires for the consumed slot assignment whether
    // or not ordinary dynamic identifiers are allowed. Pin both descriptor variants.
    [Theory]
    [InlineData("function f(){ var x=0; return (x = 5); }", false)]
    [InlineData("function f(){ var x=0; return (x = 5); }", true)]
    [InlineData("function f(){ var x=0; var y = (x = 5); return x+y; }", false)]
    [InlineData("function f(){ var x=0; var y = (x = 5); return x+y; }", true)]
    [InlineData("function f(){ var x=0,z=0; return (x = z = 5); }", true)]
    public void ConsumedSlotAssign_DeclineCodeAndReason_ArePinned(string source, bool allowsDynamicNames)
    {
        var plan = GetFunctionPlan(source, "f");
        var result = UnifiedBytecodeProductionEligibility.Evaluate(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations: allowsDynamicNames));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.UnsupportedPlanShape, result.Code);
        Assert.Contains(
            "resolves to an activation slot and is outside the ordinary dynamic-name production slice.",
            result.Reason,
            StringComparison.Ordinal);
    }

    // --- ADMITTED neighbor: the SAME assignment as a top-level STATEMENT (value discarded) routes. ---

    [Fact]
    public async Task StatementSlotAssign_RoutesThroughProduction()
    {
        // `x = 5;` as a statement lowers to AssignmentSlotInstruction -> StoreSlot and routes, proving the
        // A41 decline is scoped to consumed (value-producing) assignment expressions, not slot stores.
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
