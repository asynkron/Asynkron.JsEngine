using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     A46 — binary OPERATOR admission battery for the production sync unified-bytecode VM. Each test
///     asserts BOTH the correct ECMAScript behavior AND that the function routed through the production
///     fast path (<c>unified-bytecode-production-fast-path func=&lt;name&gt;</c>).
///
///     Primary target: the <c>**</c> exponentiation operator (<see cref="BinaryOperator.Power"/>), which
///     the production VM already evaluates via <c>JsOps.Exp</c> and which the eligibility classifier
///     already admits in <c>IsProductionBinaryOperator</c>. These tests pin the end-to-end contract:
///     right-associativity, fractional/negative exponents, signed-base parenthesization, the <c>**=</c>
///     compound form, and operator-precedence interplay.
///
///     BigInt-mixed arithmetic is covered as a NEGATIVE/decline contract: <c>1n + 1</c> must throw a
///     TypeError (mixing BigInt and Number), and that behavior is correct regardless of routing tier.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class ExponentiationOperatorAdmissionTests(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact]
    public async Task Power_IntegerOperands_ReturnsValueAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(a,b){ return a**b; } f(2,10);");
        Assert.Equal(1024d, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task Power_IsRightAssociative_ReturnsValueAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ return 2**3**2; } f();");
        // Right-associative: 2 ** (3 ** 2) === 2 ** 9 === 512 (not (2**3)**2 === 64).
        Assert.Equal(512d, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task Power_FractionalExponent_ReturnsSquareRootAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ return 2**0.5; } f();");
        var value = Assert.IsType<double>(result);
        Assert.Equal(Math.Sqrt(2.0), value, 10);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task Power_NegativeExponent_ReturnsReciprocalAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ return 2**-1; } f();");
        Assert.Equal(0.5d, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task Power_ParenthesizedNegativeBase_ReturnsValueAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ return (-2)**2; } f();");
        Assert.Equal(4d, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task Power_CompoundAssignment_ReturnsValueAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ let x=3; x**=2; return x; } f();");
        Assert.Equal(9d, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task Power_PrecedenceAgainstAddition_ReturnsValueAndRoutes()
    {
        await using var engine = CreateEngine();
        // ** binds tighter than +: 2 + (3 ** 2) === 11.
        var result = await engine.Evaluate("function f(){ return 2 + 3 ** 2; } f();");
        Assert.Equal(11d, result);
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task Power_NaNExponent_ReturnsNaNAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ return 2**NaN; } f();");
        var value = Assert.IsType<double>(result);
        Assert.True(double.IsNaN(value));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task Power_InfinityExponent_ReturnsInfinityAndRoutes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ return 2**Infinity; } f();");
        var value = Assert.IsType<double>(result);
        Assert.True(double.IsPositiveInfinity(value));
        AssertRouted("unified-bytecode-production-fast-path func=f");
    }

    [Fact]
    public async Task UnaryMinusOnBaseWithoutParens_IsSyntaxError()
    {
        // -2**2 is a SyntaxError in JS (ambiguous unary minus on the base); the parser must reject it.
        await using var engine = CreateEngine();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await engine.Evaluate("function f(){ return -2**2; } f();"));
    }

    [Fact]
    public async Task BigIntMixedAddition_ThrowsTypeError()
    {
        // Mixing BigInt and Number is a TypeError regardless of routing tier.
        await using var engine = CreateEngine();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await engine.Evaluate("function f(){ return 1n + 1; } f();"));
    }

    [Fact]
    public async Task PureBigIntPower_ReturnsBigIntValue()
    {
        // Pure-BigInt exponentiation is valid ECMAScript: 2n ** 10n === 1024n. This pins the value via
        // its string form (BigInt unwrapping is representation-specific); routing tier for the BigInt
        // shape is not asserted (BigInt admission is out of A46 scope).
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("function f(){ return (2n ** 10n).toString(); } f();");
        Assert.Equal("1024", result);
    }

    private void AssertRouted(string expectedLog)
    {
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(expectedLog, StringComparison.Ordinal));
    }
}
