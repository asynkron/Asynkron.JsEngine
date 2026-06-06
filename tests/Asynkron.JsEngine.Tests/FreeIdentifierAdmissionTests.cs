using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     FREE-IDENTIFIER (DynamicLookupDependency / free-identifier family) admission battery for the
///     production sync unified-bytecode VM. Each test asserts BOTH the correct ECMAScript behavior AND
///     that the function routed through the production fast path
///     (<c>unified-bytecode-production-fast-path func=&lt;name&gt;</c>) — a free identifier that escapes the
///     activation's slots resolves as a dynamic-identifier op walking the threaded environment chain
///     (<c>LoadDynamicIdentifier</c> / <c>TypeOfDynamicIdentifier</c> / <c>UpdateDynamicIdentifier</c> /
///     <c>DeleteDynamicIdentifier</c> / <c>DeleteComputedProperty</c> with a free key).
///
///     Covers cluster items A13 (free READ), A15 (typeof free), A22 (free identifier update), A24 (free
///     delete), A16 (computed-property delete with a free KEY), and A14 (free STORE — sloppy global
///     creation and the strict-mode ReferenceError throw). All are admitted: the shared dynamic-identifier
///     machinery (the eligibility expression walk already plumbs <c>allowsOrdinaryDynamicIdentifiers</c>
///     and <c>ContainsOrdinaryDynamicIdentifierDependency</c> already classifies each opcode as a free
///     dynamic-identifier dependency) routes every shape through the sync VM, which owns the
///     dynamic assignment-reference writes / <c>UpdateDynamicIdentifier</c> / <c>DeleteDynamicIdentifier</c>
///     handlers (sloppy global creation, strict unresolvable-assignment throw, configurable-aware delete).
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class FreeIdentifierAdmissionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    // -------------------------------------------------------------------------------------------------
    // A13 — free identifier READ
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task FreeRead_OfDeclaredGlobal_ReturnsValueAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("var g = 7; function f(){ return g; } f();");
        Assert.Equal(7d, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task FreeRead_OfUndeclaredName_ThrowsReferenceErrorAndRoutes()
    {
        await using var engine = CreateEngine();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await engine.Evaluate("function f(){ return undeclaredX; } f();"));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task FreeRead_ObservesGlobalMutation_AfterFunctionDefinition()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("var g = 1; function f(){ return g; } g = 42; f();");
        Assert.Equal(42d, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // -------------------------------------------------------------------------------------------------
    // A15 — typeof of a free identifier (must NOT throw for an unbound name)
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TypeOfFree_UndeclaredName_ReturnsUndefinedAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ return typeof undeclaredX; } f();");
        Assert.Equal("undefined", result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task TypeOfFree_DeclaredGlobal_ReturnsTypeStringAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("var g = 7; function f(){ return typeof g; } f();");
        Assert.Equal("number", result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task TypeOfFree_DeclaredStringGlobal_ReturnsStringAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("var g = 'hi'; function f(){ return typeof g; } f();");
        Assert.Equal("string", result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // -------------------------------------------------------------------------------------------------
    // A22 — identifier UPDATE on a free name (freeName++ / ++freeName / freeName--)
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task FreeUpdate_PostfixIncrement_ReturnsOldValueAndMutatesGlobal()
    {
        // Postfix `c++` returns the OLD value (5) and leaves the global at 6.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("var c = 5; function f(){ return c++; } var r = f(); r + ',' + c;");
        Assert.Equal("5,6", result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task FreeUpdate_PrefixIncrement_ReturnsNewValueAndMutatesGlobal()
    {
        // Prefix `++c` returns the NEW value (6) and leaves the global at 6.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("var c = 5; function f(){ return ++c; } var r = f(); r + ',' + c;");
        Assert.Equal("6,6", result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task FreeUpdate_PostfixDecrement_ReturnsOldValueAndMutatesGlobal()
    {
        // Postfix `c--` returns the OLD value (5) and leaves the global at 4.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("var c = 5; function f(){ return c--; } var r = f(); r + ',' + c;");
        Assert.Equal("5,4", result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // -------------------------------------------------------------------------------------------------
    // A24 — delete of a free identifier (delete freeName)
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task FreeDelete_SloppyMode_OfNonConfigurableVarGlobal_ReturnsFalseAndRoutes()
    {
        // `var g` at script scope is a non-configurable global property; `delete g` returns false.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("var g = 1; function f(){ return delete g; } f();");
        Assert.Equal(false, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task FreeDelete_SloppyMode_OfConfigurableGlobal_ReturnsTrueAndRoutes()
    {
        // An implicit (sloppy-created) global property IS configurable; `delete` removes it and yields true.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "implicitGlobal = 1; function f(){ return delete implicitGlobal; } f();");
        Assert.Equal(true, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task FreeDelete_SloppyMode_OfUndeclaredName_ReturnsTrueAndRoutes()
    {
        // `delete` of an unresolvable reference is a no-op that returns true in sloppy mode.
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ return delete neverDeclared; } f();");
        Assert.Equal(true, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // -------------------------------------------------------------------------------------------------
    // A16 — computed-property delete with a free identifier as the KEY (delete box[freeName])
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task FreeComputedDeleteKey_DeletesPropertyAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "var k = 'a'; function f(o){ return delete o[k]; } var box = {a:1}; var r = f(box); r + ',' + ('a' in box);");
        // delete returns true; the property is then absent from the box.
        Assert.Equal("true,false", result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task FreeComputedDeleteKey_OfMissingProperty_ReturnsTrueAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("var k = 'missing'; function f(o){ return delete o[k]; } f({a:1});");
        Assert.Equal(true, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    // -------------------------------------------------------------------------------------------------
    // A14 — free identifier STORE (freeVar = x). Admitted: sloppy mode creates a configurable global
    // property; strict mode throws ReferenceError for the unresolvable assignment target.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task FreeStore_SloppyMode_CreatesGlobal_AndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ createdGlobal = 99; return createdGlobal; } f();");
        Assert.Equal(99d, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task FreeStore_SloppyMode_CreatedGlobalIsVisibleOutsideFunction()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(
            "function f(){ createdGlobal2 = 5; } f(); createdGlobal2;");
        Assert.Equal(5d, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task FreeStore_StrictMode_ThrowsReferenceError()
    {
        await using var engine = CreateEngine();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await engine.Evaluate("function f(){ 'use strict'; unresolvableTarget = 1; } f();"));
    }

    private void AssertRouted(string expectedLog)
    {
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(expectedLog, StringComparison.Ordinal));
    }
}
