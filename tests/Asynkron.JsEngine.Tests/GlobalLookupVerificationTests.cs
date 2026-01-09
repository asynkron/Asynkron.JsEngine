// Verification tests for bug #481 fix: Global lookups work with child environment fix
using Asynkron.JsEngine.Ast;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests to verify that the fix for bug #481 works correctly.
/// The fix prevents synthetic slots from overwriting GlobalEnvironment slots (like Symbol.This).
/// </summary>
[Category(TestCategories.Debugging)]
public sealed class GlobalLookupVerificationTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task ForOfWithUndefinedDestructuring_ShouldNotThrow()
    {
        // Test case 1: for (var [_] of [[undefined]]) {} - should not throw
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("for (var [_] of [[undefined]]) {}");
        // Should complete without throwing ReferenceError
        Assert.Equal(Symbol.Undefined, result);
    }

    [Fact]
    public async Task ForOfWithGlobalObject_ShouldNotThrow()
    {
        // Test case 2: for (var x of [JSON]) {} - should not throw
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("for (var x of [JSON]) {}");
        // Should complete without throwing ReferenceError
        Assert.Equal(Symbol.Undefined, result);
    }

    [Fact]
    public async Task UndefinedAssignment_ShouldStillWork()
    {
        // Test case 3: var x = undefined; x; - should still work
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("var x = undefined; x;");
        Assert.Equal(Symbol.Undefined, result);
    }

    [Fact]
    public async Task GlobalObjectLookupInForOf_ShouldWork()
    {
        // Additional verification: accessing other global objects
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var objects = [];
            for (var obj of [JSON, Math, Array]) {
                objects.push(obj);
            }
            objects.length;
        """);
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task UndefinedInDestructuringDefault_ShouldWork()
    {
        // Verify undefined works in destructuring defaults
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var [x = undefined] = [];
            x;
        """);
        Assert.Equal(Symbol.Undefined, result);
    }
}
