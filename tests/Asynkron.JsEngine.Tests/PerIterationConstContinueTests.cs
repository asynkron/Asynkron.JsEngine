using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Regression battery for the per-iteration lexical const + plain/labeled <c>continue</c> bug in the
///     unified-bytecode production VM. When a function runs in the MATERIALIZED-CALL-ENVIRONMENT mode
///     (forced by a free identifier read/write/call-target), a plain <c>continue</c> that re-enters a loop
///     body carrying a per-iteration lexical <c>const</c> must restore the body block's scope-environment
///     owner the same way <c>PopEnvironment</c> does. Otherwise a later iteration reads the const slot while
///     it is still in TDZ -> "Cannot access '&lt;name&gt;' before initialization".
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class PerIterationConstContinueTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private void AssertRouted(string expectedLog)
    {
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(expectedLog, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExactRepro_FreeRead_PerIterationConstWithContinue_ReturnsCorrectAndRouted()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "var z=0;" +
            "function t(){ const rows=[]; for (let inner of [10,20,30]) { if (inner===20) continue; const value=inner; rows.push(''+(value+z)); } return rows.join(','); }" +
            "t();");
        Assert.Equal("10,30", result);
        AssertRouted("unified-bytecode-production-fast-path func=t");
    }

    [Fact]
    public async Task AsyncFreeVersion_PerIterationConstWithContinue_StaysCorrect()
    {
        await using var engine = CreateEngine();
        var captured = "";
        engine.SetGlobalFunction("captureResult", args =>
        {
            if (args.Count > 0)
            {
                captured = args[0].ToObject()?.ToString() ?? "";
            }

            return Asynkron.JsEngine.JsTypes.JsValue.Null;
        });

        // An async function returns a Promise; resolve it through then() and capture the value.
        await engine.Evaluate(
            "var z=0;" +
            "async function t(){ const rows=[]; for (let inner of [10,20,30]) { if (inner===20) continue; const value=inner; rows.push(''+(value+z)); } return rows.join(','); }" +
            "t().then(function(v){ captureResult(v); });");
        Assert.Equal("10,30", captured);
    }

    [Fact]
    public async Task MultiplePerIterationConsts_WithContinue_StayCorrect()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "var z=1;" +
            "function t(){ const rows=[]; for (let inner of [10,20,30]) { if (inner===20) continue; const a=inner; const b=inner*2; rows.push(''+(a+b+z)); } return rows.join(','); }" +
            "t();");
        // 10 -> 10+20+1 = 31 ; 30 -> 30+60+1 = 91
        Assert.Equal("31,91", result);
        AssertRouted("unified-bytecode-production-fast-path func=t");
    }

    [Fact]
    public async Task NestedLoops_InnerContinueOverPerIterationConst_StaysCorrect()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "var z=0;" +
            "function t(){ const rows=[]; for (let i of [1,2]) { for (let j of [10,20,30]) { if (j===20) continue; const value=i*100+j; rows.push(''+(value+z)); } } return rows.join(','); }" +
            "t();");
        // i=1: 110,130 ; i=2: 210,230
        Assert.Equal("110,130,210,230", result);
        AssertRouted("unified-bytecode-production-fast-path func=t");
    }

    [Fact]
    public async Task LabeledContinueToOuterLoop_OverPerIterationConst_StaysCorrect()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "var z=0;" +
            "function t(){ const rows=[]; outer: for (let i of [1,2,3]) { const value=i*10; if (i===2) continue outer; rows.push(''+(value+z)); } return rows.join(','); }" +
            "t();");
        // i=1: value=10 push ; i=2: continue outer (skip push) ; i=3: value=30 push
        Assert.Equal("10,30", result);
        AssertRouted("unified-bytecode-production-fast-path func=t");
    }

    [Fact]
    public async Task LabeledContinueFromInnerToOuter_OverPerIterationConst_StaysCorrect()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "var z=0;" +
            "function t(){ const rows=[]; outer: for (let i of [1,2]) { for (let j of [10,20]) { const value=i*100+j; if (j===10) continue outer; rows.push(''+(value+z)); } } return rows.join(','); }" +
            "t();");
        // i=1,j=10: value computed, continue outer -> next i. i=2,j=10: continue outer. So nothing pushed.
        Assert.Equal("", result);
        AssertRouted("unified-bytecode-production-fast-path func=t");
    }

    [Fact]
    public async Task PerIterationLet_NotConst_WithContinueAndFreeRead_StaysCorrect()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "var z=0;" +
            "function t(){ const rows=[]; for (let inner of [10,20,30]) { if (inner===20) continue; let value=inner; rows.push(''+(value+z)); } return rows.join(','); }" +
            "t();");
        Assert.Equal("10,30", result);
        AssertRouted("unified-bytecode-production-fast-path func=t");
    }

    [Fact]
    public async Task ConstReadAfterContinuePoint_NormalInit_HasCorrectValue()
    {
        await using var engine = CreateEngine();
        // The continue is conditional and before the const; on non-continue iterations the const is
        // initialized and read normally afterward.
        var result = await engine.Evaluate(
            "var z=5;" +
            "function t(){ const rows=[]; for (let inner of [10,20,30]) { if (inner===20) continue; const value=inner+1; rows.push(''+(value+z)); } return rows.join(','); }" +
            "t();");
        // 10 -> 11+5=16 ; 30 -> 31+5=36
        Assert.Equal("16,36", result);
        AssertRouted("unified-bytecode-production-fast-path func=t");
    }

    [Fact]
    public async Task SlotOnlyVersion_NoFreeName_WithContinue_StaysCorrect()
    {
        await using var engine = CreateEngine();
        // No free identifier -> stays on the slot-only path. Regression guard for the reference-correct path.
        var result = await engine.Evaluate(
            "function t(){ const rows=[]; for (let inner of [10,20,30]) { if (inner===20) continue; const value=inner; rows.push(''+value); } return rows.join(','); }" +
            "t();");
        Assert.Equal("10,30", result);
        // No free identifier -> the function body is self-contained and runs through the script-level
        // production VM route (not the per-call func= route the free-name versions take). Still production.
        AssertRouted("unified-bytecode-production-fast-path script");
    }

    [Fact]
    public async Task GenuineTdzBeforeInit_StillThrows()
    {
        await using var engine = CreateEngine();
        // Reading the per-iteration const before its initializer must STILL be a TDZ error. Do not over-fix.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await engine.Evaluate(
                "var z=0;" +
                "function t(){ const rows=[]; for (let inner of [10,20,30]) { rows.push(''+(value+z)); const value=inner; } return rows.join(','); }" +
                "t();");
        });
    }
}
