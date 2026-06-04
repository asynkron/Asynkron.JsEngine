using System;
using Asynkron.JsEngine.Parser;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     A52 (burn-down): the <c>debugger;</c> statement. ECMAScript semantics — with no debugger
///     attached, <c>debugger;</c> evaluates to a no-op (DebuggerStatement : Empty). It must parse,
///     lower, and execute as a no-op on BOTH the interpreter/IR path AND the production VM path, and
///     must NOT decline a function/script to the interpreter merely because it contains <c>debugger;</c>.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class DebuggerStatementTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private void AssertRouted(string expectedLog)
    {
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(expectedLog, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DebuggerInFunctionBody_IsNoOp_AndRoutesThroughProduction()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ debugger; return 1; } f()");
        Assert.Equal(1d, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task DebuggerTopLevel_IsNoOp_AndRoutesThroughScriptRoute()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("debugger; var x = 5; x;");
        Assert.Equal(5d, result);
        AssertRouted("unified-bytecode-production-fast-path script");
    }

    [Fact]
    public async Task DebuggerInLoopBody_DoesNotChangeResults()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(){ var s = 0; for (var i = 0; i < 3; i++) { debugger; s += i; } return s; } f()");
        Assert.Equal(3d, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task DebuggerBetweenStatements_PreservesOrderAndSideEffects()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(){ var a = []; a.push(1); debugger; a.push(2); debugger; a.push(3); return a.join(','); } f()");
        Assert.Equal("1,2,3", result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task DebuggerDoesNotAffectThis()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "var o = { x: 42, m: function(){ debugger; return this.x; } }; o.m();");
        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task DebuggerDoesNotAffectReturnsOrControlFlow()
    {
        await using var engine = CreateEngine();
        // debugger between a conditional return and a fallthrough must not alter control flow.
        var result = await engine.Evaluate(
            "function f(n){ if (n > 0) { debugger; return 'pos'; } debugger; return 'nonpos'; } f(1) + ':' + f(0);");
        Assert.Equal("pos:nonpos", result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task DebuggerBareTopLevel_DoesNotThrow()
    {
        await using var engine = CreateEngine();
        // A bare top-level `debugger;` must not be a ReferenceError (the pre-fix behavior).
        // The completion value of an empty-statement program is the JS `undefined` value.
        var result = await engine.Evaluate("debugger;");
        Assert.Same(Asynkron.JsEngine.Ast.Symbol.Undefined, result);
    }

    [Fact]
    public async Task DebuggerWithAutomaticSemicolonInsertion()
    {
        await using var engine = CreateEngine();
        // ASI: `debugger` on its own line (no explicit semicolon) is a valid no-op statement.
        var result = await engine.Evaluate("function f(){\n  debugger\n  return 7;\n} f()");
        Assert.Equal(7d, result);
    }

    [Fact]
    public async Task DebuggerIsReservedWord_CannotBeUsedAsVariableName()
    {
        await using var engine = CreateEngine();
        await Assert.ThrowsAsync<ParseException>(async () => await engine.Evaluate("var debugger = 1;"));
    }

    [Fact]
    public async Task DebuggerUsableAsPropertyName_AfterDot()
    {
        await using var engine = CreateEngine();
        // Reserved words remain valid as property names in member-access position.
        var result = await engine.Evaluate("var o = {}; o.debugger = 9; o.debugger;");
        Assert.Equal(9d, result);
    }

    [Fact]
    public async Task DebuggerUsableAsPropertyName_InObjectLiteralKey()
    {
        await using var engine = CreateEngine();
        // Reserved words remain valid as object-literal keys.
        var result = await engine.Evaluate("var o = { debugger: 11 }; o.debugger;");
        Assert.Equal(11d, result);
    }
}
