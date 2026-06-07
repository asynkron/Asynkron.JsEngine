using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     A7 (burn-down): admission of BASE-class constructor activations with GENERAL bodies into the
///     production unified-bytecode sync VM.
///
///     Investigation finding (locked in by these tests): the base-class-constructor activation predicate
///     (<c>CanUseProductionUnifiedBytecodeBaseClassConstructorActivation</c>) already gates on the
///     activation-slot shape (<c>CanUseSimpleIrActivationPlanShape</c>), NOT on a
///     <c>SimpleReturnProgram</c>-only body. So a base-class constructor with a general multi-statement
///     FLAT body — and even a nested-lexical-scope body — already routes through the production VM, and
///     does so IDENTICALLY to an ordinary function constructor with the same body. The decline boundary
///     that remains (e.g. a <c>this.y = b*2</c> binary-RHS property store) is a SHARED property-write-RHS
///     limitation in the production eligibility expression walker that applies to every function shape, NOT
///     a class-constructor-specific gate — so it is out of scope for the class-ctor predicate and is left
///     declined (a correct decline beats a forced half-correct admission).
///
///     Each test asserts the correct runtime RESULT and the production routing decision (routed vs not),
///     so a future change to the shared walker or the class-ctor predicate that shifts the boundary will
///     surface here as a deliberate, reviewed delta rather than a silent regression.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class ClassConstructorActivationAdmissionTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private bool Routed(string functionName) =>
        CurrentLogger!.Collector.Snapshot().Any(record =>
            record.Message.Contains(
                $"unified-bytecode-production-fast-path func={functionName}",
                StringComparison.Ordinal));

    // ----- ADMITTED: base-class ctor, simple params, multi-statement flat body -----

    [Fact(Timeout = 5000)]
    public async Task BaseCtor_TwoPlainPropertyStores_RoutesAndInitializesThis()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C { constructor(a, b) { this.x = a; this.y = b; } }
            var c = new C(3, 4);
            c.x + "," + c.y;
            """);

        Assert.Equal("3,4", result);
        Assert.True(Routed("C"), "base-class ctor with two plain property stores should route through the production VM");
    }

    [Fact(Timeout = 5000)]
    public async Task BaseCtor_LocalDeclarationThenPlainStore_Routes()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class C { constructor(n) { let t = n + 1; this.v = t; } }
            new C(4).v;
            """);

        Assert.Equal(5d, result);
        Assert.True(Routed("C"), "base-class ctor with a local declaration feeding a plain store should route");
    }

    [Fact(Timeout = 5000)]
    public async Task BaseCtor_NewTargetInsideBody_IsTheClassAndRoutes()
    {
        await using var engine = CreateEngine();
        // new.target inside a constructor invoked via `new C()` is the class C itself (not undefined). A
        // PLAIN store of new.target (`this.nt = new.target`) routes; the comparison `c.nt === C` happens in
        // a separate top-level statement so the ctor program stays a plain-store body. (Comparing inside the
        // ctor — `new.target === C` — declines because it is BOTH a binary-RHS store AND a free-name `C`
        // dynamic lookup, neither class-ctor-specific.)
        var result = await engine.Evaluate("""
            class C { constructor() { this.nt = new.target; } }
            var c = new C();
            c.nt === C;
            """);

        Assert.Equal(true, result);
        Assert.True(Routed("C"), "base-class ctor storing new.target plainly should route and observe the class as new.target");
    }

    [Fact(Timeout = 5000)]
    public async Task BaseCtor_NestedLexicalScopeBody_RoutesAndIsCorrect()
    {
        await using var engine = CreateEngine();
        // A nested if-block with its own `let` reading/writing an outer-scope local. For a base ctor the
        // `this`-property store resolves through the receiver, so the captured-name shadowing hazard that
        // bounds the closure/arrow Stage-0 lifts (HasOnlyRootFlatSlotMappings) does NOT apply here, and the
        // nested-scope body is admitted AND correct.
        var result = await engine.Evaluate("""
            class C { constructor(n) { let s = 0; if (n > 0) { let t = n + 1; s = t * t; } this.v = s; } }
            new C(2).v;
            """);

        Assert.Equal(9d, result);
        Assert.True(Routed("C"), "base-class ctor with a nested lexical scope should still route (no captured-name hazard)");
    }

    [Fact(Timeout = 5000)]
    public async Task BaseCtor_NestedBlockLetTdz_ThrowsReferenceErrorAndRoutes()
    {
        await using var engine = CreateEngine();
        // Inner `let x = x * 10;` reads `x` in its own block's TDZ -> ReferenceError. Verifies the routed
        // path preserves TDZ semantics for nested-scope ctor bodies.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await engine.Evaluate("""
                class C { constructor(x) { { let x = x * 10; this.inner = x; } this.outer = x; } }
                new C(2);
                """));

        Assert.True(Routed("C"), "base-class ctor whose nested block hits TDZ should still route (and throw)");
    }

    [Fact(Timeout = 5000)]
    public async Task BaseCtor_SingleStore_RegressionGuard_StillRoutes()
    {
        await using var engine = CreateEngine();
        // Pre-existing simple class ctor — guards against a regression that would decline the already-admitted shape.
        var result = await engine.Evaluate("""
            class C { constructor(value) { this.value = value; } }
            new C(42).value;
            """);

        Assert.Equal(42d, result);
        Assert.True(Routed("C"), "the pre-existing single-store base-class ctor must keep routing");
    }

    // ----- DECLINED (correctly): out-of-scope shapes that must NOT silently route -----

    [Fact(Timeout = 5000)]
    public async Task DerivedCtor_WithSuperCall_RoutesAndIsCorrect()
    {
        await using var engine = CreateEngine();
        // A derived constructor with super(...) now ROUTES through the production VM and computes the
        // correct instance (the complex-RHS property-write admission lifted the body-shape blocker; super
        // is threaded correctly, and `this`-before-`super` TDZ is still enforced — see SuperFieldCtor battery).
        var result = await engine.Evaluate("""
            class B { constructor(x) { this.x = x; } }
            class D extends B { constructor(x) { super(x); this.y = x * 2; } }
            var d = new D(5);
            d.x + "," + d.y;
            """);

        Assert.Equal("5,10", result);
        Assert.True(Routed("D"), "a derived (super) constructor now routes through the production VM");
    }

    [Fact(Timeout = 5000)]
    public async Task BaseCtor_WithInstanceFieldInitializer_RoutesAndIsCorrect()
    {
        await using var engine = CreateEngine();
        // An instance-field initializer ctor now ROUTES — fields initialize (in order, before the body) and
        // the body reads them correctly through the receiver.
        var result = await engine.Evaluate("""
            class C { f = 1; constructor() { this.g = this.f + 1; } }
            new C().g;
            """);

        Assert.Equal(2d, result);
        Assert.True(Routed("C"), "a class ctor with an instance-field initializer now routes");
    }

    [Fact(Timeout = 5000)]
    public async Task BaseCtor_WithDirectPrivateFieldWrite_RoutesAndInitializesPrivateState()
    {
        await using var engine = CreateEngine();
        // A7: private-name constructor activation is admitted when the constructor body uses an already
        // supported private mutation shape. Private brand/field initialization runs before constructor
        // bytecode, and the VM enters the constructor's private-name scope before executing the write.
        var result = await engine.Evaluate("""
            class C { #p; constructor(v) { this.#p = v; } getP() { return this.#p; } }
            new C(10).getP();
            """);

        Assert.Equal(10d, result);
        Assert.True(Routed("C"), "a base-class ctor with direct private field write should route");
    }

    [Fact(Timeout = 5000)]
    public async Task BaseCtor_WithPrivateReadAsNestedOperand_DeclinesUnderA51f5ButIsCorrect()
    {
        await using var engine = CreateEngine();
        // This is no longer an A7 constructor-activation boundary. It remains an A51f5 expression gap:
        // private reads used as nested value operands are still outside the admitted private-neighbor
        // expression subset, so the constructor correctly falls back while preserving semantics.
        var result = await engine.Evaluate("""
            class C { #p; constructor(v) { this.#p = v; this.q = this.#p + 1; } }
            new C(10).q;
            """);

        Assert.Equal(11d, result);
        Assert.False(Routed("C"), "private read as a nested RHS operand still belongs to A51f5");
    }

    [Fact(Timeout = 5000)]
    public async Task BaseCtor_WithPrivateBrandOnly_RoutesAndInitializesPrivateState()
    {
        await using var engine = CreateEngine();
        // Brand-only private state now proves the constructor bridge itself owns private-name class state;
        // any remaining private-expression declines are expression-shape gaps, not activation quarantine.
        var result = await engine.Evaluate("""
            class C { #p = 1; constructor(v) { this.q = v; } getP() { return this.#p; } }
            var c = new C(10);
            c.q + c.getP();
            """);

        Assert.Equal(11d, result);
        Assert.True(Routed("C"), "a private-name base ctor with brand-only state should route");
    }

    [Fact(Timeout = 5000)]
    public async Task DerivedCtor_WithPrivateFieldWrite_RoutesAndInitializesPrivateState()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            class B { constructor(v) { this.base = v; } }
            class D extends B { #p; constructor(v) { super(v); this.#p = v; } getP() { return this.#p; } }
            var d = new D(10);
            d.base + d.getP();
            """);

        Assert.Equal(20d, result);
        Assert.True(Routed("D"), "a derived ctor with direct private field write should route");
    }

    [Fact(Timeout = 5000)]
    public async Task DerivedCtor_WithPrivateBrandOnly_RoutesAndInitializesPrivateState()
    {
        await using var engine = CreateEngine();
        // Mirrors the base-constructor bridge: after super(...), pending instance initialization applies the
        // private brand/fields before the remaining constructor bytecode observes the initialized receiver.
        var result = await engine.Evaluate("""
            class B { constructor(v) { this.base = v; } }
            class D extends B { #p = 1; constructor(v) { super(v); this.q = v + 1; } getP() { return this.#p; } }
            var d = new D(10);
            d.base + d.q + d.getP();
            """);

        Assert.Equal(22d, result);
        Assert.True(Routed("D"), "a private-name derived ctor with brand-only state should route");
    }

    [Fact(Timeout = 5000)]
    public async Task BaseCtor_BinaryRhsPropertyStore_RoutesAndIsCorrect()
    {
        await using var engine = CreateEngine();
        // `this.y = b * 2` has a binary-op RHS. The shared property-write-RHS candidate now admits any
        // already-admitted value-producing RHS expression (complex-RHS admission), so this routes — for any
        // function shape, not just class ctors.
        var result = await engine.Evaluate("""
            class C { constructor(b) { this.y = b * 2; } }
            new C(4).y;
            """);

        Assert.Equal(8d, result);
        Assert.True(Routed("C"), "a binary-RHS property store now routes via the widened property-write gate");
    }

    // ----- Parity guard: identical bodies route identically for class ctor vs ordinary fn ctor -----

    [Fact(Timeout = 5000)]
    public async Task BinaryRhsStore_RoutesForOrdinaryFunctionConstructorToo()
    {
        await using var engine = CreateEngine();
        // Proves the binary-RHS-store admission is NOT class-ctor-specific: an ordinary function constructor
        // with the same body routes the same way.
        var result = await engine.Evaluate("""
            function F(b) { this.y = b * 2; }
            new F(4).y;
            """);

        Assert.Equal(8d, result);
        Assert.True(Routed("F"), "an ordinary function ctor with a binary-RHS store also routes (shared gate widened)");
    }
}
