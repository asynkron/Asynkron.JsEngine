using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public abstract class TestOptionalCatchBindingBase(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact]
    public async Task OptionalCatchBinding_BasicTest_ShouldCatchError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var caught = false;
            try {
                throw new Error('test');
            } catch {
                caught = true;
            }
            caught;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task OptionalCatchBinding_WithFinally_ShouldExecuteBoth()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var finallyRan = false;
            var catchRan = false;
            try {
                throw 'error';
            } catch {
                catchRan = true;
            } finally {
                finallyRan = true;
            }
            catchRan && finallyRan;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task OptionalCatchBinding_NoErrorThrown_ShouldSkipCatch()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var catchRan = false;
            try {
                // No error
            } catch {
                catchRan = true;
            }
            catchRan;
        ");
        Assert.Equal(false, result);
    }

    [Fact]
    public async Task OptionalCatchBinding_WithParameterStillWorks()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var errorMessage = '';
            try {
                throw new Error('with parameter');
            } catch (e) {
                errorMessage = e.message;
            }
            errorMessage;
        ");
        Assert.Equal("with parameter", result);
    }

    [Fact]
    public async Task OptionalCatchBinding_CannotAccessError()
    {
        await using var engine = CreateEngine();
        // When there's no catch parameter, the error variable should not be accessible
        var result = await engine.Evaluate(@"
            var hasE = false;
            try {
                throw new Error('test');
            } catch {
                try {
                    // Trying to access 'e' should throw ReferenceError
                    var x = e;
                } catch (err) {
                    hasE = false; // e is not defined
                }
            }
            !hasE; // Should be true because e was not accessible
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task OptionalCatchBinding_LexicalScope()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            var outer = 'outer';
            try {
                throw 'error';
            } catch {
                // Can access outer scope
                outer = 'modified';
            }
            outer;
        ");
        Assert.Equal("modified", result);
    }
}
