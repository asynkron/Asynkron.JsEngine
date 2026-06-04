using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Regression coverage for const reassignment enforcement inside routed (production unified-bytecode)
///     functions. A function/block-scope <c>const</c> binding that is reassigned at runtime must throw a
///     <c>TypeError</c>, the same way a top-level <c>const</c> does. Previously const-ness was only threaded
///     into the bytecode program for loop-head TDZ consts, so a routed function's own (or captured) const
///     binding was silently writable.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class SyncVmConstEnforcementTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ProductionFastPathLog = "unified-bytecode-production-fast-path";

    /// <summary>
    ///     Evaluates <paramref name="source" /> as an expression that captures the constructor name of any
    ///     thrown error (or "no-throw" when nothing is thrown), returning it as a string.
    ///     Wraps the body in an IIFE so a <c>return</c> in <paramref name="body" /> is legal.
    /// </summary>
    private static string WrapCaptureError(string body) =>
        $$"""
          (function () {
              try {
                  {{body}}
                  return "no-throw";
              } catch (e) {
                  return e && e.constructor ? e.constructor.name : ("" + e);
              }
          })();
          """;

    /// <summary>
    ///     Captures a thrown error's constructor name using a TOP-LEVEL try/catch (no enclosing function),
    ///     so any function declared in <paramref name="body" /> keeps its top-level routing characteristics.
    /// </summary>
    private static string WrapCaptureErrorTopLevel(string body) =>
        $$"""
          var __err = "no-throw";
          try {
              {{body}}
          } catch (e) {
              __err = e && e.constructor ? e.constructor.name : ("" + e);
          }
          __err;
          """;

    private void AssertRouted(string expectedLog) =>
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(expectedLog, StringComparison.Ordinal));

    // 1. Own const reassign throws TypeError, and the function routes through production.
    [Fact]
    public async Task OwnConstReassign_ThrowsTypeError_AndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            WrapCaptureErrorTopLevel("function f(){ const k=1; k=2; return k; } f();"));
        Assert.Equal("TypeError", result?.ToString());
        AssertRouted($"{ProductionFastPathLog} func=f");
    }

    // 2. Own const compound-assign / increment throws TypeError.
    [Fact]
    public async Task OwnConstCompoundAssign_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var plusEq = await engine.Evaluate(
            WrapCaptureError("function f(){ const k=1; k+=1; return k; } return f();"));
        Assert.Equal("TypeError", plusEq?.ToString());

        var inc = await engine.Evaluate(
            WrapCaptureError("function f(){ const k=1; k++; return k; } return f();"));
        Assert.Equal("TypeError", inc?.ToString());
    }

    // 3. Own let reassign still works (no false positive), function routes.
    [Fact]
    public async Task OwnLetReassign_Works_AndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ let n=1; n=2; return n; } f();");
        Assert.Equal(2d, Assert.IsType<double>(result));
        AssertRouted($"{ProductionFastPathLog} func=f");
    }

    // 4. Nested block const reassign throws TypeError.
    [Fact]
    public async Task NestedBlockConstReassign_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            WrapCaptureError("function f(){ { const k=1; k=2; } } f();"));
        Assert.Equal("TypeError", result?.ToString());
    }

    // 5. Captured const write throws TypeError; the owning function `mk` routes through production
    //    (its activation env slot for `k` is the one that must carry SlotFlags.Const).
    [Fact]
    public async Task CapturedConstWrite_ThrowsTypeError_AndOwnerRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            WrapCaptureErrorTopLevel("function mk(){ const k=1; function w(){ k=2; } return w; } mk()();"));
        Assert.Equal("TypeError", result?.ToString());
        AssertRouted($"{ProductionFastPathLog} func=mk");
    }

    // 6. Captured let write still works by-reference. The owning function `mk` (which holds the captured
    //    binding and would mark it const if it were const) routes through the production VM; the inner
    //    closure `inc` runs via the simple-ir activation path, so we assert the owner routed.
    [Fact]
    public async Task CapturedLetWrite_WorksByReference_AndOwnerRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function mk(){ let n=0; function inc(){ n++; return n; } return inc; } var f=mk(); ''+f()+f()+f();");
        Assert.Equal("123", result?.ToString());
        AssertRouted($"{ProductionFastPathLog} func=mk");
    }

    // 7. Loop-head const unchanged: read works, reassign still throws.
    [Fact]
    public async Task LoopHeadConst_Unchanged()
    {
        await using var engine = CreateEngine();
        var ok = await engine.Evaluate(
            WrapCaptureError("var s=0; for (const x of [1]) { s+=x; }"));
        Assert.Equal("no-throw", ok?.ToString());

        var thrown = await engine.Evaluate(
            WrapCaptureError("for (const x of [1]) { x=2; }"));
        Assert.Equal("TypeError", thrown?.ToString());
    }

    // 8. Catch-binding is reassignable (not const).
    [Fact]
    public async Task CatchBinding_IsReassignable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            WrapCaptureError("try { throw 1; } catch (e) { e = 2; }"));
        Assert.Equal("no-throw", result?.ToString());
    }

    // 9. TDZ unchanged: reading a const before initialization throws ReferenceError.
    [Fact]
    public async Task TdzConst_StillThrowsReferenceError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(){ try { return x; } catch (e) { return e.constructor.name; } const x=1; } f();");
        Assert.Equal("ReferenceError", result?.ToString());
    }

    // 10. Top-level const still throws (regression guard).
    [Fact]
    public async Task TopLevelConstReassign_StillThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            WrapCaptureError("const k=1; k=2;"));
        Assert.Equal("TypeError", result?.ToString());
    }

    // 11. Const declared then read works, function routes.
    [Fact]
    public async Task ConstDeclaredThenRead_Works_AndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ const k=5; return k*2; } f();");
        Assert.Equal(10d, Assert.IsType<double>(result));
        AssertRouted($"{ProductionFastPathLog} func=f");
    }
}
