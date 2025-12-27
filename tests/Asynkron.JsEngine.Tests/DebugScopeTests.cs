// Minimal debugging tests for scope/closure behavior in for-of loops
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests for scope and closure behavior in for-of loops, especially with destructuring defaults and eval.
/// These tests verify that closures correctly capture their lexical environment.
/// </summary>
public class DebugScopeTests(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact]
    public async Task Step1_ProbeBeforeSimple()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var probeBefore = function() { return x; };
            var x = 1;
            probeBefore();
        """);
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Step2_ProbeBeforeWithForOf()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var probeBefore = function() { return x; };
            var x = 1;
            for (let [_] of [[1]]) { }
            probeBefore();
        """);
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Step3_ProbeBeforeWithEvalInForOf()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var probeBefore = function() { return x; };
            var x = 1;
            for (let [_] of [[eval('var x = 2;')]]) { }
            probeBefore();
        """);
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task Step4_ProbeExprWithEvalInForOf()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var x = 1;
            var probeExpr;
            for (let [_] of [[(probeExpr = function() { return x; })]]) { }
            probeExpr();
        """);
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Step5_BothProbesWithEval()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var x = 1;
            var probeExpr;
            for (let [_] of [[eval('var x = 2;'), (probeExpr = function() { return x; })]]) { }
            probeExpr();
        """);
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task Step6_DestructuringDefaultWithClosure()
    {
        // The key difference: closure in destructuring DEFAULT, not just in the array
        // Use [[undefined]] so that the default expression is triggered
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var x = 1;
            var probeDecl;
            for (let [_ = probeDecl = function() { return x; }] of [[undefined]]) { }
            probeDecl();
        """);
        Assert.Equal(1d, result);
    }

    [Fact]
    public async Task Step7_DestructuringDefaultWithEval()
    {
        // Closure in destructuring default WITH eval in iterable
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var x = 1;
            var probeDecl;
            for (let [_ = probeDecl = function() { return x; }] of [[eval('var x = 2;')]]) { }
            probeDecl();
        """);
        Assert.Equal(2d, result);
    }

    [Fact]
    public async Task Step8_FullTest()
    {
        // Full test - exactly like the failing test
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var probeBefore = function() { return x; };
            var x = 1;
            var probeDecl, probeExpr, probeBody;

            for (
                let [_ = probeDecl = function() { return x; }]
                of
                [[eval('var x = 2;'), probeExpr = function() { return x; }]]
              )
              probeBody = function() { return x; };

            JSON.stringify([probeBefore(), probeDecl(), probeExpr(), probeBody(), x]);
        """);
        Assert.Equal("[2,2,2,2,2]", result);
    }
}

