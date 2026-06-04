using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     `delete` over an optional chain must short-circuit the WHOLE delete to `true` when an optional hop's
///     base is nullish, rather than attempting the delete on the nullish reference (which throws
///     `TypeError: Cannot delete property on null or undefined`). Regression for the compiler emitting the
///     optional short-circuit guard only when the delete member itself was optional, missing the case where
///     an EARLIER hop in the target chain is optional (`delete box?.[k1][k2]`).
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class OptionalChainDeleteTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Theory]
    // single-hop optional named delete
    [InlineData("function f(box){ return delete box?.x; } f(null);", true)]
    [InlineData("function f(box){ return delete box?.x; } f(undefined);", true)]
    // single-hop optional computed delete
    [InlineData("function f(box,k){ return delete box?.[k]; } f(null,'a');", true)]
    [InlineData("function f(box,k){ return delete box?.[k]; } f(undefined,'a');", true)]
    // optional hop EARLIER in the chain (the reported bug): outer hop not optional
    [InlineData("function f(box,k1,k2){ return delete box?.[k1][k2]; } f(null,'a','b');", true)]
    [InlineData("function f(box,k1,k2){ return delete box?.[k1][k2]; } f(undefined,'a','b');", true)]
    [InlineData("function f(box){ return delete box?.a.b; } f(null);", true)]
    public async Task DeleteOptionalChain_NullishBase_ShortCircuitsToTrue(string source, bool expected)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(source);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task DeleteOptionalChain_NonNullBase_DeletesAndReturnsTrue()
    {
        await using var engine = CreateEngine();
        // delete box?.x with a present property removes it and returns true.
        Assert.Equal(true, await engine.Evaluate(
            "function f(box){ var ok = delete box?.x; return ok && !('x' in box); } f({x:1});"));
        // delete box?.[k] removes the computed key.
        Assert.Equal(true, await engine.Evaluate(
            "function f(box,k){ var ok = delete box?.[k]; return ok && !(k in box); } f({a:1},'a');"));
        // delete box?.[k1][k2] removes the nested key on a present chain.
        Assert.Equal(true, await engine.Evaluate(
            "function f(box,k1,k2){ var ok = delete box?.[k1][k2]; return ok && !(k2 in box[k1]); } f({a:{b:1}},'a','b');"));
    }

    [Fact]
    public async Task Delete_NonOptional_NullishBase_StillThrows()
    {
        await using var engine = CreateEngine();
        // A NON-optional member delete on a genuinely nullish base must still throw (the fix must not
        // make every nullish-target delete return true).
        var result = await engine.Evaluate(
            "function f(box,k1,k2){ try { delete box[k1][k2]; return 'no-throw'; } catch (e) { return e.constructor.name; } } f({}, 'a', 'b');");
        Assert.Equal("TypeError", result);
    }
}
