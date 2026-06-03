using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for optional chains (<c>o?.a</c>, <c>o?.[k]</c>) and optional calls (<c>o?.m()</c>,
///     <c>f?.()</c>) inside generator/async bodies routed through the resumable VM
///     (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />). The two prior call-dispatch /
///     value-tier slices deferred these with the same reason: the resumable state had no equivalent of
///     the sync VM's stack-parallel short-circuit-flag array, so a flag set before a suspension could not
///     survive the resume. This slice stores that flag column on
///     <see cref="UnifiedBytecodeResumeState" /> (allocated when
///     <see cref="UnifiedBytecodeProgram.RequiresShortCircuitStackFlags" />), index-aligned with the
///     operand stack, so it persists across <c>yield</c>/<c>await</c> in lockstep.
///
///     Each proof asserts (a) ROUTING — eligibility via <c>EvaluateResumable</c> for the opcode-set gate,
///     plus the resumable fast-path log for the end-to-end run (a fall-back to the interpreter fails the
///     test) — and (b) the correct runtime result, including the adversarial cases the spec calls out:
///     a nullish short-circuit whose head is read BEFORE a suspension and whose chain end is AFTER it, and
///     a non-nullish optional read after a resume that must NOT inherit a stale short-circuit.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableOptionalChainTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // The gate: a generator that uses an optional read AND an optional call between yields is admitted,
    // and the admitted program actually carries the optional opcodes (proving the slice expanded
    // eligibility to the optional tier rather than admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_OptionalChainAndCallBetweenYields_AdmitsOptionalOpcodes()
    {
        var plan = GetFunctionPlan("""
            function* g(o) {
                yield o?.a;
                yield o?.m();
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.GetNamedPropertyOptional);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.PrepareNamedOptionalCallTarget);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.CallInvocationBoundary);
    }

    // End-to-end generator: an optional read (o?.a) and an optional method call (o?.m()) BETWEEN two
    // yields route through the resumable fast path with a non-nullish receiver and produce the right
    // values. The operand stack and slots survive the suspension that precedes each optional access.
    [Fact(Timeout = 5000)]
    public async Task GeneratorOptionalChainAndCall_NonNullish_RoutesResumableAndProducesValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                yield o?.a;
                yield o?.m();
            }

            var it = g({ a: 7, m: function () { return 42; } });
            var first = it.next().value;
            var second = it.next().value;
            first + "|" + second;
            """);

        Assert.Equal("7|42", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // THE adversarial case the spec demands: an optional chain whose HEAD is nullish (short-circuits to
    // undefined) where the short-circuit STRADDLES a yield. The head `o` is captured nullish before the
    // first yield; after resume, o?.a.b and o?.m() must both yield undefined (never throw on the nullish
    // base). This is exactly the flag/jump-state-must-survive-suspension case.
    [Fact(Timeout = 5000)]
    public async Task GeneratorOptionalChain_NullishHeadAcrossYield_ShortCircuitsToUndefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o) {
                yield 1;
                yield o?.a.b;
                yield o?.m();
                yield 4;
            }

            var it = g(null);
            var a = it.next().value;
            var b = it.next().value;
            var c = it.next().value;
            var d = it.next().value;
            a + "|" + (b === undefined) + "|" + (c === undefined) + "|" + d;
            """);

        // o is null: o?.a.b short-circuits to undefined (never throws reading .b off undefined),
        // o?.m() short-circuits to undefined (never invokes), and the surrounding yields are unaffected.
        Assert.Equal("1|true|true|4", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // Stale-flag adversary: within ONE resumable frame, a nullish optional chain (which sets the
    // short-circuit state) is immediately followed across a yield by a NON-nullish optional read on a
    // different receiver. The second read must NOT inherit the first chain's short-circuit — it must
    // actually read the property.
    [Fact(Timeout = 5000)]
    public async Task GeneratorOptionalChain_NonNullishAfterNullishAcrossYield_NoStaleShortCircuit()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(empty, full) {
                yield empty?.a.b;
                yield full?.a;
            }

            var it = g(null, { a: 99 });
            var first = it.next().value;
            var second = it.next().value;
            (first === undefined) + "|" + second;
            """);

        // empty?.a.b short-circuits to undefined; the SECOND optional read on `full` must NOT inherit
        // that short-circuit — it reads full.a === 99.
        Assert.Equal("true|99", result);
        AssertGeneratorFastPath("g", argc: 2);
    }

    // Optional computed read and optional identifier call between yields, non-nullish.
    [Fact(Timeout = 5000)]
    public async Task GeneratorOptionalComputedAndIdentifierCall_RoutesResumable()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(o, k, f) {
                yield o?.[k];
                yield f?.();
            }

            var it = g({ x: 5 }, "x", function () { return 11; });
            var first = it.next().value;
            var second = it.next().value;
            first + "|" + second;
            """);

        Assert.Equal("5|11", result);
        AssertGeneratorFastPath("g", argc: 3);
    }

    // End-to-end async: an optional read and an optional call sit BETWEEN/AFTER awaits. Both dispatch
    // from resumed frames; the nullish short-circuit straddles the first await.
    [Fact(Timeout = 5000)]
    public async Task AsyncOptionalChainAndCall_AcrossAwaits_RoutesResumableAndResolves()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(maybe, real, sink) {
                await maybe?.first;     // maybe is null -> optional read short-circuits across the await
                sink(real?.value);      // real.value === 4, optional read after the await
                await real?.gate;       // optional read of a resolved promise after a second suspension
                return real?.compute(); // real.compute() === 21, optional call after the await
            }

            var log = [];
            run(
                null,
                {
                    value: 4,
                    gate: Promise.resolve(0),
                    compute: function () { return 21; }
                },
                function (n) { log.push(n); }).then(value => asyncResult = value + "/" + log.join(","));
            asyncResult;
            """);

        // maybe is null so `maybe?.first` awaits undefined; sink(real?.value) logs 4; the trailing
        // optional call real?.compute() returns 21 from a resumed frame.
        Assert.Equal("21/4", result);
        AssertAsyncFastPath("run", argc: 3);
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
