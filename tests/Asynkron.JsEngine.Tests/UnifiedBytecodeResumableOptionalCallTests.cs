using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for B15 — OPTIONAL CALL dispatch inside generator/async bodies routed through the
///     resumable VM (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />). The prior optional
///     slices admitted the slot-identifier optional call (<c>f?.()</c>) and the named optional call
///     (<c>o?.m()</c>, <c>o.m?.()</c>). This slice extends the resumable VM to the two remaining optional
///     call shapes that REACH the opcode path:
///     <list type="bullet">
///         <item><c>o[k]?.()</c> — computed-member optional call (<c>PrepareComputedOptionalCallTarget</c>).</item>
///         <item><c>freeFn?.()</c> — free/dynamic-identifier optional call
///         (<c>PrepareDynamicIdentifierOptionalCallTarget</c>), resolved against the calling environment
///         threaded onto <see cref="UnifiedBytecodeResumeState" />.</item>
///     </list>
///     Each handler is the literal twin of the sync VM's, reusing <c>GetComputedCallTargetValue</c> /
///     <c>PrepareDynamicIdentifierCallTarget</c> + <c>ExecutePreparedCall</c>; the short-circuit is realized
///     by the chain-end jump (the replaced undefined is the final call result), which survives
///     yield/await because the program counter / operand stack are restored from the resume state.
///
///     Each proof asserts (a) ROUTING — eligibility via <c>EvaluateResumable</c> for the opcode-set gate,
///     plus the resumable fast-path log for the end-to-end run (a fall-back to the interpreter fails the
///     test) — and (b) the correct runtime result, including the adversarial nullish short-circuit that
///     straddles a suspension and the stale-flag guard after a resume.
///
///     The OPTIONAL-COMPUTED call <c>o?.[k]()</c> is intentionally NOT covered here: its leading optional
///     hop lowers to a <c>JumpIfNullish</c> that the shared production-plan walk declines as
///     <see cref="UnifiedBytecodeProductionDeclineCode.OptionalChainDependency" /> BEFORE the opcode
///     allowlist, so it never reaches the resumable VM. <see cref="EvaluateResumable_OptionalComputedHopCall_StillDeclinesAtPlanWalk" />
///     pins that documented boundary.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableOptionalCallTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // The gate: a generator that uses a computed-member optional call (o[k]?.()) AND a free-identifier
    // optional call (freeFn?.()) between yields is admitted, and the admitted program actually carries the
    // two newly admitted optional-call opcodes (proving the slice expanded eligibility to the optional-call
    // tier rather than admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_ComputedAndDynamicOptionalCallBetweenYields_AdmitsOptionalCallOpcodes()
    {
        var plan = GetFunctionPlan("""
            function* g(o, k) {
                yield o[k]?.();
                yield freeFn?.();
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.None, result.Code);

        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PrepareComputedOptionalCallTarget);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PrepareDynamicIdentifierOptionalCallTarget);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    // End-to-end generator: a computed-member optional call (o[k]?.()) BETWEEN two yields routes through
    // the resumable fast path with a non-nullish method and produces the right value. The operand stack and
    // slots survive the suspension that precedes the optional call.
    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedOptionalCall_NonNullish_RoutesResumableAndProducesValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o, k) {
                yield 1;
                yield o[k]?.(3);
            }

            var it = g({ m: function (n) { return n * 7; } }, "m");
            var first = it.next().value;
            var second = it.next().value;
            first + "|" + second;
            """);

        // o["m"]?.(3) = 3 * 7 = 21.
        Assert.Equal("1|21", result);
        AssertGeneratorFastPath("g", argc: 2);
    }

    // Adversarial nullish: the computed method is missing (o[k] is undefined) so o[k]?.() short-circuits to
    // undefined and NEVER invokes. The short-circuit straddles a yield: the receiver/key are evaluated
    // before the first yield's resume and the chain end is after it.
    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedOptionalCall_NullishMethodAcrossYield_ShortCircuitsToUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o, k) {
                yield 1;
                yield o[k]?.();
                yield 3;
            }

            var it = g({ present: function () { return 99; } }, "missing");
            var a = it.next().value;
            var b = it.next().value;
            var c = it.next().value;
            a + "|" + (b === undefined) + "|" + c;
            """);

        // o["missing"] is undefined -> o["missing"]?.() short-circuits to undefined (never invokes); the
        // surrounding yields are unaffected.
        Assert.Equal("1|true|3", result);
        AssertGeneratorFastPath("g", argc: 2);
    }

    // End-to-end generator: a free/dynamic-identifier optional call (freeFn?.()) BETWEEN two yields routes
    // through the resumable fast path and invokes the live binding when non-nullish.
    [Fact(Timeout = 5000)]
    public async Task GeneratorDynamicOptionalCall_NonNullish_RoutesResumableAndProducesValue()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var freeFn = function () { return 55; };
            function* g() {
                yield 1;
                yield freeFn?.();
            }

            var it = g();
            var first = it.next().value;
            var second = it.next().value;
            first + "|" + second;
            """);

        Assert.Equal("1|55", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial nullish: the free binding is null so freeFn?.() short-circuits to undefined across a yield
    // and never invokes. A null callee must NOT throw a TypeError — the optional call swallows it.
    [Fact(Timeout = 5000)]
    public async Task GeneratorDynamicOptionalCall_NullishBindingAcrossYield_ShortCircuitsToUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var freeFn = null;
            function* g() {
                yield 1;
                yield freeFn?.();
                yield 3;
            }

            var it = g();
            var a = it.next().value;
            var b = it.next().value;
            var c = it.next().value;
            a + "|" + (b === undefined) + "|" + c;
            """);

        // freeFn is null -> freeFn?.() short-circuits to undefined (never invokes); yields unaffected.
        Assert.Equal("1|true|3", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Live-binding adversary: the free binding is reassigned to a callable BETWEEN the suspension and the
    // resumed optional call. A resumed step must observe the CURRENT binding (resolution against the live
    // calling environment), so the optional call invokes the newly assigned function.
    [Fact(Timeout = 5000)]
    public async Task GeneratorDynamicOptionalCall_BindingReassignedAcrossYield_InvokesCurrentBinding()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            var freeFn = null;
            function* g() {
                yield 1;
                yield freeFn?.();
            }

            var it = g();
            var a = it.next().value;       // null binding captured here, before the optional call
            freeFn = function () { return 77; };
            var b = it.next().value;       // resumes; must read the CURRENT (reassigned) binding
            a + "|" + b;
            """);

        // The optional call resolves freeFn live: after reassignment it invokes and returns 77.
        Assert.Equal("1|77", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial: an optional-call callee that THROWS must surface as a thrown exception on the resumed
    // step, never a swallowed undefined. The optional short-circuit only guards a NULLISH callee — a
    // present-but-throwing callee still runs and propagates abruptly.
    [Fact(Timeout = 5000)]
    public async Task GeneratorComputedOptionalCalleeThrows_SurfacesThrowOnResumedStep()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o, k) {
                yield 1;
                yield o[k]?.();
                yield 3;
            }

            var it = g({ boom: function () { throw new Error("kaboom"); } }, "boom");
            var first = it.next().value;
            var caught = "none";
            try {
                it.next();
            } catch (e) {
                caught = e.message;
            }
            var afterDone = it.next().done;
            first + "|" + caught + "|" + afterDone;
            """);

        Assert.Equal("1|kaboom|true", result);
        AssertGeneratorFastPath("g", argc: 2);
    }

    // End-to-end async: a computed-member optional call sits AFTER an await — it dispatches from a resumed
    // frame. The non-nullish method invokes; the nullish-receiver-key form short-circuits. Mirrors the
    // admitted async optional-chain shape (parameter receiver, param-resolved key).
    [Fact(Timeout = 5000)]
    public async Task AsyncComputedOptionalCall_AcrossAwait_RoutesResumableAndResolves()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(o, k, gate) {
                await gate;                  // suspension before the optional call
                return o[k]?.();
            }

            run(
                { m: function () { return 14; } },
                "m",
                Promise.resolve(0)).then(value => asyncResult = value);
            asyncResult;
            """);

        // o["m"]?.() invokes and returns 14 after the await — dispatched from the resumed frame.
        Assert.Equal(14d, result);
        AssertAsyncFastPath("run", argc: 3);
    }

    // Documented boundary: the OPTIONAL-COMPUTED call `o?.[k]()` (leading optional hop on the receiver) is a
    // DIFFERENT shape from `o[k]?.()`. Its hop lowers to a JumpIfNullish that the shared production-plan
    // walk declines as OptionalChainDependency BEFORE the opcode allowlist, so it never reaches the
    // resumable VM. This pins the "admit only what reaches the path; note the rest" boundary for B15.
    [Fact]
    public void EvaluateResumable_OptionalComputedHopCall_StillDeclinesAtPlanWalk()
    {
        var plan = GetFunctionPlan("""
            function* g(o, k) {
                yield 1;
                yield o?.[k]();
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
        Assert.Equal(UnifiedBytecodeProductionDeclineCode.OptionalChainDependency, result.Code);
    }

    private void AssertGeneratorFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableGeneratorFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private void AssertAsyncFastPath(string functionName, int argc) =>
        Assert.Contains(CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(
                $"{ResumableAsyncFastPathLog} func={functionName} argc={argc}",
                StringComparison.Ordinal));

    private static ExecutionPlan GetFunctionPlan(string source, string functionName)
    {
        var pipeline = AstTestHelpers.ParseAndAnalyze(source);
        var declaration = Assert.IsType<FunctionDeclaration>(pipeline.Analyzed.Body
            .Single(node => node is FunctionDeclaration f && f.Name?.Name == functionName));
        var cache = ((IAstCacheable<ExecutionPlanCache>)declaration.Function).GetOrCreateCache();
        Assert.True(cache.Succeeded, cache.FailureReason);
        return Assert.IsType<ExecutionPlan>(cache.Plan);
    }
}
