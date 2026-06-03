using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     Proof pack for slot INCREMENT / DECREMENT (<c>x++</c>, <c>x--</c>, <c>++x</c>, <c>--x</c>) inside
///     the resumable VM (<see cref="UnifiedBytecodeVirtualMachine.ExecuteResumable" />) — burn-down item
///     B8. Before this slice the resumable instruction allowlist
///     (<c>IsSupportedResumableInstruction</c>) omitted <see cref="IncrementSlotInstruction" /> and the
///     opcode allowlist (<c>TryFindUnsupportedResumableOpcode</c>) omitted
///     <see cref="UnifiedBytecodeOpCode.UpdateSlot" />, so any slot update in a generator / async body fell
///     back to the interpreter.
///
///     The admitted surface is deliberately bounded to <b>parameter and <c>var</c> slots</b>. Const-ness is
///     a runtime environment property the lowered plan does not preserve, and the resumable resume state
///     carries no const-slot metadata, so the resumable VM cannot reproduce the
///     <c>TypeError: Assignment to constant variable</c> that the sync VM raises for a <c>const</c> update.
///     Every update whose target is a lexical (<c>let</c>/<c>const</c>) slot is therefore declined and keeps
///     its interpreter route — see <c>EvaluateResumable_LetIncrement_StaysDeclined</c> and the end-to-end
///     <c>ConstIncrementInGenerator_StaysOnInterpreterAndThrows</c> negative proofs.
///
///     Each proof asserts (a) ROUTING — eligibility via <c>EvaluateResumable</c> plus, for the end-to-end
///     runs, the resumable fast-path log (a fall-back to the interpreter fails the test) — and (b)
///     correctness for the adversarial cases: postfix vs prefix value, decrement, a counter mutated across
///     several yields, an async update across an await, and a Symbol operand that must throw a TypeError
///     through the resumable Throw step.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class UnifiedBytecodeResumableSlotUpdateTests(ITestOutputHelper output)
    : InternalTestBase(output)
{
    private const string ResumableGeneratorFastPathLog = "unified-bytecode-resumable-generator-fast-path";
    private const string ResumableAsyncFastPathLog = "unified-bytecode-resumable-async-fast-path";

    // GATE: a generator that increments a `var` slot across a yield is admitted, and the admitted program
    // actually carries UpdateSlot (proving the slice expanded eligibility to the slot-update tier rather
    // than admitting an unrelated shape).
    [Fact]
    public void EvaluateResumable_VarIncrementAcrossYield_AdmitsUpdateSlot()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                var n = 1;
                yield n;
                n++;
                yield n;
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
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateSlot);
    }

    // GATE: an update on a PARAMETER slot (never const) is also admitted and carries UpdateSlot.
    [Fact]
    public void EvaluateResumable_ParameterIncrementAcrossYield_AdmitsUpdateSlot()
    {
        var plan = GetFunctionPlan("""
            function* g(p) {
                yield p;
                p++;
                yield p;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.True(result.IsEligible, result.Reason);
        Assert.Contains(
            result.Program.Instructions,
            static instruction => instruction.OpCode == UnifiedBytecodeOpCode.UpdateSlot);
    }

    // NEGATIVE GATE: an update on a LEXICAL (`let`) slot stays declined — the resumable VM cannot enforce
    // the const semantics a `let`/`const` slot might require, so it is kept off the fast path entirely.
    // This pins the boundary so a future change cannot silently route a `const` update through the
    // const-unaware resumable handler.
    [Fact]
    public void EvaluateResumable_LetIncrement_StaysDeclined()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                let i = 0;
                yield i;
                i++;
                yield i;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
    }

    // End-to-end: a postfix `var` increment across a yield routes through the resumable fast path and the
    // postfix yields the OLD value while the slot holds the NEW value at the next read.
    [Fact(Timeout = 5000)]
    public async Task GeneratorVarPostfixIncrementAcrossYield_RoutesResumableAndIsCorrect()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                var n = 10;
                yield n;
                yield n++;   // postfix: yields 10, then n becomes 11
                yield n;     // 11
            }

            var it = g();
            (it.next().value) + "|" + (it.next().value) + "|" + (it.next().value);
            """);

        Assert.Equal("10|10|11", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // End-to-end: a prefix increment yields the NEW value.
    [Fact(Timeout = 5000)]
    public async Task GeneratorVarPrefixIncrementAcrossYield_RoutesResumableAndIsCorrect()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                var n = 10;
                yield n;
                yield ++n;   // prefix: n becomes 11, yields 11
            }

            var it = g();
            (it.next().value) + "|" + (it.next().value);
            """);

        Assert.Equal("10|11", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // End-to-end: a decrement on a parameter slot across a yield.
    [Fact(Timeout = 5000)]
    public async Task GeneratorParameterDecrementAcrossYield_RoutesResumableAndIsCorrect()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g(p) {
                yield p;
                p--;
                yield p;
            }

            var it = g(5);
            (it.next().value) + "|" + (it.next().value);
            """);

        Assert.Equal("5|4", result);
        AssertGeneratorFastPath("g", argc: 1);
    }

    // End-to-end: a counter mutated across SEVERAL suspensions — the slot value must persist on the resume
    // state between every update/yield, not reset. Uses straight-line updates (no loop) so the generator
    // body stays inside the already-admitted resumable control-flow surface and the test isolates the
    // slot-update behavior under test.
    [Fact(Timeout = 5000)]
    public async Task GeneratorCounterAcrossMultipleYields_PersistsSlotState()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                var c = 0;
                c++;
                yield c;   // 1
                c++;
                yield c;   // 2
                c++;
                yield c;   // 3
            }

            var it = g();
            (it.next().value) + "" + (it.next().value) + (it.next().value);
            """);

        Assert.Equal("123", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // Adversarial: incrementing a Symbol-valued `var` slot must throw a TypeError through the resumable
    // Throw step (ToNumeric on a Symbol throws), not silently mutate to NaN.
    [Fact(Timeout = 5000)]
    public async Task GeneratorIncrementSymbolValue_ThrowsTypeError()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                var s = Symbol("x");
                yield 1;
                s++;        // ToNumeric(Symbol) throws TypeError
                yield 2;
            }

            var it = g();
            var first = it.next().value;   // 1
            var caught = "none";
            try {
                it.next();                 // runs the Symbol increment -> TypeError
            } catch (e) {
                caught = e.constructor.name;
            }
            first + "|" + caught;
            """);

        Assert.Equal("1|TypeError", result);
        AssertGeneratorFastPath("g", argc: 0);
    }

    // End-to-end async: an async function that increments a `var` slot after an await suspension routes
    // through the resumable fast path and returns the updated value.
    [Fact(Timeout = 5000)]
    public async Task AsyncIncrementAcrossAwait_RoutesResumableAndIsCorrect()
    {
        await using var engine = CreateEngine();
        var result = await engine.EvaluateAndAwait("""
            var asyncResult = undefined;
            async function run(start) {
                var n = start;
                await Promise.resolve(0);
                n++;
                return n;
            }

            run(40).then(value => asyncResult = value);
            asyncResult;
            """);

        Assert.Equal(41d, result);
        AssertAsyncFastPath("run", argc: 1);
    }

    // NEGATIVE end-to-end: a `const` increment in a generator must keep its interpreter route and still
    // throw the TypeError — the resumable fast-path log must be ABSENT (the const-unaware resumable handler
    // never runs), proving the lexical-slot decline holds end to end.
    [Fact(Timeout = 5000)]
    public async Task ConstIncrementInGenerator_StaysOnInterpreterAndThrows()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                const x = 1;
                yield x;
                try { x++; } catch (e) { yield "THROW:" + e.constructor.name; return; }
                yield "no-throw";
            }

            var it = g();
            (it.next().value) + "|" + (it.next().value);
            """);

        Assert.Equal("1|THROW:TypeError", result);
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal));
    }

    // GATE (isolates the fix): a generator that PLAIN-ASSIGNS a `const` slot (`x = 2`, no try/catch) must
    // be DECLINED by EvaluateResumable. The already-admitted StoreSlot opcode carries no const metadata, so
    // without the AssignmentSlotInstruction lexical-slot guard this program was wrongly eligible and `x = 2`
    // silently succeeded on the resumable fast path (yielding `1|2`). This is the direct, non-vacuous proof:
    // it is eligible without the guard and declined with it.
    [Fact]
    public void EvaluateResumable_ConstSlotAssignment_StaysDeclined()
    {
        var plan = GetFunctionPlan("""
            function* g() {
                const x = 1;
                yield x;
                x = 2;
                yield x;
            }
            """,
            "g");

        var result = UnifiedBytecodeProductionEligibility.EvaluateResumable(
            plan,
            new UnifiedBytecodeProductionActivationDescriptor(IsGenerator: true));

        Assert.False(result.IsEligible);
    }

    // NEGATIVE end-to-end (mirrors ConstIncrementInGenerator, for plain assignment): a `const`
    // reassignment in a generator keeps its interpreter route and throws TypeError; the resumable
    // fast-path log must be ABSENT.
    [Fact(Timeout = 5000)]
    public async Task ConstAssignmentInGenerator_StaysOnInterpreterAndThrows()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            function* g() {
                const x = 1;
                yield x;
                try { x = 2; } catch (e) { yield "THROW:" + e.constructor.name; return; }
                yield "no-throw";
            }

            var it = g();
            (it.next().value) + "|" + (it.next().value);
            """);

        Assert.Equal("1|THROW:TypeError", result);
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(ResumableGeneratorFastPathLog, StringComparison.Ordinal));
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
